using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DofusRetroManager.Data;

namespace DofusRetroManager;

/// <summary>
/// Carte du livre : State 2 = rangée au classeur, 1 = possédée ailleurs (banque, inventaire), 0 = manquante.
/// ArtItem : objet dont l'icône est l'image de la carte (celui de la carte normale pour une brillante).
/// </summary>
public sealed record BookCard(int Id, int Number, string Name, int Rarity, int ArtItem, bool Shiny, int State, string Tip);

/// <summary>Famille du livre : cartes normales et brillantes, et combien sont rangées au classeur.</summary>
public sealed record BookFamily(int Id, string Name, int ItemId, int Done, int Owned, int Total, int ShinyDone, List<BookCard> Cards, List<BookCard> Shiny);

public sealed record BookRarity(int Rarity, int Done, int Total);

/// <summary>Livre de la collection TTG, comme celui du jeu : totaux, raretés, familles et leurs cartes.</summary>
public sealed record TtgBook(string Owner, int Done, int Owned, int Total, int ShinyDone, int ShinyTotal, int FamiliesDone, int FamiliesTotal,
    List<BookRarity> Rarities, List<BookFamily> Families);

public partial class MainWindow
{
    /// <summary>
    /// Livre de la collection du personnage choisi dans l'onglet TTG (ou de tous) : une carte est « au classeur » si elle y
    /// est rangée (message reçu à l'ouverture du classeur en jeu), « possédée » si elle est en banque ou en inventaire.
    /// </summary>
    TtgBook? BuildTtgBook()
    {
        if (_data == null || _data.TtgCards.Count == 0) return null;

        var character = CharacterChoice(TtgCharacter, ref _ttgCharsUpdating);
        var owned = OwnedItems(character).Select(x => x.Item.TemplateId).ToHashSet();
        var binder = _tracker.AllSnapshots().Where(x => character == null ? x.Character.Length > 0 || x == _tracker.State : x.Character == character)
            .SelectMany(x => x.Binder).ToHashSet();

        BookCard Card(TtgCard c)
        {
            int art = c.ItemId > 0 ? c.ItemId : _data.TtgCardItems.GetValueOrDefault(c.Number);
            int state = binder.Contains(c.Id) ? 2 : !c.Shiny && c.ItemId > 0 && owned.Contains(c.ItemId) ? 1 : 0;
            var stars = new string('★', Math.Max(1, c.Rarity));
            var tip = $"#{c.Number} {c.Name}{(c.Shiny ? " (brillante)" : "")} · {stars}\n"
                + (state == 2 ? "Rangée au classeur" : state == 1 ? "Possédée (banque ou inventaire), pas au classeur" : "Manquante");
            if (state < 2 && !c.Shiny && c.ItemId > 0 && BookCardPrice(c.ItemId) is { } price) tip += $" · prix {Kamas(price)}";
            return new BookCard(c.Id, c.Number, c.Name, c.Rarity, art, c.Shiny, state, tip);
        }

        var all = _data.TtgCards.Values.OrderBy(c => c.Number).Select(c => (c, b: Card(c))).ToList();
        var families = _data.TtgFamilies.Values.OrderBy(f => f.Id).Select(f =>
        {
            var normal = all.Where(x => x.c.Family == f.Id && !x.c.Shiny).Select(x => x.b).ToList();
            var shiny = all.Where(x => x.c.Family == f.Id && x.c.Shiny).Select(x => x.b).ToList();
            return new BookFamily(f.Id, f.Name, f.ItemId, normal.Count(b => b.State == 2), normal.Count(b => b.State > 0), normal.Count,
                shiny.Count(b => b.State == 2), normal, shiny);
        }).Where(f => f.Total > 0).ToList();
        var normals = all.Where(x => !x.c.Shiny).Select(x => x.b).ToList();
        var shinies = all.Where(x => x.c.Shiny).Select(x => x.b).ToList();
        return new TtgBook(character ?? "tous vos personnages",
            normals.Count(b => b.State == 2), normals.Count(b => b.State > 0), normals.Count,
            shinies.Count(b => b.State == 2), shinies.Count,
            families.Count(f => f.Done == f.Total), families.Count,
            normals.GroupBy(b => b.Rarity).OrderBy(g => g.Key).Select(g => new BookRarity(g.Key, g.Count(b => b.State == 2), g.Count())).ToList(),
            families);
    }

