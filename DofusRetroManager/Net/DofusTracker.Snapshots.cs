using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DofusRetroManager.Net;

public sealed partial class DofusTracker
{
    // --- Persistance : la dernière banque connue est conservée entre deux lancements ---

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    static string SnapshotPath(string character) =>
        Path.Combine(BanksDir, string.Concat((character.Length > 0 ? character : "_inconnu").Split(Path.GetInvalidFileNameChars())) + ".json");

    void SaveSnapshot(Snapshot s)
    {
        // Personnage provisoire (fenêtre pas encore reconnue) : rien n'est écrit, pour ne pas mélanger les personnages
        if (s.Character.Length == 0) return;
        try
        {
            Directory.CreateDirectory(BanksDir);
            File.WriteAllText(SnapshotPath(s.Character), JsonSerializer.Serialize(s, JsonOpts));
        }
        catch (Exception ex) { Log?.Invoke($"Sauvegarde impossible : {ex.Message}"); }
    }

    static Snapshot? LoadSnapshot(string character)
    {
        try
        {
            var path = SnapshotPath(character);
            return File.Exists(path) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path)) : null;
        }
        catch { return null; }
    }

    readonly Dictionary<string, (DateTime Time, Snapshot? Snapshot)> _savedCache = new();

    /// <summary>
    /// Banques et inventaires de tous les personnages : celui suivi (état en cours), ceux des autres fenêtres de jeu
    /// ouvertes (état en cours), puis les autres, tels que sauvegardés dans <see cref="BanksDir"/>.
    /// Les fichiers ne sont relus que s'ils ont changé.
    /// </summary>
    public List<Snapshot> AllSnapshots()
    {
        var list = new List<Snapshot> { State };
        foreach (var s in _byStream.Values.Distinct())
            if (s.Character.Length > 0 && list.All(x => x.Character != s.Character)) list.Add(s);
        if (!Directory.Exists(BanksDir)) return list;
        foreach (var f in new DirectoryInfo(BanksDir).GetFiles("*.json"))
        {
            if (!_savedCache.TryGetValue(f.FullName, out var c) || c.Time != f.LastWriteTimeUtc)
            {
                Snapshot? s = null;
                try { s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(f.FullName)); } catch { }
                _savedCache[f.FullName] = c = (f.LastWriteTimeUtc, s);
            }
            if (c.Snapshot is { Character.Length: > 0 } saved && list.All(x => x.Character != saved.Character)) list.Add(saved);
        }
        return list;
    }

    /// <summary>Recharge la banque la plus récemment sauvegardée (au démarrage).</summary>
    public void LoadLatest()
    {
        if (!Directory.Exists(BanksDir)) return;
        var latest = new DirectoryInfo(BanksDir).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
        if (latest == null) return;
        try
        {
            var s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(latest.FullName));
            if (s == null) return;
            State = s;
            Log?.Invoke($"Dernière banque connue chargée : {s.Character} ({s.Bank.Count} objets, {s.BankUpdatedAt:g}).");
            BankChanged?.Invoke();
        }
        catch { }
    }
}
