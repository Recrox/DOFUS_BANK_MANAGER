using System.IO;
using System.Text.Json;
using DofusBankManager.Data;

namespace DofusBankManager.Hunt;

/// <summary>Monstre d'un groupe : modèle (fichier de langue « monsters ») et niveau.</summary>
public sealed record GroupMonster(int Id, int Level);

/// <summary>Groupe de monstres présent sur la map. Bonus : étoiles du groupe, en % d'XP et de drop.</summary>
public sealed record MapGroup(long SpriteId, int Bonus, List<GroupMonster> Monsters, DateTime SeenAt);

/// <summary>Archi-monstre aperçu sur une map.</summary>
public sealed class ArchiSighting
{
    public DateTime Time { get; set; }
    public int MonsterId { get; set; }
    public int Level { get; set; }
    public string MapId { get; set; } = "";
}

public sealed class FightDrop
{
    public int ItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Combat terminé : ce qu'a gagné le personnage, les monstres affrontés et ses stats à ce moment.</summary>
public sealed class FightRecord
{
    public DateTime Time { get; set; }
    public int DurationSeconds { get; set; }
    public long Xp { get; set; }
    public long Kamas { get; set; }
    public List<FightDrop> Drops { get; set; } = new();
    public List<GroupMonster> Monsters { get; set; } = new();
    public int Wisdom { get; set; }
    public int Prospection { get; set; }
    public int GroupProspection { get; set; }
}

sealed class HuntData
{
    public List<ArchiSighting> Archis { get; set; } = new();
    public List<FightRecord> Fights { get; set; } = new();
    public DateTime SessionStart { get; set; } = DateTime.Now;
    public bool SoundAlert { get; set; } = true;
}

/// <summary>
/// Onglet Chasse : groupes de monstres de la map, archi-monstres aperçus, caractéristiques utiles (sagesse, prospection,
/// prospection du groupe) et résultats de combat. Tout est lu passivement (README, règles 1 et 3) ; des messages lus,
/// seuls les monstres, les niveaux, la prospection et la ligne du personnage sont gardés : les noms des autres joueurs
/// ne sont jamais conservés (règle 4).
/// </summary>
public sealed class HuntTracker
{
    const int MaxArchis = 100, MaxFights = 3000;
    /// <summary>Le même archi-monstre sur la même map n'est signalé qu'une fois dans ce délai.</summary>
    static readonly TimeSpan ArchiRepeat = TimeSpan.FromMinutes(10);
    /// <summary>Sans combat pendant ce délai, le combat suivant ouvre une nouvelle session.</summary>
    static readonly TimeSpan SessionGap = TimeSpan.FromHours(1);

    static string DataPath => Path.Combine(GameData.AppDir, "chasse.json");

    readonly GameData _data;
    readonly Func<long> _characterId;
    HuntData _store = new();

    public string MapId { get; private set; } = "";
    public Dictionary<long, MapGroup> Groups { get; } = new();
    public IReadOnlyList<ArchiSighting> Archis => _store.Archis;
    public IReadOnlyList<FightRecord> Fights => _store.Fights;
    public DateTime SessionStart => _store.SessionStart;

    public bool SoundAlert
    {
        get => _store.SoundAlert;
        set { _store.SoundAlert = value; Save(); }
    }

    /// <summary>Sagesse et prospection totales du personnage (null tant que le message As n'a pas été vu).</summary>
    public int? Wisdom { get; private set; }
    public int? Prospection { get; private set; }
    /// <summary>Membres du groupe : identifiant -> (niveau, prospection). Vide hors groupe.</summary>
    public Dictionary<long, (int Level, int Prospection)> Party { get; } = new();

    /// <summary>Prospection du groupe (somme des membres), sinon celle du personnage.</summary>
    public int? GroupProspection => Party.Count > 0 ? Party.Values.Sum(p => p.Prospection) : Prospection;

    public event Action? Changed;
    public event Action<ArchiSighting>? ArchiSpotted;
    public event Action<string>? Log;

