using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;

namespace DofusBankManager.Data;

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
/// Icônes des sorts, rendues depuis les fichiers du client installé (clips/spells/icons/up/&lt;sort&gt;.swf) :
/// un glyphe blanc, que le jeu pose sur un fond coloré. Lecture seule : le jeu n'est jamais modifié.
/// </summary>
public static class SpellIcons
{
    static readonly string IconsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Retro", "resources", "app", "retroclient", "clips", "spells", "icons", "up");

    static readonly Dictionary<int, ImageSource?> Cache = new();

    public static ImageSource? Get(int spellId)
    {
        if (Cache.TryGetValue(spellId, out var img)) return img;
        img = null;
        var path = Path.Combine(IconsDir, spellId + ".swf");
        try
        {
            if (File.Exists(path) && SwfRenderer.Render(File.ReadAllBytes(path)) is { } drawing)
            {
                var di = new DrawingImage(drawing);
                di.Freeze();
                img = di;
            }
        }
        catch { } // icône illisible : le nom du sort reste affiché
        Cache[spellId] = img;
        return img;
    }
}

/// <summary>Convertit un identifiant d'objet en icône (chargée à l'affichage de la ligne).</summary>
public sealed class ItemIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int id ? ItemIcons.Get(id) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
