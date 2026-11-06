using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;

namespace DofusRetroManager.Data;

/// <summary>
/// Icônes des objets, rendues depuis les fichiers du client installé
/// (clips/items/&lt;type&gt;/&lt;gfx&gt;.swf). Lecture seule : le jeu n'est jamais modifié.
/// </summary>
public static class ItemIcons
{
    static readonly string ItemsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Retro", "resources", "app", "retroclient", "clips", "items");

    static readonly Dictionary<int, ImageSource?> Cache = new();

    public static GameData? Data { get; set; }

    public static ImageSource? Get(int itemId)
    {
        if (Cache.TryGetValue(itemId, out var img)) return img;
        img = null;
        if (Data?.Items.TryGetValue(itemId, out var it) == true)
        {
            var path = Path.Combine(ItemsDir, it.TypeId.ToString(), it.Gfx + ".swf");
            try
            {
                if (File.Exists(path) && SwfRenderer.Render(File.ReadAllBytes(path)) is { } drawing)
                {
                    var di = new DrawingImage(drawing);
                    di.Freeze();
                    img = di;
                }
            }
            catch { } // icône illisible : on affiche simplement rien
        }
        Cache[itemId] = img;
        return img;
    }
}

/// <summary>
/// Icônes des sorts, comme dans le jeu, rendues depuis les fichiers du client installé : le fond
/// (clips/spells/icons/back/&lt;b&gt;.swf : « _spellBackground » et « _spellFrame ») puis le symbole
/// (icons/up/&lt;up&gt;.swf : « _spellPrint »), colorés selon le fichier de langue des sorts.
/// Lecture seule : le jeu n'est jamais modifié.
/// </summary>
public static class SpellIcons
{
    static readonly string IconsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Retro", "resources", "app", "retroclient", "clips", "spells", "icons");

    static readonly Dictionary<int, ImageSource?> Cache = new();

    static Color Rgb(int c) => Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);

    static Drawing? Load(string dir, int id, Dictionary<string, Color> tints)
    {
        var path = Path.Combine(IconsDir, dir, id + ".swf");
        return File.Exists(path) ? SwfRenderer.Render(File.ReadAllBytes(path), tints) : null;
    }

    public static ImageSource? Get(int spellId, SpellLook? look)
    {
        if (Cache.TryGetValue(spellId, out var img)) return img;
        img = null;
        try
        {
            // Sans données de langue : symbole blanc sur fond gris, comme un sort sans élément
            look ??= new SpellLook(1, spellId, 0x6E6E6E, 0xFFFFFF, 0xFFFFFF);
            var group = new DrawingGroup();
            if (Load("back", look.Back, new() { ["_spellBackground"] = Rgb(look.Background), ["_spellFrame"] = Rgb(look.Frame) }) is { } back)
                group.Children.Add(back);
            if (Load("up", look.Up, new() { ["_spellPrint"] = Rgb(look.Print) }) is { } print)
                group.Children.Add(print);
            if (group.Children.Count > 0)
            {
                var di = new DrawingImage(group);
                di.Freeze();
                img = di;
            }
        }
        catch { } // icône illisible : le nom du sort reste affiché
        Cache[spellId] = img;
        return img;
    }
}
