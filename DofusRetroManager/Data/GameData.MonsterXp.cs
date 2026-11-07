using System.IO;
using System.Net.Http;
using System.Xml.Linq;

namespace DofusRetroManager.Data;

public sealed partial class GameData
{
    /// <summary>
    /// XP des monstres : { "monsters": [ { "id", "previewGrade": { "level", "xp" } }, … ] }. Livrée avec l'application
    /// (Data/monstres-xp.json) ; un monstres.json
    /// placé dans le dossier de l'application la complète ou la remplace. Les monstres sans XP (0) sont ignorés.
    /// </summary>
    static void LoadMonsterXp(GameData data, Action<string> log)
    {
        using (var bundled = typeof(GameData).Assembly.GetManifestResourceStream("DofusRetroManager.Data.monstres-xp.json"))
            if (bundled != null) ReadMonsterXp(data, new StreamReader(bundled).ReadToEnd(), "données livrées", log);

        var path = Path.Combine(AppDir, "monstres.json");
        if (File.Exists(path)) ReadMonsterXp(data, File.ReadAllText(path), "monstres.json", log);
    }

    static void ReadMonsterXp(GameData data, string json, string source, Action<string> log)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            int count = 0;
            foreach (var m in doc.RootElement.GetProperty("monsters").EnumerateArray())
                if (m.TryGetProperty("id", out var id) && m.TryGetProperty("previewGrade", out var g)
                    && g.TryGetProperty("xp", out var xp) && xp.GetInt64() > 0 && g.TryGetProperty("level", out var level))
                {
                    data.MonsterXp[id.GetInt32()] = (level.GetInt32(), xp.GetInt64());
                    count++;
                }
            log($"XP des monstres : {count} monstres ({source}).");
        }
        catch (Exception ex) { log($"XP des monstres illisible ({source}) : {ex.Message}"); }
    }
}
