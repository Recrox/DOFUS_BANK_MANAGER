using System.IO;
using System.Windows;
using System.Windows.Media;

namespace DofusRetroManager.Data;

/// <summary>Morceau de la carte du monde : dessin d'une tuile et sa place, en unités de carte.</summary>
public sealed record MapTile(Drawing Drawing, Rect Bounds);

/// <summary>
/// Carte du monde, rendue depuis les fichiers du client installé, comme dans le jeu :
/// clips/maps/&lt;super-zone&gt;.swf exporte des tuiles « x_y » de 600 × 345, une map y occupe 40 × 23,
/// et clips/maps/hints.swf exporte les icônes des points d'intérêt par numéro de gfx.
/// Lecture seule : le jeu n'est jamais modifié, et rien n'est copié ni livré avec l'application.
/// </summary>
public static class WorldMap
{
    public const double CellWidth = 40, CellHeight = 23;
    const double TileWidth = 600, TileHeight = 345;

    static readonly string MapsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Retro", "resources", "app", "retroclient", "clips", "maps");

    static SwfRenderer? _hints;
    static readonly Dictionary<int, ImageSource?> HintCache = new();

    /// <summary>Rectangle d'une map sur la carte.</summary>
    public static Rect Cell(int x, int y) => new(x * CellWidth, y * CellHeight, CellWidth, CellHeight);

    /// <summary>Tuiles d'une super-zone (0 : continent, 3 : Incarnam) ; vide si le client n'est pas installé. Lent : à appeler hors de l'interface.</summary>
    public static List<MapTile> LoadTiles(int superArea)
    {
        var tiles = new List<MapTile>();
        var path = Path.Combine(MapsDir, superArea + ".swf");
        if (!File.Exists(path)) return tiles;
        var swf = SwfRenderer.Load(File.ReadAllBytes(path));
        foreach (var name in swf.Exports.Keys)
        {
            var p = name.Split('_');
            if (p.Length != 2 || !int.TryParse(p[0], out var tx) || !int.TryParse(p[1], out var ty)) continue;
            try
            {
                if (swf.RenderSymbol(name) is { } d)
                    tiles.Add(new MapTile(d, new Rect(tx * TileWidth, ty * TileHeight, TileWidth, TileHeight)));
            }
            catch { } // tuile illisible : elle reste vide
        }
        return tiles;
    }

    /// <summary>Icône d'un point d'intérêt (gfx du fichier de langue « hints ») ; null si absente.</summary>
    public static ImageSource? HintIcon(int gfx)
    {
        if (HintCache.TryGetValue(gfx, out var img)) return img;
        img = null;
        try
        {
            var path = Path.Combine(MapsDir, "hints.swf");
            if (_hints == null && File.Exists(path)) _hints = SwfRenderer.Load(File.ReadAllBytes(path));
            if (_hints?.RenderSymbol(gfx.ToString()) is { } d)
            {
                var di = new DrawingImage(d);
                di.Freeze();
                img = di;
            }
        }
        catch { } // icône illisible : un simple point la remplace
        return HintCache[gfx] = img;
    }
}
