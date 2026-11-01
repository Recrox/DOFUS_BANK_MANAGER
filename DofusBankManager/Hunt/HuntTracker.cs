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
    /// <summary>Personnage du joueur qui a fait le combat (multi-compte : un combat par personnage).</summary>
    public string Character { get; set; } = "";
    public int DurationSeconds { get; set; }
    public long Xp { get; set; }
    public long Kamas { get; set; }
    public List<FightDrop> Drops { get; set; } = new();
    public List<GroupMonster> Monsters { get; set; } = new();
    public int Wisdom { get; set; }
    public int Prospection { get; set; }
    public int GroupProspection { get; set; }
}

/// <summary>
/// Personnage suivi sur une connexion au jeu. En multi-compte, chaque fenêtre a sa propre connexion :
/// sagesse, prospection, groupe et map sont tenus séparément pour chacune.
/// </summary>
public sealed class HuntCharacter(string stream, int number)
{
    public string Stream { get; } = stream;
    public int Number { get; } = number;
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int? Level { get; set; }
    public int? Wisdom { get; set; }
    public int? Prospection { get; set; }
    /// <summary>Membres du groupe : identifiant -> (niveau, prospection). Vide hors groupe.</summary>
    public Dictionary<long, (int Level, int Prospection)> Party { get; } = new();
    public string MapId { get; set; } = "";
    public Dictionary<long, MapGroup> Groups { get; } = new();
    /// <summary>Dernier changement de map : la fenêtre où l'on joue est celle qui bouge.</summary>
    public DateTime LastMove { get; set; }
    /// <summary>Dernier message reçu sur cette connexion.</summary>
    public DateTime LastActivity { get; set; } = DateTime.Now;

    /// <summary>Connexion qui a donné quelque chose d'utile (un personnage, des stats ou une map).</summary>
    public bool HasData => Name.Length > 0 || Wisdom != null || Prospection != null || MapId.Length > 0;

    public string Label => Name.Length > 0 ? Name : $"Connexion {Number}";

    /// <summary>Prospection du groupe (somme des membres), sinon celle du personnage.</summary>
    public int? GroupProspection => Party.Count > 0 ? Party.Values.Sum(p => p.Prospection) : Prospection;
    /// <summary>Niveau du groupe (somme des membres), sinon celui du personnage.</summary>
    public int? GroupLevel => Party.Count > 0 ? Party.Values.Sum(p => p.Level) : Level;
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
    /// <summary>Identifiant du dernier personnage connu (sauvegardé avec la banque) : repli si l'appli a démarré en jeu.</summary>
    readonly Func<long> _savedCharacterId;
    readonly Dictionary<string, HuntCharacter> _characters = new();
    HuntData _store = new();

    /// <summary>Connexion du message en cours de traitement (à renseigner avant chaque message).</summary>
    public string Stream { get; set; } = "";

    /// <summary>Connexion choisie dans la liste ; null : la fenêtre où l'on joue (dernier changement de map).</summary>
    public string? Pinned { get; set; }

    /// <summary>Personnages des fenêtres ouvertes (les connexions sans rien d'utile ne sont pas listées).</summary>
    public IEnumerable<HuntCharacter> Characters => _characters.Values.Where(c => c.HasData);

    /// <summary>Personnage affiché.</summary>
    public HuntCharacter? Shown =>
        Pinned != null && _characters.TryGetValue(Pinned, out var c) ? c : Characters.MaxBy(x => x.LastMove);

    /// <summary>Sans message pendant ce délai, une connexion est considérée comme fermée (fermeture non vue).</summary>
    static readonly TimeSpan Inactive = TimeSpan.FromMinutes(20);

    public IReadOnlyList<ArchiSighting> Archis => _store.Archis;
    public IReadOnlyList<FightRecord> Fights => _store.Fights;
    public DateTime SessionStart => _store.SessionStart;

    public bool SoundAlert
    {
        get => _store.SoundAlert;
        set { _store.SoundAlert = value; Save(); }
    }

    public event Action? Changed;
    public event Action<ArchiSighting>? ArchiSpotted;
    public event Action<string>? Log;

