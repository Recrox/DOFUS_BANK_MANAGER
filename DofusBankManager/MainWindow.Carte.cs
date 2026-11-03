using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using DofusBankManager.Data;

namespace DofusBankManager;

/// <summary>Ligne de la liste des points d'intérêt de l'onglet Carte.</summary>
public sealed record CarteRow(MapHint Hint, string Name, string Position, string Category, ImageSource? Icon);

/// <summary>
/// Onglet Carte : carte du monde et ses points d'intérêt (zaaps, temples, HDV, ateliers, donjons…), avec la position
/// de vos personnages. La carte et les icônes sont lues dans le client installé, les points d'intérêt dans le fichier
/// de langue « hints » : rien n'est livré avec l'application. Simple affichage (règle 3).
/// </summary>
public partial class MainWindow
{
    /// <summary>Filtre « Zaaps » : les zaaps sont rangés dans la catégorie Divers du jeu, on les sépare comme sur les sites de cartes.</summary>
    const int ZaapFilter = -1;
    const int DungeonCategory = 6;
    const double MarkerSize = 22;

    readonly HashSet<int> _carteShown = [ZaapFilter, DungeonCategory];
    /// <summary>Super-zone affichée (0 : continent, 3 : Incarnam), -1 avant le premier chargement.</summary>
    int _carteSuperArea = -1;
    bool _carteLoading;
    /// <summary>Zone de chaque case de la super-zone affichée (la plus fréquente parmi ses maps).</summary>
    Dictionary<(int X, int Y), MapInfo> _carteCells = new();

    static int CarteFilterOf(MapHint h) => h.Name == "Zaap" ? ZaapFilter : h.Category;

    static Point CarteCenter(MapInfo m) =>
        new((m.X + 0.5) * WorldMap.CellWidth, (m.Y + 0.5) * WorldMap.CellHeight);

