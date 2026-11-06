using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DofusRetroManager;

// Contrôles sans affichage : ils remplacent ceux de l'ancienne interface WPF. Ils gardent leurs noms, leurs propriétés et
// leurs événements (mêmes règles que WPF : un changement de valeur, même par le code, prévient les gestionnaires), si bien
// que la logique des onglets n'a pas changé. La page HTML lit leur état et leur renvoie les actions (MainWindow.WebUi.cs).

/// <summary>Base : visibilité, activation, infobulle et étiquette, comme un élément WPF.</summary>
public class WebControl
{
    /// <summary>
    /// Compteur de changements, tous contrôles confondus : la page n'est relue et mise à jour que s'il a bougé depuis le
    /// dernier envoi (au repos, rien n'est recalculé).
    /// </summary>
    public static long Version { get; private set; }
    protected static void Touch() => Version++;
    internal static void TouchAll() => Version++;

    public Visibility Visibility { get; set { if (field != value) { field = value; Touch(); } } } = Visibility.Visible;
    public bool IsEnabled { get; set { if (field != value) { field = value; Touch(); } } } = true;
    public object? ToolTip { get; set; }
    public object? Tag { get; set { field = value; Touch(); } }
    public double Width { get; set; } = double.NaN;
}

/// <summary>Texte affiché (ancien TextBlock) ; sa couleur peut suivre une couleur du thème.</summary>
public sealed class WebText : WebControl
{
    Brush? _brush;
    public string Text { get; set { if (field != value) { field = value ?? ""; Touch(); } } } = "";
    /// <summary>Couleur du thème (clé de ressource) : relue à chaque envoi, elle suit le changement de thème.</summary>
    public string? ForegroundKey { get; set { field = value; Touch(); } }
    public Brush? Foreground
    {
        get => ForegroundKey != null ? Application.Current?.TryFindResource(ForegroundKey) as Brush : _brush;
        set { _brush = value; ForegroundKey = null; }
    }
    public void SetResourceReference(DependencyProperty _, object key) => ForegroundKey = key as string;
}

public class WebButton : WebControl
{
    public object? Content { get; set { field = value; Touch(); } }
    public event RoutedEventHandler? Click;
    /// <summary>Clic venu de la page.</summary>
    public void RaiseClick() => Click?.Invoke(this, new RoutedEventArgs(ButtonBase.ClickEvent, this));
}

/// <summary>Case à cocher ou bouton bascule.</summary>
public sealed class WebToggle : WebButton
{
    bool? _checked = false;
    public event RoutedEventHandler? Checked, Unchecked;
    public bool? IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Touch();
            if (value == true) Checked?.Invoke(this, new RoutedEventArgs(ToggleButton.CheckedEvent, this));
            else if (value == false) Unchecked?.Invoke(this, new RoutedEventArgs(ToggleButton.UncheckedEvent, this));
        }
    }
}

public sealed class WebTextBox : WebControl
{
    string _text = "";
    public event TextChangedEventHandler? TextChanged;
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value) return;
            _text = value;
            Touch();
            TextChanged?.Invoke(this, new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
        }
    }
    public void AppendText(string s) => Text += s;
    public void ScrollToEnd() { }
}

/// <summary>Liste déroulante : éléments (ou source), choix par index ou par élément.</summary>
public sealed class WebCombo : WebControl
{
    public WebCombo() => Items = new WebItems(this);

    IEnumerable? _source;
    int _index = -1;
    public event SelectionChangedEventHandler? SelectionChanged;
    public WebItems Items { get; }

    public IEnumerable? ItemsSource
    {
        get => _source;
        set
        {
            var old = SelectedItem;
            _source = value;
            Touch();
            Items.Reset(value?.Cast<object?>() ?? []);
            // Comme WPF : le choix est gardé s'il est encore dans la liste
            _index = -1;
            Select(old == null ? -1 : Items.IndexOf(old), old);
        }
    }

    public int SelectedIndex
    {
        get => _index;
        set => Select(value >= 0 && value < Items.Count ? value : -1, SelectedItem);
    }

    public object? SelectedItem
    {
        get => _index >= 0 && _index < Items.Count ? Items[_index] : null;
        set => Select(value == null ? -1 : Items.IndexOf(value), SelectedItem);
    }

    internal void Select(int index, object? old)
    {
        if (_index != index) Touch();
        _index = index;
        var now = SelectedItem;
        if (Equals(old, now)) return;
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent,
            old == null ? Array.Empty<object>() : new[] { old }, now == null ? Array.Empty<object>() : new[] { now }));
    }
}

/// <summary>Éléments d'une liste déroulante : un retrait ou un vidage remet le choix à jour.</summary>
public sealed class WebItems(WebCombo owner) : Collection<object?>
{
    bool _resetting;

