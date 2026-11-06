using System.IO;
using System.Text.Json;

namespace DofusRetroManager.Data;

/// <summary>
/// Jets observés : pour chaque objet et chaque caractéristique, la plus petite et la plus grande valeur vues sur de vrais
/// exemplaires (offres consultées à l'HDV, objets de vos banques et inventaires). Le fichier de langue des objets ne donne
/// que le maximum (« ef ») ; le minimum s'apprend ainsi au fil des consultations. Seules les valeurs comprises entre 1 et le
/// maximum du jeu sont retenues (un objet forgemagé au-delà, ou une caractéristique ajoutée, est ignoré).
/// Rien ne sort de ce PC (jets.json).
/// </summary>
public sealed class ItemRolls
{
    /// <summary>Plus petite et plus grande valeur vues, et nombre d'exemplaires observés.</summary>
    public sealed class Roll
    {
        public int Min { get; set; } = int.MaxValue;
        public int Max { get; set; }
        public int Seen { get; set; }
    }

    static string DataPath => Path.Combine(GameData.AppDir, "jets.json");

    /// <summary>Objet -> caractéristique -> jet.</summary>
    Dictionary<int, Dictionary<int, Roll>> _rolls = new();
    /// <summary>Exemplaires déjà comptés (identifiant d'offre ou d'objet), pour ne pas compter deux fois le même.</summary>
    readonly HashSet<long> _counted = new();
    bool _dirty;
    DateTime _saved;

    public ItemRolls()
    {
        try { if (File.Exists(DataPath)) _rolls = JsonSerializer.Deserialize<Dictionary<int, Dictionary<int, Roll>>>(File.ReadAllText(DataPath)) ?? new(); }
        catch { _rolls = new(); }
    }

    public Roll? Of(int item, int characteristic) =>
        _rolls.TryGetValue(item, out var r) && r.TryGetValue(characteristic, out var roll) && roll.Seen > 0 ? roll : null;

    /// <summary>Un exemplaire réel de l'objet (id : offre ou objet, 0 si inconnu) et ses caractéristiques.</summary>
    public void Observe(ItemInfo item, long id, IReadOnlyDictionary<int, int> stats)
    {
        if (item.MaxStats.Count == 0 || stats.Count == 0) return;
        if (id != 0 && !_counted.Add(id)) return;
        foreach (var (c, v) in stats)
        {
            if (!item.MaxStats.TryGetValue(c, out var max) || max <= 0 || v < 1 || v > max) continue;
            if (!_rolls.TryGetValue(item.Id, out var byStat)) _rolls[item.Id] = byStat = new();
            if (!byStat.TryGetValue(c, out var roll)) byStat[c] = roll = new();
            roll.Min = Math.Min(roll.Min, v);
            roll.Max = Math.Max(roll.Max, v);
            roll.Seen++;
            _dirty = true;
        }
        if (_dirty && DateTime.Now - _saved > TimeSpan.FromSeconds(30)) Save();
    }

    public void Save()
    {
        if (!_dirty) return;
        _dirty = false;
        _saved = DateTime.Now;
        try { File.WriteAllText(DataPath, JsonSerializer.Serialize(_rolls)); } catch { }
    }
}
