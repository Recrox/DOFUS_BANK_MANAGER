using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using DofusRetroManager.Data;

namespace DofusRetroManager;

/// <summary>Case à montrer sur la carte (position d'un marchand, d'un archi-monstre…) : super-zone, coordonnées et libellé du repère.</summary>
public sealed record CarteTarget(int SuperArea, int X, int Y, string Label);

/// <summary>Ligne dont la position est cliquable : un clic montre sa case sur la carte (null : position inconnue).</summary>
public interface IHasCarteTarget
{
    CarteTarget? Goto { get; }
}

/// <summary>
/// Onglet Carte : données de la carte du monde (zaaps, temples, HDV, ateliers, donjons…) et position de vos personnages,
/// dessinées par la page. La carte et les icônes sont lues dans le client installé, les points d'intérêt dans le fichier
/// de langue « hints » : rien n'est livré avec l'application. Simple affichage (règle 3).
/// </summary>
public partial class MainWindow
{
    /// <summary>Filtre « Zaaps » : les zaaps sont rangés dans la catégorie Divers du jeu, on les sépare comme sur les sites de cartes.</summary>
    const int ZaapFilter = -1;
    const int DungeonCategory = 6;

    readonly HashSet<int> _carteShown = [ZaapFilter, DungeonCategory];
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

    /// <summary>
    /// Case cliquée sur la carte : zone, points d'intérêt et maisons, puis les ressources des maps de cette case déjà
    /// visitées (onglet Carte, récolte) avec leur nombre disponible et leur repousse. Les prix des maisons ne sont pas connus.
    /// </summary>
    void ShowCarteCellInfo(int superArea, int x, int y)
    {
        if (_data == null) return;
        var maps = _data.Maps.Values.Where(m => m.SuperArea == superArea && m.X == x && m.Y == y).ToList();
        var zone = maps.GroupBy(m => m.SubArea).MaxBy(g => g.Count())?.First();
        CarteCellTitle.Text = $"[{x},{y}]" + (zone != null ? " " + zone.Area + (zone.SubArea.Length > 0 && zone.SubArea != zone.Area ? $" ({zone.SubArea})" : "") : "");
        CarteCellTitle.ToolTip = zone == null ? null : zone.Area + (zone.SubArea.Length > 0 && zone.SubArea != zone.Area ? $" ({zone.SubArea})" : "");
        var here = _data.Hints.Where(h => h.Map.SuperArea == superArea && h.Map.X == x && h.Map.Y == y).ToList();
        var houses = here.Count(h => h.Category == GameData.HouseCategory);
        var names = here.Where(h => h.Category != GameData.HouseCategory).Select(h => h.Name).Distinct().ToList();
        if (houses > 0) names.Add(houses == 1 ? "1 maison" : $"{houses} maisons");
        CarteCellInfo.Text = names.Count > 0 ? string.Join(" · ", names) : "";
        CarteCellInfo.ToolTip = here.Count == 0 ? null : string.Join("\n", here.Select(h => h.Name).Distinct())
            + (houses > 0 ? "\nPrix des maisons : pas disponible pour l'instant." : "");
        var lines = _harvest == null ? [] : maps.Where(m => _harvest.Maps.ContainsKey(m.Id)).SelectMany(m => _harvest.Lines(m.Id)).ToList();
        bool visited = _harvest != null && maps.Any(m => _harvest.Maps.ContainsKey(m.Id));
        CarteCellHarvest.Text = !visited ? "Ressources : map pas encore visitée" : lines.Count == 0 ? "Aucune ressource" : "";
        CarteCellHarvest.Visibility = CarteCellHarvest.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CarteCellList.ItemsSource = lines.Select(Harvest.HarvestTracker.Chip).ToList();
        CarteCellPanel.Visibility = Visibility.Visible;
    }

    // --- Positions cliquables (TTG, Combat) : la page ouvre la carte sur leur case ---

    /// <summary>Case d'une map reçue du jeu, si la carte peut l'afficher (continent ou Incarnam).</summary>
    CarteTarget? CarteTargetOf(int? mapId, string label) =>
        mapId is { } id && _data != null && _data.Maps.TryGetValue(id, out var m) && m.SuperArea is 0 or 3
            ? new CarteTarget(m.SuperArea, m.X, m.Y, label) : null;

    CarteTarget? CarteTargetOf(string mapId, string label) => CarteTargetOf(int.TryParse(mapId, out var id) ? id : null, label);

    // --- Interface HTML : la page dessine la carte avec les tuiles et icônes servies par l'application ---

    readonly System.Collections.Concurrent.ConcurrentDictionary<int, List<MapTile>> _carteTiles = new(); // lu aussi par le fil de rendu des images
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]?> _carteWebFiles = new();
    string? _carteWebJson;

    List<MapTile> CarteTiles(int superArea)
    {
        if (!_carteTiles.TryGetValue(superArea, out var tiles))
            _carteTiles[superArea] = tiles = WorldMap.LoadTiles(superArea);
        return tiles;
    }

    bool _carteLoading;

    /// <summary>
    /// Tuiles du continent et d'Incarnam lues en arrière-plan (lecture des fichiers du client, plusieurs secondes) : la
    /// fenêtre reste fluide ; la page affiche « Chargement de la carte… » en attendant.
    /// </summary>
    async void PreloadCarte()
    {
        if (_carteLoading) return;
        _carteLoading = true;
        try
        {
            foreach (var sa in new[] { 0, 3 })
                if (!_carteTiles.ContainsKey(sa)) _carteTiles.TryAdd(sa, await Task.Run(() => WorldMap.LoadTiles(sa)));
        }
        catch (Exception ex) { AddLog("Carte : " + ex.Message); _carteTiles.TryAdd(0, []); _carteTiles.TryAdd(3, []); }
        finally { _carteLoading = false; }
    }

    /// <summary>
    /// Données de la carte pour la page (calculées une fois) : tuiles présentes, catégories, points d'intérêt et zone
    /// de chaque case, pour le continent (0) et Incarnam (3).
    /// </summary>
    string CarteWebJson()
    {
        if (_carteWebJson != null || _data == null) return _carteWebJson ?? "null";
        if (!_carteTiles.ContainsKey(0) || !_carteTiles.ContainsKey(3)) { PreloadCarte(); return "null"; }
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

}