    protected override void InsertItem(int index, object? item)
    {
        base.InsertItem(index, item);
        WebControl.TouchAll();
    }

    internal void Reset(IEnumerable<object?> items)
    {
        _resetting = true;
        try { Clear(); foreach (var i in items) Add(i); }
        finally { _resetting = false; }
    }

    protected override void ClearItems()
    {
        WebControl.TouchAll();
        var old = owner.SelectedItem;
        base.ClearItems();
        if (!_resetting) owner.Select(-1, old);
    }

    protected override void RemoveItem(int index)
    {
        WebControl.TouchAll();
        var old = owner.SelectedItem;
        int sel = owner.SelectedIndex;
        base.RemoveItem(index);
        if (!_resetting) owner.Select(sel == index ? -1 : sel > index ? sel - 1 : sel, old);
    }
}

/// <summary>Tableau ou liste (ancien DataGrid / ItemsControl) : lignes et ligne choisie.</summary>
public sealed class WebList : WebControl
{
    IEnumerable? _source;
    object? _selected;
    public event SelectionChangedEventHandler? SelectionChanged;

    public IEnumerable? ItemsSource
    {
        get => _source;
        set
        {
            _source = value;
            Touch();
            // Comme WPF : la ligne choisie est gardée si elle est encore dans la liste (lignes égales), sinon oubliée
            if (_selected == null) return;
            var same = value?.Cast<object?>().FirstOrDefault(r => Equals(r, _selected));
            if (same != null) _selected = same;
            else SelectedItem = null;
        }
    }

    public object? SelectedItem
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            var old = _selected;
            _selected = value;
            Touch();
            if (Equals(old, value)) return;
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent,
                old == null ? Array.Empty<object>() : new[] { old }, value == null ? Array.Empty<object>() : new[] { value }));
        }
    }

    public void ScrollIntoView(object? _) { }
}

/// <summary>Groupe de boutons créés par le code (filtres) : la page les affiche et les coche.</summary>
public sealed class WebPanel : WebControl
{
    /// <summary>Boutons créés par le code ; leurs cases se cochent sans prévenir : la page les relit au plus toutes les 5 s.</summary>
    public List<UIElement> Children { get; } = new();
}

public sealed class WebImage : WebControl
{
    public ImageSource? Source { get; set; }
}

/// <summary>Courbe de prix : points (date, prix) dessinés par la page.</summary>
public sealed class WebChart : WebControl
{
    public IReadOnlyList<(DateTime Time, double Price)> Points { get; set { field = value; Touch(); } } = [];
}

public sealed class WebSlider : WebControl
{
    double _value;
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public event RoutedPropertyChangedEventHandler<double>? ValueChanged;
    public double Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, Minimum, Maximum);
            if (_value == value) return;
            var old = _value;
            _value = value;
            Touch();
            ValueChanged?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(old, value, RangeBase.ValueChangedEvent));
        }
    }
}

/// <summary>Pastille (couleur) ou icône.</summary>
public sealed class WebShape : WebControl
{
    public Brush? Fill { get; set { field = value; Touch(); } }
    public Geometry? Data { get; set; }
}

public sealed class WebPopup : WebControl
{
    public bool IsOpen { get; set; }
}

/// <summary>Colonne d'un tableau dont le titre ou la visibilité change (envoyée à la page).</summary>
public sealed class WebColumn : WebControl
{
    public object? Header { get; set { field = value; Touch(); } }
}

/// <summary>Onglets : la page choisit l'onglet affiché par son index.</summary>
public sealed class WebTabs : WebControl
{
    readonly List<WebTab> _tabs = new();
    int _index = -1;
    public event SelectionChangedEventHandler? SelectionChanged;
    public IReadOnlyList<WebTab> Items => _tabs;

    public void Add(WebTab tab)
    {
        tab.Owner = this;
        _tabs.Add(tab);
    }

    public int SelectedIndex
    {
        get => _index;
        set
        {
            if (value < 0 || value >= _tabs.Count || value == _index) return;
            var old = SelectedItem;
            _index = value;
            Touch();
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent,
                old == null ? Array.Empty<object>() : new object[] { old }, new object[] { _tabs[value] }));
        }
    }

    public WebTab? SelectedItem
    {
        get => _index >= 0 ? _tabs[_index] : null;
        set { if (value != null) SelectedIndex = _tabs.IndexOf(value); }
    }
}

public sealed class WebTab : WebControl
{
    internal WebTabs? Owner { get; set; }
    public object? Header { get; set; }
    public bool IsSelected
    {
        get => Owner?.SelectedItem == this;
        set { if (value && Owner != null) Owner.SelectedItem = this; }
    }
}
