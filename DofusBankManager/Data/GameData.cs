using System.IO;
using System.Net.Http;
using System.Xml.Linq;

namespace DofusBankManager.Data;

public sealed record ItemInfo(int Id, string Name, int Level, int TypeId, string TypeName);

public sealed record Ingredient(int ItemId, int Quantity);

public sealed record Recipe(int ResultId, IReadOnlyList<Ingredient> Ingredients, IReadOnlyList<string> Jobs);

/// <summary>
/// Données statiques du jeu (objets, recettes, métiers) extraites des fichiers de langue
/// de Dofus Retro, téléchargés depuis le CDN d'Ankama puis mis en cache localement.
/// </summary>
public sealed class GameData
{
    const string DefaultDataUrl = "https://dofusretro.cdn.ankama.com/static-data/612/";
    const string Lang = "fr";
    static readonly string[] Files = ["items", "crafts", "skills", "jobs"];

    public static string AppDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DofusBankManager");

    public Dictionary<int, ItemInfo> Items { get; } = new();
    public List<Recipe> Recipes { get; } = new();
    public List<string> JobNames { get; } = new();

    public ItemInfo GetItem(int id) =>
        Items.TryGetValue(id, out var it) ? it : new ItemInfo(id, $"Objet #{id}", 0, 0, "?");

    public static async Task<GameData> LoadAsync(Action<string> log)
    {
        var dir = Path.Combine(AppDir, "lang");
        Directory.CreateDirectory(dir);
        var paths = await DownloadAsync(dir, log);

        var data = new GameData();
        var items = Avm1.Run(await File.ReadAllBytesAsync(paths["items"]));
        var crafts = Avm1.Run(await File.ReadAllBytesAsync(paths["crafts"]));
        var skills = Avm1.Run(await File.ReadAllBytesAsync(paths["skills"]));
        var jobs = Avm1.Run(await File.ReadAllBytesAsync(paths["jobs"]));

        var types = new Dictionary<int, string>();
        if (items.GetValueOrDefault("I") is AsObject I)
        {
            if (I.GetValueOrDefault("t") is AsObject t)
                foreach (var (k, v) in t)
                    if (v is AsObject o) types[int.Parse(k)] = Avm1.ToStr(o.GetValueOrDefault("n"));
            if (I.GetValueOrDefault("u") is AsObject u)
                foreach (var (k, v) in u)
                {
                    if (v is not AsObject o) continue;
                    int id = int.Parse(k), type = (int)Avm1.ToNum(o.GetValueOrDefault("t"));
                    data.Items[id] = new ItemInfo(id, Avm1.ToStr(o.GetValueOrDefault("n")),
                        (int)Avm1.ToNum(o.GetValueOrDefault("l")), type, types.GetValueOrDefault(type, "?"));
                }
        }

        var jobNames = new Dictionary<int, string>();
        if (jobs.GetValueOrDefault("J") is AsObject J)
            foreach (var (k, v) in J)
                if (v is AsObject o) jobNames[int.Parse(k)] = Avm1.ToStr(o.GetValueOrDefault("n"));

        // Compétence -> liste des objets fabricables : donne le métier de chaque recette
        var jobsByResult = new Dictionary<int, SortedSet<string>>();
        if (skills.GetValueOrDefault("SK") is AsObject SK)
            foreach (var v in SK.Values)
            {
                if (v is not AsObject o || o.GetValueOrDefault("cl") is not List<object?> cl) continue;
                var job = jobNames.GetValueOrDefault((int)Avm1.ToNum(o.GetValueOrDefault("j")), "?");
                if (job == "-Base-") job = "Sans métier";
                foreach (var r in cl)
                {
                    int rid = (int)Avm1.ToNum(r);
                    if (!jobsByResult.TryGetValue(rid, out var set)) jobsByResult[rid] = set = new();
                    set.Add(job);
                }
            }

        if (crafts.GetValueOrDefault("CR") is AsObject CR)
            foreach (var (k, v) in CR)
            {
                if (v is not List<object?> list) continue;
                var ings = list.OfType<List<object?>>()
                    .Where(x => x.Count >= 2)
                    .Select(x => new Ingredient((int)Avm1.ToNum(x[0]), (int)Avm1.ToNum(x[1])))
                    .ToList();
                if (ings.Count == 0) continue;
                int rid = int.Parse(k);
                data.Recipes.Add(new Recipe(rid, ings,
                    jobsByResult.TryGetValue(rid, out var js) ? js.ToList() : ["?"]));
            }

        data.JobNames.AddRange(data.Recipes.SelectMany(r => r.Jobs).Distinct().Order());
        log($"Données du jeu : {data.Items.Count} objets, {data.Recipes.Count} recettes.");
        return data;
    }

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
