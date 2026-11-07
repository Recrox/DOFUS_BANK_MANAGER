using System.IO;
using System.Net.Http;
using System.Xml.Linq;

namespace DofusRetroManager.Data;

public sealed partial class GameData
{
    /// <summary>Télécharge les fichiers de langue à jour ; retombe sur le cache si le CDN est injoignable.</summary>
    static async Task<Dictionary<string, string>> DownloadAsync(string dir, Action<string> log)
    {
        var result = new Dictionary<string, string>();
        var baseUrl = FindDataUrl();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var versions = await http.GetStringAsync($"{baseUrl}lang/versions_{Lang}.txt");
            // Format : &f=items,fr,1395|crafts,fr,1393|...
            foreach (var entry in versions.Replace("&f=", "").Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(',');
                if (parts.Length < 3 || !Files.Contains(parts[0])) continue;
                var name = $"{parts[0]}_{parts[1]}_{parts[2].Trim()}.swf";
                var path = Path.Combine(dir, name);
                if (!File.Exists(path))
                {
                    log($"Téléchargement de {name}…");
                    var bytes = await http.GetByteArrayAsync($"{baseUrl}lang/swf/{name}");
                    await File.WriteAllBytesAsync(path, bytes);
                }
                result[parts[0]] = path;
            }
        }
        catch (Exception ex)
        {
            log($"CDN injoignable ({ex.Message}), utilisation du cache local.");
        }

        foreach (var f in Files.Where(f => !result.ContainsKey(f)))
        {
            var cached = Directory.GetFiles(dir, $"{f}_{Lang}_*.swf")
                .OrderByDescending(p => int.TryParse(Path.GetFileNameWithoutExtension(p).Split('_').Last(), out var v) ? v : 0)
                .FirstOrDefault();
            if (cached == null && Optional.Contains(f))
            {
                log($"Fichier de langue '{f}' indisponible pour l'instant : fonctions associées limitées.");
                continue;
            }
            result[f] = cached ?? throw new FileNotFoundException(
                $"Fichier de langue '{f}' introuvable (ni sur le CDN, ni en cache).");
        }
        return result;
    }

    /// <summary>Lit l'URL des données dans le config.xml du client installé, si présent.</summary>
    static string FindDataUrl()
    {
        try
        {
            var cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ankama", "Retro", "resources", "app", "retroclient", "config.xml");
            if (File.Exists(cfg))
            {
                var url = XDocument.Load(cfg).Descendants("dataserver")
                    .Select(e => (string?)e.Attribute("url"))
                    .FirstOrDefault(u => u != null && u.StartsWith("http"));
                if (url != null) return url.EndsWith('/') ? url : url + "/";
            }
        }
        catch { }
        return DefaultDataUrl;
    }
}