    void InitCarte(GameData data)
    {
        var filters = new List<(int Key, string Name)> { (ZaapFilter, "Zaaps") };
        filters.AddRange(data.HintCategories.OrderBy(c => c.Key).Select(c => (c.Key, c.Value)));
        foreach (var (key, name) in filters)
        {
            var sample = data.Hints.FirstOrDefault(h => CarteFilterOf(h) == key);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (sample != null && WorldMap.HintIcon(sample.Gfx) is { } icon)
                content.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var toggle = new ToggleButton
            {
                Content = content, Tag = key, Style = (Style)FindResource("PillToggle"), Margin = new Thickness(0, 0, 4, 4),
                IsChecked = _carteShown.Contains(key), ToolTip = $"Afficher les points « {name} » sur la carte",
            };
            toggle.Checked += CarteFilter_Changed;
            toggle.Unchecked += CarteFilter_Changed;
            CarteFilters.Children.Add(toggle);
        }

        CarteMap.CellHovered += ShowCarteCell;
        CarteMap.HoverLeft += () => CarteHover.Text = "Survolez la carte pour voir les coordonnées.";
        CarteMap.MarkerClicked += tag =>
        {
            if (tag is not MapHint h) return;
            var row = (CarteGrid.ItemsSource as IEnumerable<CarteRow>)?.FirstOrDefault(r => r.Hint == h);
            if (row != null) { CarteGrid.SelectedItem = row; CarteGrid.ScrollIntoView(row); }
        };
        _hunt!.Changed += RefreshCartePlayers;
        MainTabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == MainTabs && MainTabs.SelectedItem == CarteTab) _ = LoadCarteAsync();
        };
        if (MainTabs.SelectedItem == CarteTab) _ = LoadCarteAsync();
    }

    /// <summary>Charge la carte de la super-zone choisie (au premier affichage de l'onglet, puis à chaque changement).</summary>
    async Task LoadCarteAsync()
    {
        int superArea = CarteSuperArea.SelectedIndex == 1 ? 3 : 0;
        if (_data == null || _carteLoading || superArea == _carteSuperArea) return;
        _carteLoading = true;
        CarteEmpty.Text = "Chargement de la carte…";
        CarteEmpty.Visibility = Visibility.Visible;
        try
        {
            var tiles = await Task.Run(() => WorldMap.LoadTiles(superArea));
            _carteSuperArea = superArea;
            _carteCells = _data.Maps.Values.Where(m => m.SuperArea == superArea)
                .GroupBy(m => (m.X, m.Y))
                .ToDictionary(g => g.Key, g => g.GroupBy(m => m.SubArea).MaxBy(s => s.Count())!.First());
            CarteMap.Tiles = tiles;
            CarteMap.Highlight = null;
            CarteEmpty.Text = "Carte introuvable : elle est lue dans les fichiers de Dofus Retro, qui doit être installé sur ce PC.";
            CarteEmpty.Visibility = tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyCarteFilters();
            RefreshCartePlayers();
        }
        catch (Exception ex)
        {
            AddLog("Carte : " + ex.Message);
            CarteEmpty.Text = "Impossible de lire la carte : " + ex.Message;
        }
        finally { _carteLoading = false; }

        // Super-zone changée pendant le chargement
        if ((CarteSuperArea.SelectedIndex == 1 ? 3 : 0) != _carteSuperArea) await LoadCarteAsync();
    }

    void CarteSuperArea_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && MainTabs.SelectedItem == CarteTab) _ = LoadCarteAsync();
    }

    void CarteFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: int key } t)
        {
            if (t.IsChecked == true) _carteShown.Add(key); else _carteShown.Remove(key);
        }
        ApplyCarteFilters();
    }

    void CarteSearch_Changed(object sender, TextChangedEventArgs e) => ApplyCarteFilters();

    void CarteGrid_Changed(object sender, RoutedEventArgs e) => CarteMap.ShowGrid = CarteShowGrid.IsChecked == true;

    void ApplyCarteFilters()
    {
        if (_data == null || _carteSuperArea < 0) return;
        var search = CarteSearch.Text.Trim();
        // Une recherche porte sur toutes les catégories ; sans recherche, seules les catégories cochées s'affichent
        var hints = _data.Hints.Where(h => h.Map.SuperArea == _carteSuperArea)
            .Where(h => search.Length > 0
                ? Contains(h.Name, search) || Contains(h.Map.Area, search) || Contains(h.Map.SubArea, search)
                  || $"[{h.Map.X},{h.Map.Y}]".Contains(search.Replace(" ", ""))
                : _carteShown.Contains(CarteFilterOf(h)))
            .ToList();

        string CategoryName(MapHint h) =>
            CarteFilterOf(h) == ZaapFilter ? "Zaaps" : _data.HintCategories.GetValueOrDefault(h.Category, "Divers");

        CarteMap.SetMarkers("hints", hints.Select(h =>
        {
            FrameworkElement el = WorldMap.HintIcon(h.Gfx) is { } icon
                ? new Image { Source = icon, Stretch = Stretch.Uniform }
                : new Ellipse { Fill = (Brush)FindResource("AccentSolid"), Stroke = Brushes.White, StrokeThickness = 1.5 };
            el.Width = el.Height = el is Image ? MarkerSize : 10;
            el.Tag = h;
            el.Cursor = System.Windows.Input.Cursors.Hand;
            el.ToolTip = $"{h.Name}\n{h.Map.Label}";
            return (el, CarteCenter(h.Map));
        }));

        var selected = (CarteGrid.SelectedItem as CarteRow)?.Hint;
        var rows = hints.OrderBy(CarteFilterOf).ThenBy(h => h.Name)
            .Select(h => new CarteRow(h, h.Name, h.Map.Label, CategoryName(h), WorldMap.HintIcon(h.Gfx))).ToList();
        CarteGrid.ItemsSource = rows;
        if (rows.FirstOrDefault(r => r.Hint == selected) is { } keep) CarteGrid.SelectedItem = keep;

        CarteCount.Text = hints.Count == 0
            ? search.Length > 0 ? "Aucun point d'intérêt ne correspond à la recherche." : "Cochez une catégorie pour afficher ses points d'intérêt."
            : string.Join(" · ", hints.GroupBy(CategoryName).Select(g => $"{g.Key} ({g.Count()})"));
    }

    void CarteRow_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (CarteGrid.SelectedItem is not CarteRow row) return;
        CarteMap.Highlight = (row.Hint.Map.X, row.Hint.Map.Y);
        CarteMap.CenterOn(CarteCenter(row.Hint.Map));
        ShowCarteCell(row.Hint.Map.X, row.Hint.Map.Y);
    }

    void ShowCarteCell(int x, int y)
    {
        var text = $"[{x},{y}]";
        if (_carteCells.TryGetValue((x, y), out var m))
            text += $"  {m.Area}" + (m.SubArea.Length > 0 && m.SubArea != m.Area ? $" ({m.SubArea})" : "");
        var here = _data?.Hints.Where(h => h.Map.SuperArea == _carteSuperArea && h.Map.X == x && h.Map.Y == y)
            .Select(h => h.Name).Distinct().ToList();
        if (here is { Count: > 0 }) text += "  —  " + string.Join(", ", here.Take(4)) + (here.Count > 4 ? "…" : "");
        CarteHover.Text = text;
    }

    /// <summary>Position de vos personnages : la map actuelle de chaque fenêtre de jeu (onglet Combat).</summary>
    void RefreshCartePlayers()
    {
        if (_data == null || _hunt == null || _carteSuperArea < 0) return;
        var shown = _hunt.Shown;
        var markers = new List<(FrameworkElement, Point)>();
        foreach (var c in _hunt.Characters)
        {
            if (!int.TryParse(c.MapId, out var id) || !_data.Maps.TryGetValue(id, out var m) || m.SuperArea != _carteSuperArea) continue;
            bool main = c == shown;
            var dot = new Ellipse
            {
                Width = 16, Height = 16, StrokeThickness = 2.5, Stroke = Brushes.White,
                Fill = main ? (Brush)FindResource("AccentSolid") : new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0xD1)),
            };
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock { Text = c.Label, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold },
            };
            Canvas.SetLeft(label, 20);
            Canvas.SetTop(label, -1);
            var marker = new Canvas { Width = 16, Height = 16, Children = { dot, label }, ToolTip = $"{c.Label}\n{m.Label}", IsHitTestVisible = true };
            Panel.SetZIndex(marker, 10);
            markers.Add((marker, CarteCenter(m)));
        }
        CarteMap.SetMarkers("players", markers);
    }

    void CarteMyPosition_Click(object sender, RoutedEventArgs e)
    {
        if (_data != null && _hunt?.Shown is { } c && int.TryParse(c.MapId, out var id) && _data.Maps.TryGetValue(id, out var m))
        {
            if (m.SuperArea != _carteSuperArea)
            {
                CarteSuperArea.SelectedIndex = m.SuperArea == 3 ? 1 : 0; // la carte se recharge, puis on recentre
                return;
            }
            CarteMap.Highlight = (m.X, m.Y);
            CarteMap.CenterOn(CarteCenter(m), 1.5);
            ShowCarteCell(m.X, m.Y);
        }
        else CarteHover.Text = "Position inconnue : changez de map en jeu pour que l'application la voie.";
    }

    void CarteFit_Click(object sender, RoutedEventArgs e) => CarteMap.Fit();
    void CarteZoomIn_Click(object sender, RoutedEventArgs e) => CarteMap.Zoom(true);
    void CarteZoomOut_Click(object sender, RoutedEventArgs e) => CarteMap.Zoom(false);
}
