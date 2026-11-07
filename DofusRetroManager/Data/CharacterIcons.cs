using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace DofusRetroManager.Data;

/// <summary>Classe (1 Féca … 12 Pandawa) et sexe (0 homme, 1 femme) d'un personnage du joueur, lus au choix du personnage (ASK).</summary>
public sealed record CharacterLook(int Breed, int Sex)
{
    static readonly string[] Breeds =
        ["Féca", "Osamodas", "Enutrof", "Sram", "Xélor", "Ecaflip", "Eniripsa", "Iop", "Crâ", "Sadida", "Sacrieur", "Pandawa"];

    [JsonIgnore] public bool IsKnown => Breed is >= 1 and <= 12 && Sex is 0 or 1;

    /// <summary>Apparence de base du personnage : classe × 10 + sexe (ex. 81 : Iop femme).</summary>
    [JsonIgnore] public int Gfx => Breed * 10 + Sex;

    /// <summary>« Iop · femme ».</summary>
    [JsonIgnore] public string Text => IsKnown ? $"{Breeds[Breed - 1]} · {(Sex == 1 ? "femme" : "homme")}" : "";
}

/// <summary>
/// Têtes des personnages, comme dans le jeu (clips/artworks/mini/&lt;classe×10+sexe&gt;.swf), rendues depuis les fichiers
/// du client installé, aux couleurs par défaut de la classe. Lecture seule : le jeu n'est jamais modifié.
/// </summary>
public static class CharacterIcons
{
    static readonly string MiniDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Retro", "resources", "app", "retroclient", "clips", "artworks", "mini");

    static readonly Dictionary<int, ImageSource?> Cache = new();

    public static ImageSource? Get(CharacterLook? look) => look is { IsKnown: true } ? Get(look.Gfx) : null;

    /// <summary>Appelé par la page (fil de rendu des images) et par l'interface : le cache est protégé par un verrou.</summary>
    public static ImageSource? Get(int gfx)
    {
        lock (Cache) return Build(gfx);
    }

    static ImageSource? Build(int gfx)
    {
        if (Cache.TryGetValue(gfx, out var img)) return img;
        img = null;
        var path = Path.Combine(MiniDir, gfx + ".swf");
        try
        {
            if (File.Exists(path) && SwfRenderer.Render(File.ReadAllBytes(path)) is { } drawing)
            {
                var di = new DrawingImage(drawing);
                di.Freeze();
                img = di;
            }
        }
        catch { } // tête illisible : le nom reste affiché
        Cache[gfx] = img;
        return img;
    }
}