    public HuntTracker(GameData data, Func<long> savedCharacterId)
    {
        _data = data;
        _savedCharacterId = savedCharacterId;
        Load();
    }

    HuntCharacter Current
    {
        get
        {
            if (!_characters.TryGetValue(Stream, out var c))
            {
                Prune();
                // Numéro libre le plus petit : « Connexion 1 », « Connexion 2 »… même après des fermetures
                int n = Enumerable.Range(1, _characters.Count + 1).First(i => _characters.Values.All(x => x.Number != i));
                _characters[Stream] = c = new HuntCharacter(Stream, n);
            }
            c.LastActivity = DateTime.Now;
            return c;
        }
    }

    /// <summary>Fenêtre de jeu fermée ou déconnectée : son personnage disparaît de la liste.</summary>
    public void OnStreamClosed(string stream)
    {
        if (!_characters.Remove(stream)) return;
        if (Pinned == stream) Pinned = null;
        Changed?.Invoke();
    }

    /// <summary>Oublie les connexions muettes depuis longtemps (fermeture non vue par la capture).</summary>
    public void Prune()
    {
        foreach (var key in _characters.Where(kv => DateTime.Now - kv.Value.LastActivity > Inactive).Select(kv => kv.Key).ToList())
        {
            _characters.Remove(key);
            if (Pinned == key) Pinned = null;
        }
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

    public void OnCharacterSelected(long id, string name, int level)
    {
        var c = Current;
        c.Id = id;
        c.Name = name;
        if (level > 0) c.Level = level;
        c.Party.Clear();
        // Reconnexion : le même personnage sur une ancienne connexion (fermeture non vue) est retiré
        foreach (var old in _characters.Where(kv => kv.Value != c && kv.Value.Id == id).Select(kv => kv.Key).ToList())
        {
            _characters.Remove(old);
            if (Pinned == old) Pinned = c.Stream;
        }
        Changed?.Invoke();
    }

    public void OnLevelUp(int level)
    {
        Current.Level = level;
        Changed?.Invoke();
    }

    public void OnMapChanged(string mapId)
    {
        var c = Current;
        c.MapId = mapId;
        c.Groups.Clear();
        c.LastMove = DateTime.Now;
        Changed?.Invoke();
    }

    /// <summary>
    /// Sprites de la map : "+cellule;direction;bonus;id;monstres;-3;apparences;niveaux;…" pour un groupe de monstres,
    /// "-id" pour un sprite retiré. Les autres sprites (joueurs, PNJ, marchands…) sont ignorés sans être lus.
    /// </summary>
    public void OnSprites(string body)
    {
        var c = Current;
        bool changed = false;
        foreach (var s in body.Split('|'))
        {
            if (s.Length < 2) continue;
            if (s[0] == '-')
            {
                if (long.TryParse(s.AsSpan(1), out var gone)) changed |= c.Groups.Remove(gone);
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

            bool known = c.Groups.ContainsKey(id);
            c.Groups[id] = new MapGroup(id, int.TryParse(f[2], out var bonus) ? bonus : 0, monsters, DateTime.Now);
            changed = true;
            if (!known) CheckArchis(c.MapId, monsters);
        }
        if (changed) Changed?.Invoke();
    }

    void CheckArchis(string mapId, List<GroupMonster> monsters)
    {
        foreach (var m in monsters.Where(m => _data.GetMonster(m.Id).IsArchi))
        {
            var now = DateTime.Now;
            // Multi-compte : plusieurs fenêtres sur la même map voient le même archi, une seule alerte
            if (_store.Archis.Any(a => a.MonsterId == m.Id && a.MapId == mapId && now - a.Time < ArchiRepeat)) continue;
            var sighting = new ArchiSighting { Time = now, MonsterId = m.Id, Level = m.Level, MapId = mapId };
            _store.Archis.Insert(0, sighting);
            if (_store.Archis.Count > MaxArchis) _store.Archis.RemoveRange(MaxArchis, _store.Archis.Count - MaxArchis);
            Save();
            Log?.Invoke($"Archi-monstre repéré : {_data.GetMonster(m.Id).Name} (niveau {m.Level}, map {mapId}).");
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
        var c = Current;
        int? prospection = int.TryParse(p[8].Split(',')[0], out var pp) ? pp : null;
        int? wisdom = Total(p[13]);
        if (prospection == c.Prospection && wisdom == c.Wisdom) return;
        c.Prospection = prospection ?? c.Prospection;
        c.Wisdom = wisdom ?? c.Wisdom;
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
        var c = Current;
        if (body == null) c.Party.Clear();
        else if (body[0] == '-')
        {
            if (long.TryParse(body.AsSpan(1), out var gone)) c.Party.Remove(gone);
        }
        else if (body[0] is '+' or '~')
            foreach (var s in body[1..].Split('|'))
            {
                var f = s.Split(';');
                if (f.Length >= 11 && long.TryParse(f[0], out var id) && int.TryParse(f[8], out var level)
                    && int.TryParse(f[10], out var pp))
                    c.Party[id] = (level, pp);
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
        var c = Current;
        var monsters = new List<GroupMonster>();
        var players = new List<string[]>();

        foreach (var line in parts.Skip(3))
        {
            var f = line.Split(';');
            if (f.Length < 4 || !long.TryParse(f[1], out var id)) continue;
            if (id < 0)
            {
                if (int.TryParse(f[2], out var tpl) && _data.Monsters.ContainsKey(tpl))
                    monsters.Add(new GroupMonster(tpl, int.TryParse(f[3], out var l) ? l : 0));
            }
            else if (f.Length >= 13 && long.TryParse(f[8], out _)) players.Add(f);
        }

        // Ligne du personnage : son identifiant (appris à la sélection du personnage). Repli si l'appli a démarré en jeu :
        // l'identifiant sauvegardé (une seule fenêtre), ou le seul joueur du combat (combat en solo).
        long me = c.Id != 0 ? c.Id
            : _characters.Count == 1 && players.Any(f => f[1] == _savedCharacterId().ToString()) ? _savedCharacterId()
            : players.Count == 1 ? long.Parse(players[0][1]) : 0;
        var line0 = players.FirstOrDefault(f => f[1] == me.ToString());
        if (line0 == null)
        {
            if (players.Count > 1)
                Log?.Invoke("Fin de combat : personnage inconnu sur cette fenêtre, combat non compté. "
                    + "Lancez l'application avant de choisir votre personnage (ou revenez à la sélection des personnages).");
            return;
        }
        // Personnage retrouvé sans l'avoir vu à la sélection : sa ligne donne son identifiant et son nom
        if (c.Id == 0) c.Id = me;
        if (c.Name.Length == 0) c.Name = line0[2];
        if (c.Level == null && int.TryParse(line0[3], out var lvl)) c.Level = lvl;

        var fight = new FightRecord
        {
            Time = DateTime.Now,
            Character = c.Name,
            DurationSeconds = (int)(durationMs / 1000),
            Xp = long.Parse(line0[8]),
            Kamas = long.TryParse(line0[12], out var k) ? k : 0,
            Drops = ParseDrops(line0[11]),
            Monsters = monsters,
            Wisdom = c.Wisdom ?? 0,
            Prospection = c.Prospection ?? 0,
            GroupProspection = c.GroupProspection ?? 0,
        };

        // Longue pause depuis le dernier combat (et pas de session ouverte à la main depuis) : nouvelle session
        var last = _store.Fights.LastOrDefault();
        if (last != null && fight.Time - last.Time > SessionGap && _store.SessionStart <= last.Time)
            _store.SessionStart = fight.Time.AddSeconds(-fight.DurationSeconds);
        _store.Fights.Add(fight);
        if (_store.Fights.Count > MaxFights) _store.Fights.RemoveRange(0, _store.Fights.Count - MaxFights);
        Save();
        Log?.Invoke($"Combat terminé ({c.Label}) : {fight.Xp:N0} XP, {fight.Kamas:N0} kamas, {fight.Drops.Sum(d => d.Quantity)} objet(s).");
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
