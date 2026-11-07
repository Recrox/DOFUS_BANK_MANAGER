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
/// Icônes des challenges de combat, comme dans le jeu : clips/challenges/&lt;g&gt;.swf du client installé (g : champ du
/// fichier de langue « fightChallenge »). Lecture seule : le jeu n'est jamais modifié.
/// </summary>
public static class ChallengeIcons
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Retro", "resources", "app", "retroclient", "clips", "challenges");

    static readonly Dictionary<int, ImageSource?> Cache = new();

    public static ImageSource? Get(int gfx)
    {
        if (Cache.TryGetValue(gfx, out var img)) return img;
        img = null;
        var path = Path.Combine(Dir, gfx + ".swf");
        try
        {
            if (File.Exists(path) && SwfRenderer.Render(File.ReadAllBytes(path)) is { } drawing)
            {
                var di = new DrawingImage(drawing);
                di.Freeze();
                img = di;
            }
        }
        catch { } // icône illisible : rien
        return Cache[gfx] = img;
    }
}

/// <summary>
/// Icônes des caractéristiques de la fiche du personnage, comme dans le jeu : symboles « Icon&lt;nom&gt; » de
/// modules/core.swf du client installé (Wisdom : sagesse, PP : prospection, Init : initiative, Vita : vitalité).
/// Lecture seule : le jeu n'est jamais modifié.
/// </summary>
public static class StatIcons
{
    static readonly string CorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Retro", "resources", "app", "retroclient", "modules", "core.swf");

    static readonly Dictionary<string, ImageSource?> Cache = new();
    static SwfRenderer? _core;

    public static ImageSource? Get(string name)
    {
        if (Cache.TryGetValue(name, out var img)) return img;
        img = null;
        try
        {
            _core ??= File.Exists(CorePath) ? SwfRenderer.Load(File.ReadAllBytes(CorePath)) : null;
            if (_core?.RenderSymbol("Icon" + name) is { } drawing)
            {
                var di = new DrawingImage(drawing);
                di.Freeze();
                img = di;
            }
        }
        catch { } // icône illisible : l'abréviation reste affichée
        return Cache[name] = img;
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