    /// <summary>Prix d'une carte manquante : HDV, sinon meilleur marchand de la période, sinon dernier prix marchand.</summary>
    double? BookCardPrice(int item)
    {
        var m = _merchants?.Prices.GetValueOrDefault(item);
        return UnitPrice(item) ?? m?.BestRecent(TtgDays) ?? m?.Last?.Price;
    }

    void TtgBook_Click(object sender, RoutedEventArgs e)
    {
        if (BuildTtgBook() is not { } book)
        {
            AddLog("Livre TTG : données des cartes pas encore chargées.");
            return;
        }
        new TtgBookWindow(book) { Owner = this }.Show();
    }
}

/// <summary>Fenêtre « Classeur » de l'onglet TTG (interface classique) : la collection présentée comme le livre du jeu.</summary>
public sealed class TtgBookWindow : Window
{
    readonly TtgBook _book;
    BookFamily? _family;
    bool _shiny;
    readonly WrapPanel _families = new();
    readonly WrapPanel _cards = new();
    readonly TextBlock _familyTitle = new() { FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _familyCount = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };

    public TtgBookWindow(TtgBook book)
    {
        _book = book;
        Title = $"Collection de {book.Owner} — Dofus Retro Manager";
        Width = 1180;
        Height = 760;
        MinWidth = 800;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(ForegroundProperty, "Text");
        SetResourceReference(FontFamilyProperty, "AppFont");
        FontSize = 13;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Page(LeftPage(), 0));
        grid.Children.Add(Page(RightPage(), 1));
        Content = grid;
        Select(book.Families.FirstOrDefault(f => f.Owned > 0) ?? book.Families.FirstOrDefault());
    }

    static Border Page(UIElement content, int column)
    {
        var page = new Border { Child = content, Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Margin = new Thickness(column == 0 ? 0 : 10, 0, 0, 0) };
        page.SetResourceReference(Border.BackgroundProperty, "Surface2");
        page.SetResourceReference(Border.BorderBrushProperty, "Border");
        Grid.SetColumn(page, column);
        return page;
    }

    static TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 10, 0, 6) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "AccentSolid");
        return t;
    }

    /// <summary>Ligne de progression : libellé court (ou étoiles), barre, « 4 / 464 ».</summary>
    static Grid Progress(string label, int done, int total, string tip)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), ToolTip = tip, Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        var bar = new ProgressBar { Maximum = Math.Max(1, total), Value = done, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);
        var count = new TextBlock { Text = $"{done} / {total}", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(count, 2);
        row.Children.Add(count);
        return row;
    }

    UIElement LeftPage()
    {
        var p = new StackPanel();
        var title = new TextBlock { Text = $"Collection de {_book.Owner}", FontSize = 20, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        p.Children.Add(title);
        p.Children.Add(Heading("Par types"));
        p.Children.Add(Progress("Classeur", _book.Done, _book.Total,
            $"Cartes rangées au classeur. {_book.Owned} possédée(s) en comptant la banque et l'inventaire."));
        p.Children.Add(Progress("Brillantes", _book.ShinyDone, _book.ShinyTotal, "Cartes brillantes rangées au classeur"));
        p.Children.Add(Progress("Familles", _book.FamiliesDone, _book.FamiliesTotal, "Familles complètes au classeur"));
        p.Children.Add(Heading("Par raretés"));
        foreach (var r in _book.Rarities)
            p.Children.Add(Progress(new string('★', Math.Max(1, r.Rarity)), r.Done, r.Total, $"Rareté {r.Rarity} : cartes rangées au classeur"));
        p.Children.Add(Heading("Par familles"));
        foreach (var f in _book.Families) _families.Children.Add(FamilyButton(f));
        p.Children.Add(_families);
        return new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Famille : icône de son paquet et petite barre de progression (nom et compte au survol).</summary>
    FrameworkElement FamilyButton(BookFamily f)
    {
        var box = new StackPanel { Width = 52 };
        box.Children.Add(new Image { Source = ItemIcons.Get(f.ItemId), Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center });
        box.Children.Add(new ProgressBar { Maximum = Math.Max(1, f.Total), Value = f.Done, Height = 6, Margin = new Thickness(4, 3, 4, 0) });
        var button = new ToggleButton
        {
            Content = box, Tag = f, Margin = new Thickness(0, 0, 4, 6), Padding = new Thickness(2), Cursor = Cursors.Hand,
            ToolTip = $"{f.Name} : {f.Done} / {f.Total} au classeur" + (f.Owned > f.Done ? $", {f.Owned} possédée(s)" : "")
                + (f.ShinyDone > 0 ? $" · {f.ShinyDone} brillante(s)" : ""),
        };
        button.SetResourceReference(StyleProperty, "PillToggle");
        button.Click += (_, _) => Select(f);
        return button;
    }

    UIElement RightPage()
    {
        var dock = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var shiny = new ToggleButton { Content = "✨ Brillantes", Padding = new Thickness(12, 4, 12, 4), ToolTip = "Afficher les cartes brillantes de la famille" };
        shiny.SetResourceReference(StyleProperty, "PillToggle");
        shiny.Click += (_, _) => { _shiny = shiny.IsChecked == true; ShowCards(); };
        DockPanel.SetDock(shiny, Dock.Right);
        head.Children.Add(shiny);
        head.Children.Add(_familyTitle);
        head.Children.Add(_familyCount);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        var legend = new TextBlock { Text = "Couleur : au classeur · pâle : en banque ou inventaire · pointillés : manquante (prix au survol)", FontSize = 11, Margin = new Thickness(0, 8, 0, 0) };
        legend.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        DockPanel.SetDock(legend, Dock.Bottom);
        dock.Children.Add(legend);
        dock.Children.Add(new ScrollViewer { Content = _cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return dock;
    }

    void Select(BookFamily? f)
    {
        _family = f;
        foreach (var b in _families.Children.OfType<ToggleButton>()) b.IsChecked = ReferenceEquals(b.Tag, f);
        ShowCards();
    }

    void ShowCards()
    {
        _cards.Children.Clear();
        if (_family is not { } f) return;
        _familyTitle.Text = f.Name;
        var list = _shiny ? f.Shiny : f.Cards;
        _familyCount.Text = $"{list.Count(c => c.State == 2)} / {list.Count}";
        _familyCount.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        foreach (var c in list) _cards.Children.Add(Tile(c));
    }

    /// <summary>Carte : image (icône de l'objet carte), numéro, nom et étoiles de rareté ; cadre en pointillés si manquante.</summary>
    static FrameworkElement Tile(BookCard c)
    {
        var grid = new Grid { Width = 128, Height = 176, Margin = new Thickness(0, 0, 10, 10), ToolTip = c.Tip, Background = Brushes.Transparent };
        var stack = new StackPanel { Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        var number = new TextBlock { Text = $"#{c.Number}", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        number.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        stack.Children.Add(number);
        if (c.State > 0)
            stack.Children.Add(new Image { Source = ItemIcons.Get(c.ArtItem), Width = 92, Height = 92, Margin = new Thickness(0, 2, 0, 4) });
        else
            stack.Children.Add(new Border { Height = 70 });
        stack.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center });
        var stars = new TextBlock { Text = new string('★', Math.Max(1, c.Rarity)), HorizontalAlignment = HorizontalAlignment.Center };
        stars.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
        stack.Children.Add(stars);

        if (c.State == 0)
        {
            var dash = new Rectangle { RadiusX = 10, RadiusY = 10, StrokeThickness = 1.5, StrokeDashArray = [4, 3] };
            dash.SetResourceReference(Shape.StrokeProperty, "TextMuted");
            grid.Children.Add(dash);
            stack.Opacity = 0.75;
        }
        else
        {
            var frame = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(c.Shiny ? 3 : 2) };
            frame.SetResourceReference(Border.BackgroundProperty, "Surface");
            if (c.Shiny) frame.BorderBrush = new LinearGradientBrush(Color.FromRgb(0xFF, 0xD8, 0x4D), Color.FromRgb(0xFF, 0x8A, 0x3D), 45);
            else frame.SetResourceReference(Border.BorderBrushProperty, c.State == 2 ? "AccentSolid" : "Border");
            grid.Children.Add(frame);
            if (c.State == 1) stack.Opacity = 0.55;
        }
        grid.Children.Add(stack);
        return grid;
    }
}
