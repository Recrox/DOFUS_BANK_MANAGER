using System.IO;

namespace DofusRetroManager.Data;

/// <summary>
/// Lit, en lecture seule, une map du client installé (data/maps/{id}_{date}X.swf) pour y trouver les ressources
/// récoltables. Le contenu des maps est chiffré : la clé n'arrive que dans le message GDM, quand un personnage entre
/// sur la map. Rien n'est envoyé au serveur, rien n'est modifié dans le client (règles 1 et 2).
/// </summary>
public static class MapDecoder
{
    static readonly string MapsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Retro", "resources", "app", "retroclient", "data", "maps");

    const string Hash = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
    const string HexChars = "0123456789ABCDEF";

    /// <summary>Case portant un objet interactif : numéro de case et graphisme (calque d'objets 2).</summary>
    public readonly record struct InteractiveCell(int Cell, int Gfx);

    /// <summary>Cases interactives de la map, ou null si le fichier est introuvable ou la clé incorrecte.</summary>
    public static List<InteractiveCell>? InteractiveCells(int mapId, string date, string key)
    {
        var path = Path.Combine(MapsDir, $"{mapId}_{date}X.swf");
        if (!File.Exists(path)) path = Path.Combine(MapsDir, $"{mapId}_{date}.swf");
        if (!File.Exists(path)) return null;

        var data = Avm1.ToStr(Avm1.Run(File.ReadAllBytes(path)).GetValueOrDefault("mapData"));
        if (data.Length == 0) return null;
        if (path.EndsWith("X.swf"))
        {
            if (key.Length == 0) return null;
            data = Decipher(data, PrepareKey(key));
        }
        if (data.Length == 0 || data.Length % 10 != 0 || data.Any(c => Hash.IndexOf(c) < 0)) return null;

        var cells = new List<InteractiveCell>();
        for (int i = 0, cell = 0; i < data.Length; i += 10, cell++)
        {
            var v = new int[10];
            for (int j = 0; j < 10; j++) v[j] = Hash.IndexOf(data[i + j]);
            bool interactive = (v[7] & 2) != 0;
            int obj2 = ((v[0] & 2) << 12) + ((v[7] & 1) << 12) + (v[8] << 6) + v[9];
            if (interactive && obj2 > 0) cells.Add(new InteractiveCell(cell, obj2));
        }
        return cells;
    }

    /// <summary>Clé du message GDM : hexadécimal, puis séquences « %xx » décodées.</summary>
    static string PrepareKey(string key)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i + 1 < key.Length; i += 2) sb.Append((char)Convert.ToInt32(key.Substring(i, 2), 16));
        return Unescape(sb.ToString());
    }

    static string Decipher(string data, string key)
    {
        if (key.Length == 0) return "";
        int checksum = HexChars.IndexOf(HexChars[key.Sum(c => c % 16) % 16]) * 2;
        var sb = new System.Text.StringBuilder(data.Length / 2);
        for (int i = 0, n = 0; i + 1 < data.Length; i += 2, n++)
            sb.Append((char)(Convert.ToInt32(data.Substring(i, 2), 16) ^ key[(n + checksum) % key.Length]));
        return Unescape(sb.ToString());
    }

    /// <summary>« unescape » de Flash : %XX et %uXXXX deviennent un caractère (sans décodage UTF-8), le reste est gardé.</summary>
    static string Unescape(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 5 < s.Length + 0 && s[i + 1] == 'u'
                && int.TryParse(s.AsSpan(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out var u))
            { sb.Append((char)u); i += 5; }
            else if (s[i] == '%' && i + 2 < s.Length
                && int.TryParse(s.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            { sb.Append((char)b); i += 2; }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