    public HuntTracker(GameData data, Func<long> characterId)
    {
        _data = data;
        _characterId = characterId;
        Load();
    }

    public void NewSession()
    {
        _store.SessionStart = DateTime.Now;
        Save();
        Changed?.Invoke();
    }

    public void ClearArchis()
    {
        _store.Archis.Clear();
        Save();
        Changed?.Invoke();
    }

    public void OnMapChanged(string mapId)
    {
        MapId = mapId;
        Groups.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Sprites de la map : "+cellule;direction;bonus;id;monstres;-3;apparences;niveaux;…" pour un groupe de monstres,
    /// "-id" pour un sprite retiré. Les autres sprites (joueurs, PNJ, marchands…) sont ignorés sans être lus.
    /// </summary>
    public void OnSprites(string body)
    {
        bool changed = false;
        foreach (var s in body.Split('|'))
        {
            if (s.Length < 2) continue;
            if (s[0] == '-')
            {
                if (long.TryParse(s.AsSpan(1), out var gone)) changed |= Groups.Remove(gone);
                continue;
            }
            if (s[0] is not ('+' or '~')) continue;
            var f = s[1..].Split(';');
            if (f.Length < 8 || f[5] != "-3" || !long.TryParse(f[3], out var id)) continue;

            var ids = f[4].Split(',');
            var levels = f[7].Split(',');
            var monsters = new List<GroupMonster>();
            for (int i = 0; i < ids.Length; i++)
                if (int.TryParse(ids[i], out var m))
                    monsters.Add(new GroupMonster(m, i < levels.Length && int.TryParse(levels[i], out var l) ? l : 0));
            if (monsters.Count == 0) continue;

            bool known = Groups.ContainsKey(id);
            Groups[id] = new MapGroup(id, int.TryParse(f[2], out var bonus) ? bonus : 0, monsters, DateTime.Now);
            changed = true;
            if (!known) CheckArchis(monsters);
        }
        if (changed) Changed?.Invoke();
    }

    void CheckArchis(List<GroupMonster> monsters)
    {
        foreach (var m in monsters.Where(m => _data.GetMonster(m.Id).IsArchi))
        {
            var now = DateTime.Now;
            if (_store.Archis.Any(a => a.MonsterId == m.Id && a.MapId == MapId && now - a.Time < ArchiRepeat)) continue;
            var sighting = new ArchiSighting { Time = now, MonsterId = m.Id, Level = m.Level, MapId = MapId };
            _store.Archis.Insert(0, sighting);
            if (_store.Archis.Count > MaxArchis) _store.Archis.RemoveRange(MaxArchis, _store.Archis.Count - MaxArchis);
            Save();
            Log?.Invoke($"Archi-monstre repéré : {_data.GetMonster(m.Id).Name} (niveau {m.Level}, map {MapId}).");
            ArchiSpotted?.Invoke(sighting);
        }
    }

    /// <summary>
    /// Caractéristiques : "xp|kamas|capital|sorts|alignement|pv|énergie|initiative|prospection|PA|PM|force|vitalité|sagesse|…",
    /// chaque caractéristique au format "base,objets,dons,bonus".
    /// </summary>
    public void OnStats(string body)
    {
        var p = body.Split('|');
        if (p.Length < 14) return;
        int? prospection = int.TryParse(p[8].Split(',')[0], out var pp) ? pp : null;
        int? wisdom = Total(p[13]);
        if (prospection == Prospection && wisdom == Wisdom) return;
        Prospection = prospection ?? Prospection;
        Wisdom = wisdom ?? Wisdom;
        Changed?.Invoke();

        static int? Total(string stat)
        {
            var v = stat.Split(',');
            return v.Length >= 2 && v.All(x => int.TryParse(x, out _)) ? v.Sum(int.Parse) : null;
        }
    }

    /// <summary>
    /// Membres du groupe : "+id;nom;apparence;c1;c2;c3;accessoires;pv,pvmax;niveau;initiative;prospection;côté" (plusieurs
    /// séparés par '|'), "~…" pour une mise à jour, "-id" pour un départ ; null quand le groupe est quitté.
    /// Le nom (champ 1) n'est pas lu.
    /// </summary>
    public void OnParty(string? body)
    {
        if (body == null) Party.Clear();
        else if (body[0] == '-')
        {
            if (long.TryParse(body.AsSpan(1), out var gone)) Party.Remove(gone);
        }
        else if (body[0] is '+' or '~')
            foreach (var s in body[1..].Split('|'))
            {
                var f = s.Split(';');
                if (f.Length >= 11 && long.TryParse(f[0], out var id) && int.TryParse(f[8], out var level)
                    && int.TryParse(f[10], out var pp))
                    Party[id] = (level, pp);
            }
        else return;
        Changed?.Invoke();
    }

    /// <summary>
    /// Fin de combat : "durée|initiateur|type|ligne|ligne…". Ligne d'un combattant en combat contre des monstres :
    /// "équipe;id;nom;niveau;mort;xpmin;xp;xpmax;xpgagnée;xpguilde;xpmonture;objets;kamas" (objets "modèle~qté,…").
    /// Seule la ligne du personnage est lue ; pour les monstres (id négatif), le champ « nom » est le modèle du monstre.
    /// </summary>
    public void OnFightEnd(string body)
    {
        var parts = body.Split('|');
        if (parts.Length < 4 || !long.TryParse(parts[0], out var durationMs)) return;
        long me = _characterId();
        FightRecord? fight = null;
        var monsters = new List<GroupMonster>();

        foreach (var line in parts.Skip(3))
        {
            var f = line.Split(';');
            if (f.Length < 4 || !long.TryParse(f[1], out var id)) continue;
            if (id < 0)
            {
                if (int.TryParse(f[2], out var tpl) && _data.Monsters.ContainsKey(tpl))
                    monsters.Add(new GroupMonster(tpl, int.TryParse(f[3], out var l) ? l : 0));
            }
            else if (id == me && me != 0 && f.Length >= 13 && long.TryParse(f[8], out var xp))
            {
                fight = new FightRecord
                {
                    Time = DateTime.Now,
                    DurationSeconds = (int)(durationMs / 1000),
                    Xp = xp,
                    Kamas = long.TryParse(f[12], out var k) ? k : 0,
                    Drops = ParseDrops(f[11]),
                    Wisdom = Wisdom ?? 0,
                    Prospection = Prospection ?? 0,
                    GroupProspection = GroupProspection ?? 0,
                };
            }
        }
        if (fight == null) return;
        fight.Monsters = monsters;

        // Longue pause depuis le dernier combat (et pas de session ouverte à la main depuis) : nouvelle session
        var last = _store.Fights.LastOrDefault();
        if (last != null && fight.Time - last.Time > SessionGap && _store.SessionStart <= last.Time)
            _store.SessionStart = fight.Time.AddSeconds(-fight.DurationSeconds);
        _store.Fights.Add(fight);
        if (_store.Fights.Count > MaxFights) _store.Fights.RemoveRange(0, _store.Fights.Count - MaxFights);
        Save();
        Log?.Invoke($"Combat terminé : {fight.Xp:N0} XP, {fight.Kamas:N0} kamas, {fight.Drops.Sum(d => d.Quantity)} objet(s).");
        Changed?.Invoke();
    }

    static List<FightDrop> ParseDrops(string s)
    {
        var drops = new List<FightDrop>();
        foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split('~');
            if (f.Length >= 2 && int.TryParse(f[0], out var tpl) && int.TryParse(f[1], out var qty))
                drops.Add(new FightDrop { ItemId = tpl, Quantity = qty });
        }
        return drops;
    }

    void Load()
    {
        try
        {
            if (File.Exists(DataPath)) _store = JsonSerializer.Deserialize<HuntData>(File.ReadAllText(DataPath)) ?? new();
        }
        catch { _store = new(); }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(DataPath, JsonSerializer.Serialize(_store));
        }
        catch (Exception ex) { Log?.Invoke("Sauvegarde de la chasse impossible : " + ex.Message); }
    }
}
