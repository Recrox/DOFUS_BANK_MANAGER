using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using DofusRetroManager.Data;

namespace DofusRetroManager;

/// <summary>Ligne de la liste des points d'intérêt de l'onglet Carte.</summary>
public sealed record CarteRow(MapHint Hint, string Name, string Position, string Category, ImageSource? Icon);

/// <summary>Case à montrer sur la carte (position d'un marchand, d'un archi-monstre…) : super-zone, coordonnées et libellé du repère.</summary>
public sealed record CarteTarget(int SuperArea, int X, int Y, string Label);

/// <summary>Ligne dont la position est cliquable : un clic montre sa case sur la carte (null : position inconnue).</summary>
public interface IHasCarteTarget
{
    CarteTarget? Goto { get; }
}

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

    /// <summary>Icône d'une maison, dessinée par l'application (le jeu n'en a pas dans les icônes de la carte).</summary>
    static readonly ImageSource HouseIcon = CreateHouseIcon();

    static ImageSource CreateHouseIcon()
    {
        var geometry = Geometry.Parse("M2,11 L12,3 L22,11 Z M5,11 H19 V21 H5 Z");
        var door = Geometry.Parse("M10,21 V15 H14 V21 Z");
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xD9, 0x8E, 0x3B)),
            new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x2A, 0x10)), 1.6) { LineJoin = PenLineJoin.Round }, geometry));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x4A, 0x2A, 0x10)), null, door));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    static ImageSource? CarteIcon(MapHint h) => h.Category == GameData.HouseCategory ? HouseIcon : WorldMap.HintIcon(h.Gfx);

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
            if (sample != null && CarteIcon(sample) is { } icon)
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
            CarteMap.SetMarkers("goto", []);
            ApplyCarteGotoLater();
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

    void CarteGrid_Changed(object sender, RoutedEventArgs e)
    {
        // Appelé dès le chargement de la fenêtre (case cochée dans le XAML), avant la création de la carte
        if (CarteMap != null) CarteMap.ShowGrid = CarteShowGrid.IsChecked == true;
    }

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
            FrameworkElement el = CarteIcon(h) is { } icon
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
            .Select(h => new CarteRow(h, h.Name, h.Map.Label, CategoryName(h), CarteIcon(h))).ToList();
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
        if (_hunt?.Shown is { } c && CarteTargetOf(c.MapId, c.Label) is { } target) ShowOnCarte(target, marker: false);
        else CarteHover.Text = "Position inconnue : changez de map en jeu pour que l'application la voie.";
    }

    // --- Positions cliquables (TTG, Combat) : aller à la case sur la carte ---

    /// <summary>Case à montrer, en attente du chargement de la carte.</summary>
    (CarteTarget Target, bool Marker)? _carteGoto;

    /// <summary>Case d'une map reçue du jeu, si la carte peut l'afficher (continent ou Incarnam).</summary>
    CarteTarget? CarteTargetOf(int? mapId, string label) =>
        mapId is { } id && _data != null && _data.Maps.TryGetValue(id, out var m) && m.SuperArea is 0 or 3
            ? new CarteTarget(m.SuperArea, m.X, m.Y, label) : null;

    CarteTarget? CarteTargetOf(string mapId, string label) => CarteTargetOf(int.TryParse(mapId, out var id) ? id : null, label);

    /// <summary>Clic sur une position (TTG, Combat) : ouvre la carte sur sa case.</summary>
    void PositionLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is IHasCarteTarget { Goto: { } target }) ShowOnCarte(target);
    }

    void HuntMap_Click(object sender, RoutedEventArgs e)
    {
        if (_hunt?.Shown is { } c && CarteTargetOf(c.MapId, c.Label) is { } target) ShowOnCarte(target, marker: false);
    }

    /// <summary>
    /// Affiche l'onglet Carte centré sur la case, encadrée, avec un repère portant son libellé (pas pour vos personnages,
    /// déjà marqués). Charge au besoin la bonne super-zone : la case est montrée une fois la carte prête.
    /// </summary>
    void ShowOnCarte(CarteTarget target, bool marker = true)
    {
        _carteGoto = (target, marker);
        MainTabs.SelectedItem = CarteTab;
        int index = target.SuperArea == 3 ? 1 : 0;
        if (CarteSuperArea.SelectedIndex != index) CarteSuperArea.SelectedIndex = index;
        else if (_carteSuperArea == target.SuperArea && !_carteLoading) ApplyCarteGotoLater();
    }

    /// <summary>Après la mise en page de l'onglet (la vue doit connaître sa taille pour centrer).</summary>
    void ApplyCarteGotoLater() => _ = Dispatcher.BeginInvoke(ApplyCarteGoto, System.Windows.Threading.DispatcherPriority.Loaded);

    void ApplyCarteGoto()
    {
        if (_carteGoto is not { } go || go.Target.SuperArea != _carteSuperArea) return;
        var (t, marker) = go;
        _carteGoto = null;
        var at = new Point((t.X + 0.5) * WorldMap.CellWidth, (t.Y + 0.5) * WorldMap.CellHeight);
        CarteMap.Highlight = (t.X, t.Y);
        CarteMap.CenterOn(at, 1.5);
        ShowCarteCell(t.X, t.Y);
        if (!marker) { CarteMap.SetMarkers("goto", []); return; }
        var pin = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M9,0 C4,0 0,4 0,9 C0,15 9,24 9,24 C9,24 18,15 18,9 C18,4 14,0 9,0 Z M9,5 A4,4 0 1 0 9,13 A4,4 0 1 0 9,5 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), Stroke = Brushes.White, StrokeThickness = 1.5,
        };
        Canvas.SetLeft(pin, -1);
        Canvas.SetTop(pin, -16);
        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
            Child = new TextBlock { Text = t.Label, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold },
        };
        Canvas.SetLeft(label, 20);
        Canvas.SetTop(label, -10);
        var element = new Canvas { Width = 16, Height = 16, Children = { pin, label }, ToolTip = t.Label };
        Panel.SetZIndex(element, 20);
        CarteMap.SetMarkers("goto", [(element, at)]);
    }

    // --- Interface HTML : la page dessine la carte avec les tuiles et icônes servies par l'application ---

    readonly Dictionary<int, List<MapTile>> _carteTiles = new();
    readonly Dictionary<string, byte[]?> _carteWebFiles = new();
    string? _carteWebJson;

    List<MapTile> CarteTiles(int superArea)
    {
        if (!_carteTiles.TryGetValue(superArea, out var tiles))
            _carteTiles[superArea] = tiles = WorldMap.LoadTiles(superArea);
        return tiles;
    }

    /// <summary>
    /// Données de la carte pour la page (calculées une fois) : tuiles présentes, catégories, points d'intérêt et zone
    /// de chaque case, pour le continent (0) et Incarnam (3).
    /// </summary>
    string CarteWebJson()
    {
        if (_carteWebJson != null || _data == null) return _carteWebJson ?? "null";
        int[] areas = [0, 3];
        var cells = areas.ToDictionary(sa => sa.ToString(), sa => _data.Maps.Values.Where(m => m.SuperArea == sa)
            .GroupBy(m => $"{m.X},{m.Y}")
            .ToDictionary(g => g.Key, g =>
            {
                var m = g.GroupBy(x => x.SubArea).MaxBy(s => s.Count())!.First();
                return m.Area + (m.SubArea.Length > 0 && m.SubArea != m.Area ? $" ({m.SubArea})" : "");
            }));
        var cats = new List<object> { new { k = ZaapFilter, n = "Zaaps", i = Icon(_data.Hints.FirstOrDefault(h => CarteFilterOf(h) == ZaapFilter)) } };
        cats.AddRange(_data.HintCategories.OrderBy(c => c.Key)
            .Select(c => new { k = c.Key, n = c.Value, i = Icon(_data.Hints.FirstOrDefault(h => CarteFilterOf(h) == c.Key)) }));
        _carteWebJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            tiles = areas.ToDictionary(sa => sa.ToString(), sa => CarteTiles(sa)
                .Select(t => new[] { (int)(t.Bounds.X / t.Bounds.Width), (int)(t.Bounds.Y / t.Bounds.Height) })),
            cats,
            shown = _carteShown,
            hints = _data.Hints.Where(h => areas.Contains(h.Map.SuperArea)).Select(h => new
            {
                n = h.Name, k = CarteFilterOf(h), i = Icon(h), x = h.Map.X, y = h.Map.Y, sa = h.Map.SuperArea, l = h.Map.Label,
            }),
            cells,
        });
        return _carteWebJson;

        static string? Icon(MapHint? h) => h == null ? null : h.Category == GameData.HouseCategory ? "carte/house.png" : $"carte/hint/{h.Gfx}.png";
    }

    /// <summary>Position de vos personnages pour la page : nom, super-zone, coordonnées, personnage affiché.</summary>
    string CartePlayersJson()
    {
        if (_data == null || _hunt == null) return "[]";
        var shown = _hunt.Shown;
        return System.Text.Json.JsonSerializer.Serialize(_hunt.Characters
            .Select(c => (c, ok: int.TryParse(c.MapId, out var id) && _data.Maps.ContainsKey(id), id))
            .Where(x => x.ok)
            .Select(x => (x.c, m: _data.Maps[x.id]))
            .Select(x => new { n = x.c.Label, sa = x.m.SuperArea, x = x.m.X, y = x.m.Y, l = x.m.Label, me = x.c == shown }));
    }

    /// <summary>Fichiers de la carte : carte/tile/{super-zone}/{x}_{y}.png, carte/hint/{gfx}.png, carte/house.png.</summary>
    byte[]? CarteWebFile(string path)
    {
        if (_carteWebFiles.TryGetValue(path, out var png)) return png;
        var tile = System.Text.RegularExpressions.Regex.Match(path, @"^carte/tile/(\d+)/(-?\d+)_(-?\d+)\.png$");
        var hint = System.Text.RegularExpressions.Regex.Match(path, @"^carte/hint/(\d+)\.png$");
        if (tile.Success)
        {
            int sa = int.Parse(tile.Groups[1].Value), x = int.Parse(tile.Groups[2].Value), y = int.Parse(tile.Groups[3].Value);
            var t = CarteTiles(sa).FirstOrDefault(t => (int)(t.Bounds.X / t.Bounds.Width) == x && (int)(t.Bounds.Y / t.Bounds.Height) == y);
            if (t != null)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen()) dc.DrawDrawing(t.Drawing);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)t.Bounds.Width, (int)t.Bounds.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                png = ms.ToArray();
            }
        }
        else if (hint.Success) png = Png(WorldMap.HintIcon(int.Parse(hint.Groups[1].Value)), 48);
        else if (path == "carte/house.png") png = Png(HouseIcon, 48);
        return _carteWebFiles[path] = png;
    }

    void CarteFit_Click(object sender, RoutedEventArgs e) => CarteMap.Fit();
    void CarteZoomIn_Click(object sender, RoutedEventArgs e) => CarteMap.Zoom(true);
    void CarteZoomOut_Click(object sender, RoutedEventArgs e) => CarteMap.Zoom(false);
}
