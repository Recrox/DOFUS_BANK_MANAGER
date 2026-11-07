using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hunt;

/// <summary>Monstre d'un groupe : modèle (fichier de langue « monsters ») et niveau.</summary>
public sealed record GroupMonster(int Id, int Level);

/// <summary>
/// Alerte du joueur : prévenir (son et bulle) quand un groupe de la map contient au moins MinCount monstres de ce type,
/// de niveau MinLevel à MaxLevel (ex. 3 Craqueleurs des plaines de niveau 49 au plus).
/// </summary>
public sealed class MonsterAlert
{
    public int MonsterId { get; set; }
    public int MinCount { get; set; } = 1;
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 9999;
    public bool Enabled { get; set; } = true;
}

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
    /// <summary>Initiative (onglet Switcheur : ordre de jeu des personnages).</summary>
    public int? Initiative { get; set; }
    /// <summary>Membres du groupe : identifiant -> (niveau, prospection). Vide hors groupe.</summary>
    public Dictionary<long, (int Level, int Prospection)> Party { get; } = new();
    public string MapId { get; set; } = "";
    public Dictionary<long, MapGroup> Groups { get; } = new();
    /// <summary>Dernier changement de map : la fenêtre où l'on joue est celle qui bouge.</summary>
    public DateTime LastMove { get; set; }
    /// <summary>Dernier message reçu sur cette connexion.</summary>
    public DateTime LastActivity { get; set; } = DateTime.Now;

    /// <summary>Économe : en combat, et sorts déjà lancés dans ce combat (dans l'ordre).</summary>
    public bool InFight { get; set; }
    public List<int> UsedSpells { get; } = new();
    /// <summary>Sorts reçus sur cette connexion (la liste peut arriver avant le nom du personnage).</summary>
    public List<int>? KnownSpells { get; set; }

    /// <summary>Points de vie du personnage (As, puis GTM en combat).</summary>
    public int? Life { get; set; }
    public int? MaxLife { get; set; }
    /// <summary>Heure du dernier relevé des points de vie : hors combat, ils remontent ensuite d'un point par intervalle de RegenMs.</summary>
    public DateTime LifeAt { get; set; } = DateTime.Now;
    /// <summary>Régénération hors combat : un point de vie toutes les RegenMs millisecondes (ILS du jeu, sinon 1 s).</summary>
    public double RegenMs { get; set; } = 1000;
    /// <summary>Points de vie estimés maintenant : ceux relevés, plus la régénération hors combat depuis (jamais au-delà du maximum).</summary>
    public int? CurrentLife => Life is not { } l ? null
        : InFight || MaxLife is not { } m ? l
        : (int)Math.Min(m, l + Math.Max(0, (DateTime.Now - LifeAt).TotalMilliseconds) / Math.Max(100, RegenMs));
    public int? LifePercent => CurrentLife is { } l && MaxLife is { } m && m > 0 ? (int)Math.Round(100.0 * Math.Clamp(l, 0, m) / m) : null;

    /// <summary>Début et durée (ms) du tour de jeu du personnage en cours (null : ce n'est pas son tour).</summary>
    public DateTime? TurnStart { get; set; }
    public int TurnMs { get; set; }

    /// <summary>Challenges du combat en cours (message Gd) : identifiant -> réussi (true), raté (false) ou en cours (null).</summary>
    public Dictionary<int, bool?> Challenges { get; } = new();

    /// <summary>Connexion qui a donné quelque chose d'utile (un personnage, des stats ou une map).</summary>
    public bool HasData => Name.Length > 0 || Wisdom != null || Prospection != null || MapId.Length > 0;

    public string Label => Name.Length > 0 ? Name : $"Connexion {Number}";

    /// <summary>Prospection du groupe (somme des membres), sinon celle du personnage.</summary>
    public int? GroupProspection => Party.Count > 0 ? Party.Values.Sum(p => p.Prospection) : Prospection;
    /// <summary>Niveau du groupe (somme des membres), sinon celui du personnage.</summary>
    public int? GroupLevel => Party.Count > 0 ? Party.Values.Sum(p => p.Level) : Level;
}

/// <summary>Évènements notifiés dans l'onglet Switcheur.</summary>
public enum GameNotice { Fight, Turn, Exchange, Party }

/// <summary>
/// Dernier état connu d'un de vos personnages, gardé d'un lancement à l'autre (chasse.json) : l'application, relancée
/// pendant que le jeu est ouvert (mise à jour…), retrouve son ordre de jeu, sa vie et son identifiant sans reconnexion.
/// </summary>
public sealed class KnownCharacter
{
    public long Id { get; set; }
    public int? Level { get; set; }
    public int? Initiative { get; set; }
    public int? Wisdom { get; set; }
    public int? Prospection { get; set; }
    public int? Life { get; set; }
    public int? MaxLife { get; set; }
    public DateTime LifeAt { get; set; }
    public double RegenMs { get; set; } = 1000;

    /// <summary>Vie estimée maintenant (hors combat : régénération depuis le dernier relevé), en pourcentage.</summary>
    public int? LifePercent => Life is not { } l || MaxLife is not { } m || m <= 0 ? null
        : (int)Math.Round(100.0 * Math.Clamp(l + Math.Max(0, (DateTime.Now - LifeAt).TotalMilliseconds) / Math.Max(100, RegenMs), 0, m) / m);
}

sealed class HuntData
{
    public List<ArchiSighting> Archis { get; set; } = new();
    public List<FightRecord> Fights { get; set; } = new();
    public DateTime SessionStart { get; set; } = DateTime.Now;
    /// <summary>Session en pause depuis (null : en cours) et durée des pauses déjà terminées, en secondes.</summary>
    public DateTime? SessionPausedAt { get; set; }
    public double SessionPausedSeconds { get; set; }
    public bool SoundAlert { get; set; } = true;
    /// <summary>Volume de l'alerte archi-monstre, de 0 à 100.</summary>
    public int SoundVolume { get; set; } = 70;
    public bool ArchiPopup { get; set; } = true;
    public List<MonsterAlert> Alerts { get; set; } = new();
    /// <summary>Sorts connus par personnage (nom -> sorts), pour l'Économe même si l'appli démarre en jeu.</summary>
    public Dictionary<string, List<int>> Spells { get; set; } = new();
    /// <summary>Classe et sexe par personnage (nom -> apparence), pour son icône même si l'appli démarre en jeu.</summary>
    public Dictionary<string, CharacterLook> Looks { get; set; } = new();
    /// <summary>Dernier état connu de vos personnages (nom -> identifiant, niveau, initiative, vie…).</summary>
    public Dictionary<string, KnownCharacter> Known { get; set; } = new();
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
    public bool SessionPaused => _store.SessionPausedAt != null;

    /// <summary>Durée de la session, pauses déduites (absence du joueur).</summary>
    public TimeSpan SessionDuration =>
        (_store.SessionPausedAt ?? DateTime.Now) - _store.SessionStart - TimeSpan.FromSeconds(_store.SessionPausedSeconds);

    /// <summary>Met la session en pause (le temps ne compte plus) ou la reprend.</summary>
    public void PauseSession(bool pause)
    {
        if (pause == SessionPaused) return;
        if (pause) _store.SessionPausedAt = DateTime.Now;
        else ResumeSession(DateTime.Now);
        Save();
        Changed?.Invoke();
    }

    void ResumeSession(DateTime at)
    {
        if (_store.SessionPausedAt is not { } since) return;
        if (at > since) _store.SessionPausedSeconds += (at - since).TotalSeconds;
        _store.SessionPausedAt = null;
    }

    public bool SoundAlert
    {
        get => _store.SoundAlert;
        set { _store.SoundAlert = value; Save(); }
    }

    /// <summary>Pop-up au premier plan quand un archi-monstre apparaît sur la map.</summary>
    public bool ArchiPopup
    {
        get => _store.ArchiPopup;
        set { _store.ArchiPopup = value; Save(); }
    }

    /// <summary>Alertes monstres du joueur (monstre, nombre minimum, niveaux).</summary>
    public List<MonsterAlert> Alerts => _store.Alerts;

    public void SaveAlerts() => Save();

    public int SoundVolume
    {
        get => _store.SoundVolume;
        set { _store.SoundVolume = Math.Clamp(value, 0, 100); Save(); }
    }

    public event Action? Changed;
    public event Action<ArchiSighting>? ArchiSpotted;
    /// <summary>Un groupe de la map répond à une alerte monstre du joueur (personnage qui le voit, alerte, nombre trouvé).</summary>
    public event Action<HuntCharacter, MonsterAlert, int>? AlertMatched;
    public event Action<string>? Log;
    /// <summary>
    /// Évènement à notifier (onglet Switcheur) : personnage de la connexion, type, et nom du personnage concerné quand le
    /// message le donne (invitation de groupe : l'invité), pour retrouver sa fenêtre même si son nom n'est pas encore connu.
    /// </summary>
    public event Action<HuntCharacter, GameNotice, string?>? Notice;
    /// <summary>Le personnage entre dans un groupe (il était seul) : l'onglet Switcheur peut figer l'ordre de jeu.</summary>
    public event Action<HuntCharacter>? PartyJoined;
    /// <summary>Personnage d'une connexion reconnu sans sa sélection (connexion, nom) : la banque suit la même fenêtre.</summary>
    public event Action<string, string>? Identified;

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
        _store.SessionPausedAt = null;
        _store.SessionPausedSeconds = 0;
        Save();
        Changed?.Invoke();
    }

    public void ClearArchis()
    {
        _store.Archis.Clear();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Classe et sexe d'un personnage du joueur, vus à sa sélection (lors de ce lancement ou d'un précédent).</summary>
    public CharacterLook? LookOf(string name) => name.Length == 0 ? null
        : _store.Looks.GetValueOrDefault(name) ?? _store.Looks.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    // --- Mémoire des personnages : l'application relancée pendant que le jeu est ouvert les retrouve sans reconnexion ---

    DateTime _knownSaved;
    bool _knownDirty;

    /// <summary>Dernier état connu d'un personnage, par son nom (celui du titre de sa fenêtre).</summary>
    public KnownCharacter? KnownOf(string name) => name.Length == 0 ? null
        : _store.Known.GetValueOrDefault(name) ?? _store.Known.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Retient l'état du personnage ; le fichier est réécrit au plus toutes les 30 s (et à la fermeture).</summary>
    void Remember(HuntCharacter c)
    {
        if (c.Name.Length == 0) return;
        if (!_store.Known.TryGetValue(c.Name, out var k)) _store.Known[c.Name] = k = new KnownCharacter();
        if (c.Id != 0) k.Id = c.Id;
        k.Level = c.Level ?? k.Level;
        k.Initiative = c.Initiative ?? k.Initiative;
        k.Wisdom = c.Wisdom ?? k.Wisdom;
        k.Prospection = c.Prospection ?? k.Prospection;
        if (c.Life != null && !c.InFight)
        {
            k.Life = c.Life;
            k.MaxLife = c.MaxLife;
            k.LifeAt = c.LifeAt;
            k.RegenMs = c.RegenMs;
        }
        _knownDirty = true;
        if (DateTime.Now - _knownSaved > TimeSpan.FromSeconds(30)) SaveIfDirty();
    }

    /// <summary>Écrit ce qui reste à enregistrer (appelé à la fermeture de l'application).</summary>
    public void SaveIfDirty()
    {
        if (!_knownDirty) return;
        _knownDirty = false;
        _knownSaved = DateTime.Now;
        Save();
    }

    /// <summary>Connexion dont le personnage n'est pas encore connu (application lancée après la connexion au jeu).</summary>
    public bool NeedsName(string stream) => _characters.TryGetValue(stream, out var c) && c.Name.Length == 0;

    /// <summary>
    /// Personnage de la connexion retrouvé sans le voir à la sélection (sa fenêtre, ou son identifiant) : son dernier état
    /// connu est repris (identifiant, niveau, initiative, vie…), puis remplacé par ce que le jeu envoie ensuite.
    /// </summary>
    public bool Identify(string stream, string name)
    {
        if (!_characters.TryGetValue(stream, out var c) || c.Name.Length > 0) return false;
        // Le personnage est déjà suivi sur une autre connexion encore active : pas de doublon
        if (_characters.Values.Any(x => x != c && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return false;
        c.Name = name;
        if (KnownOf(name) is { } k)
        {
            if (c.Id == 0) c.Id = k.Id;
            c.Level ??= k.Level;
            c.Initiative ??= k.Initiative;
            c.Wisdom ??= k.Wisdom;
            c.Prospection ??= k.Prospection;
            if (c.Life == null && k.Life != null)
            {
                c.Life = k.Life;
                c.MaxLife = k.MaxLife;
                c.LifeAt = k.LifeAt;
                c.RegenMs = k.RegenMs;
            }
        }
        Log?.Invoke($"Fenêtre reconnue : {name}.");
        Identified?.Invoke(stream, name);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Identifiant appris en jeu (action, fin de combat) : s'il est celui d'un de vos personnages connus, il est reconnu.</summary>
    void IdentifyById(HuntCharacter c)
    {
        if (c.Name.Length > 0 || c.Id == 0) return;
        var known = _store.Known.FirstOrDefault(kv => kv.Value.Id == c.Id).Key;
        if (known != null) Identify(c.Stream, known);
    }

    public void OnCharacterSelected(long id, string name, int level, CharacterLook? look)
    {
        var c = Current;
        c.Id = id;
        c.Name = name;
        if (level > 0) c.Level = level;
        if (look != null && !look.Equals(_store.Looks.GetValueOrDefault(name)))
        {
            _store.Looks[name] = look;
            Save();
        }
        c.Party.Clear();
        SaveSpells(c);
        // Reconnexion : le même personnage sur une ancienne connexion (fermeture non vue) est retiré
        foreach (var old in _characters.Where(kv => kv.Value != c && kv.Value.Id == id).Select(kv => kv.Key).ToList())
        {
            _characters.Remove(old);
            if (Pinned == old) Pinned = c.Stream;
        }
        Remember(c);
        Changed?.Invoke();
    }

    public void OnLevelUp(int level)
    {
        Current.Level = level;
        Remember(Current);
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
            // Personnage de la fenêtre, reconnu par son nom (application relancée après le jeu : identifiant inconnu,
            // nécessaire pour le début de son tour). Seul son identifiant est gardé ; les autres joueurs sont ignorés.
            if (c.Id == 0 && c.Name.Length > 0 && f.Length > 5 && f[5] is not ("-3" or "-1" or "-2")
                && string.Equals(f[4], c.Name, StringComparison.OrdinalIgnoreCase) && long.TryParse(f[3], out var own) && own > 0)
            {
                c.Id = own;
                Remember(c);
                Log?.Invoke($"Identifiant de {c.Name} retrouvé sur la map.");
                continue;
            }
            if (f.Length < 8 || f[5] != "-3" || !long.TryParse(f[3], out var id)) continue;

            var ids = f[4].Split(',');
            var levels = f[7].Split(',');
            var monsters = new List<GroupMonster>();
            // Un boss est affiché sans niveau (-1, ex. Kralamoure Géant dans son antre) : on prend son grade 3
            for (int i = 0; i < ids.Length; i++)
                if (int.TryParse(ids[i], out var m))
                {
                    int level = i < levels.Length && int.TryParse(levels[i], out var l) ? l : 0;
                    monsters.Add(new GroupMonster(m, level >= 1 ? level : _data.GetMonster(m).Grade3Level));
                }
            if (monsters.Count == 0) continue;

            bool known = c.Groups.ContainsKey(id);
            c.Groups[id] = new MapGroup(id, int.TryParse(f[2], out var bonus) ? bonus : 0, monsters, DateTime.Now);
            changed = true;
            if (!known) { CheckArchis(c.MapId, monsters); CheckAlerts(c, monsters); }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Dernière alerte par map et par règle : plusieurs fenêtres sur la même map, ou un groupe revu, ne sonnent qu'une fois.</summary>
    readonly Dictionary<(string Map, MonsterAlert Alert), DateTime> _alerted = new();

    void CheckAlerts(HuntCharacter c, List<GroupMonster> monsters)
    {
        var now = DateTime.Now;
        foreach (var alert in _store.Alerts.Where(a => a.Enabled))
        {
            int count = monsters.Count(m => m.Id == alert.MonsterId && m.Level >= alert.MinLevel && m.Level <= alert.MaxLevel);
            if (count < Math.Max(1, alert.MinCount)) continue;
            if (_alerted.TryGetValue((c.MapId, alert), out var last) && now - last < ArchiRepeat) continue;
            _alerted[(c.MapId, alert)] = now;
            AlertMatched?.Invoke(c, alert, count);
        }
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
    /// chaque caractéristique au format "base,objets,dons,bonus[,additionnel]". Le 5e morceau (colonne « Additionnel » :
    /// parchemins) est déjà compté dans la base : seuls les 4 premiers sont additionnés.
    /// </summary>
    public void OnStats(string body)
    {
        var p = body.Split('|');
        if (p.Length < 14) return;
        var c = Current;
        int? prospection = int.TryParse(p[8].Split(',')[0], out var pp) ? pp : null;
        // En combat, l'initiative suit les points de vie et les effets : elle n'est retenue que hors combat,
        // sinon l'ordre de jeu (onglet Switcheur) changerait en plein combat
        int? initiative = !c.InFight && int.TryParse(p[7].Split(',')[0], out var ini) ? ini : c.Initiative;
        int? wisdom = Total(p[13]);
        var life = p[5].Split(',');
        int? hp = life.Length >= 2 && int.TryParse(life[0], out var l) ? l : c.Life;
        int? hpMax = life.Length >= 2 && int.TryParse(life[1], out var lm) ? lm : c.MaxLife;
        if (life.Length >= 2) c.LifeAt = DateTime.Now; // vie exacte du jeu : la régénération estimée repart d'ici
        if (prospection == c.Prospection && wisdom == c.Wisdom && initiative == c.Initiative && hp == c.Life && hpMax == c.MaxLife) return;
        c.Prospection = prospection ?? c.Prospection;
        c.Initiative = initiative ?? c.Initiative;
        c.Wisdom = wisdom ?? c.Wisdom;
        c.Life = hp;
        c.MaxLife = hpMax;
        Remember(c);
        Changed?.Invoke();

        static int? Total(string stat)
        {
            var v = stat.Split(',').Take(4).ToArray();
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
        bool wasAlone = c.Party.Count == 0;
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
        if (wasAlone && c.Party.Count > 1) PartyJoined?.Invoke(c);
    }

    /// <summary>
    /// Début de tour (GTM|id;0;pv;PA;PM;case;;pvmax|… ou id;1 pour un combattant mort) : seuls les points de vie
    /// du personnage de cette fenêtre sont lus ; les lignes des autres combattants sont ignorées.
    /// </summary>
    public void OnFightersUpdate(string body)
    {
        var c = Current;
        if (c.Id == 0) return;
        foreach (var s in body.Split('|'))
        {
            var f = s.Split(';');
            if (f.Length < 1 || !long.TryParse(f[0], out var id) || id != c.Id) continue;
            int? hp = f.Length >= 2 && f[1] == "1" ? 0 : f.Length >= 3 && int.TryParse(f[2], out var l) ? l : c.Life;
            int? hpMax = f.Length >= 8 && int.TryParse(f[7], out var m) ? m : c.MaxLife;
            if (hp == c.Life && hpMax == c.MaxLife) return;
            c.Life = hp;
            c.MaxLife = hpMax;
            Changed?.Invoke();
            return;
        }
    }

    /// <summary>Challenges de combat (fichier de langue « fightChallenge ») suivis par le chronomètre de l'onglet Combat.</summary>
    public const int Econome = 5, Barbare = 9;

    /// <summary>
    /// Challenge du combat : "Gd{id};{cible visible};{cible};{bonus XP};{bonus drop}" à l'annonce, puis "GdO{id}" (réussi)
    /// ou "GdK{id}" (raté). Seuls l'identifiant du challenge et son résultat sont lus.
    /// </summary>
    public void OnChallenge(string body)
    {
        if (body.Length == 0) return;
        var c = Current;
        if (body[0] is 'O' or 'K' && int.TryParse(body.TrimStart('O', 'K').Split(';')[0], out var done))
            c.Challenges[done] = body[0] == 'O';
        else if (int.TryParse(body.Split(';')[0], out var id))
            c.Challenges.TryAdd(id, null);
        else return;
        Changed?.Invoke();
    }

    /// <summary>
    /// Fin de combat : "durée|initiateur|type|ligne|ligne…". Ligne d'un combattant en combat contre des monstres :
    /// "équipe;id;nom;niveau;mort;xpmin;xp;xpmax;?;xpgagnée;xpguilde;xpmonture;objets;kamas" (objets "modèle~qté,…") :
    /// XP gagnée en 9, objets en 12, kamas en 13.
    /// Seule la ligne du personnage est lue ; pour les monstres (id négatif), le champ « nom » est le modèle du monstre.
    /// </summary>
    public void OnFightEnd(string body)
    {
        var parts = body.Split('|');
        // Durée, parfois suivie d'autres champs ("durée;bonus")
        if (parts.Length < 4 || !long.TryParse(parts[0].Split(';')[0], out var durationMs))
        {
            Log?.Invoke($"Fin de combat non reconnue ({Shape(parts)}).");
            return;
        }
        var c = Current;
        c.InFight = false; // l'Économe garde les sorts du combat affichés jusqu'au suivant
        c.LifeAt = DateTime.Now; // la vie remonte à partir de la fin du combat
        c.TurnStart = null;
        var monsters = new List<GroupMonster>();
        var players = new List<string[]>();

        // Lignes des combattants : les segments à champs séparés par ';' (initiateur et type de combat n'en ont pas)
        foreach (var line in parts.Skip(1))
        {
            var f = line.Split(';');
            if (f.Length < 4 || !long.TryParse(f[1], out var id)) continue;
            if (id < 0)
            {
                if (int.TryParse(f[2], out var tpl) && _data.Monsters.ContainsKey(tpl))
                    monsters.Add(new GroupMonster(tpl, int.TryParse(f[3], out var l) ? l : 0));
            }
            else if (f.Length >= 14 && long.TryParse(f[9], out _)) players.Add(f);
        }

        // Ligne du personnage : son identifiant (appris à la sélection du personnage). Repli si l'appli a démarré en jeu :
        // l'identifiant sauvegardé (une seule fenêtre), ou le seul joueur du combat (combat en solo).
        long me = c.Id != 0 ? c.Id
            : _characters.Count == 1 && players.Any(f => f[1] == _savedCharacterId().ToString()) ? _savedCharacterId()
            : players.Count == 1 ? long.Parse(players[0][1]) : 0;
        var line0 = players.FirstOrDefault(f => f[1] == me.ToString());
        if (line0 == null)
        {
            if (players.Count == 0)
                Log?.Invoke($"Fin de combat : ligne du personnage non reconnue ({Shape(parts)}), combat non compté.");
            else if (players.Count > 1)
                Log?.Invoke("Fin de combat : personnage inconnu sur cette fenêtre, combat non compté. "
                    + "Lancez l'application avant de choisir votre personnage (ou revenez à la sélection des personnages).");
            return;
        }
        // Personnage retrouvé sans l'avoir vu à la sélection : sa ligne donne son identifiant et son nom
        if (c.Id == 0) { c.Id = me; IdentifyById(c); }
        if (c.Name.Length == 0) c.Name = line0[2];
        if (c.Level == null && int.TryParse(line0[3], out var lvl)) c.Level = lvl;
        Remember(c);

        var fight = new FightRecord
        {
            Time = DateTime.Now,
            Character = c.Name,
            DurationSeconds = (int)(durationMs / 1000),
            Xp = long.Parse(line0[9]),
            Kamas = long.TryParse(line0[13], out var k) ? k : 0,
            Drops = ParseDrops(line0[12]),
            Monsters = monsters,
            Wisdom = c.Wisdom ?? 0,
            Prospection = c.Prospection ?? 0,
            GroupProspection = c.GroupProspection ?? 0,
        };

        // Longue pause depuis le dernier combat (et pas de session ouverte à la main depuis) : nouvelle session
        var last = _store.Fights.LastOrDefault();
        if (last != null && fight.Time - last.Time > SessionGap && _store.SessionStart <= last.Time)
        {
            _store.SessionStart = fight.Time.AddSeconds(-fight.DurationSeconds);
            _store.SessionPausedAt = null;
            _store.SessionPausedSeconds = 0;
        }
        // Combat pendant une pause : le joueur est revenu, la session reprend au début du combat
        else ResumeSession(fight.Time.AddSeconds(-fight.DurationSeconds));
        _store.Fights.Add(fight);
        if (_store.Fights.Count > MaxFights) _store.Fights.RemoveRange(0, _store.Fights.Count - MaxFights);
        Save();
        Log?.Invoke($"Combat terminé ({c.Label}) : {fight.Xp:N0} XP, {fight.Kamas:N0} kamas, {fight.Drops.Sum(d => d.Quantity)} objet(s).");
        Changed?.Invoke();
    }

    // --- Économe : chaque sort ne doit être lancé qu'une fois par combat ---

    /// <summary>Sorts connus du personnage affiché (vide s'ils n'ont jamais été reçus).</summary>
    public IReadOnlyList<int> SpellsOf(HuntCharacter c) =>
        c.KnownSpells ?? (c.Name.Length > 0 && _store.Spells.TryGetValue(c.Name, out var s) ? s : c.UsedSpells.Distinct().ToList());

    /// <summary>Garde les sorts du personnage d'une fois sur l'autre (dès que son nom est connu).</summary>
    void SaveSpells(HuntCharacter c)
    {
        if (c.KnownSpells == null || c.Name.Length == 0) return;
        _store.Spells[c.Name] = c.KnownSpells;
        Save();
    }

    /// <summary>Sorts du personnage : "sort~niveau~position;…" (envoyé à la connexion et quand un sort change).</summary>
    public void OnSpellList(string body)
    {
        var c = Current;
        var spells = new List<(int Id, int Position)>();
        foreach (var e in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = e.Split('~');
            if (f.Length >= 1 && int.TryParse(f[0], out var id))
                spells.Add((id, f.Length >= 3 && int.TryParse(f[2], out var pos) ? pos : 99));
        }
        if (spells.Count == 0) return;
        // Ordre de la barre de sorts, puis ceux qui n'y sont pas
        c.KnownSpells = spells.OrderBy(s => s.Position).Select(s => s.Id).ToList();
        SaveSpells(c);
        Changed?.Invoke();
    }

    public void OnFightJoined()
    {
        var c = Current;
        c.InFight = true;
        c.UsedSpells.Clear();
        c.Challenges.Clear(); // annoncés ensuite par le serveur (Gd)
        Changed?.Invoke();
        Notice?.Invoke(c, GameNotice.Fight, null);
    }

    /// <summary>Demande d'échange : notifiée si elle est adressée au personnage de cette fenêtre (ou s'il est encore inconnu).</summary>
    public void OnExchangeRequested(long target)
    {
        var c = Current;
        if (c.Id == 0 || c.Id == target) Notice?.Invoke(c, GameNotice.Exchange, null);
    }

    /// <summary>
    /// Invitation de groupe, vue sur la fenêtre de l'invité (et sur celle de l'invitant si c'est un de vos comptes) : le nom
    /// de l'invité sert seulement à trouver sa fenêtre ; il n'est pas conservé.
    /// </summary>
    public void OnPartyInvited(string invited) => Notice?.Invoke(Current, GameNotice.Party, invited);

    /// <summary>Économe : l'arme (corps-à-corps) compte comme un sort, sous l'identifiant 0 (sort « arme » du fichier de langue).</summary>
    public const int Weapon = 0;

    /// <summary>Dernière action demandée par chaque fenêtre (clé : connexion serveur -> client), en attente de la réponse du serveur.</summary>
    readonly Dictionary<string, (int Spell, DateTime Time)> _ownActions = new();

    /// <summary>"ip:port > ip:port" -> sens inverse (même fenêtre de jeu, autre direction).</summary>
    static string Reverse(string stream) => stream.Split(" > ") is [var a, var b] ? $"{b} > {a}" : stream;

    /// <summary>
    /// Action de combat. Reçue du serveur : "id;type;acteur;paramètres", type 300 = sort lancé, 302 = échec critique
    /// (le sort est quand même utilisé), le premier paramètre étant le sort ; 303 = attaque à l'arme. Seules les actions
    /// du personnage sont retenues. Envoyée par le client : "300{sort};{case}" ou "303{case}" : c'est le tour du
    /// personnage de cette fenêtre, et la réponse du serveur donne son identifiant (appli lancée après la connexion).
    /// </summary>
    public void OnGameAction(string body)
    {
        var f = body.Split(';');
        if (f.Length <= 2)
        {
            if (f[0].StartsWith("300") && int.TryParse(f[0].AsSpan(3), out var asked)) OnOwnAction(asked);
            else if (f.Length == 1 && f[0].StartsWith("303")) OnOwnAction(Weapon);
            return;
        }
        if (f.Length < 4 || !long.TryParse(f[2], out var actor)) return;
        int spell;
        if (f[1] == "303") spell = Weapon;
        else if (f[1] is not ("300" or "302") || !int.TryParse(f[3].Split(',')[0], out spell)) return;
        var c = Current;
        // Réponse à l'action demandée par cette fenêtre : l'acteur est son personnage
        if (_ownActions.TryGetValue(Stream, out var own) && own.Spell == spell && DateTime.Now - own.Time < TimeSpan.FromSeconds(10))
        {
            _ownActions.Remove(Stream);
            if (c.Id == 0) { c.Id = actor; IdentifyById(c); }
        }
        // Personnage inconnu (aucune action demandée encore) : on garde les sorts qu'il connaît, faute de mieux
        bool mine = c.Id != 0 ? actor == c.Id : spell != Weapon && SpellsOf(c).Contains(spell);
        if (!mine) return;
        c.InFight = true;
        c.UsedSpells.Add(spell);
        Changed?.Invoke();
    }

    /// <summary>
    /// Début de tour : si c'est le personnage de cette fenêtre qui joue, il devient le personnage affiché
    /// (multi-compte : l'onglet Combat suit le personnage dont c'est le tour, même s'il était choisi à la main).
    /// </summary>
    /// <summary>
    /// Régénération hors combat (ILS) : un point de vie toutes les N millisecondes (plus rapide assis). La vie estimée
    /// repart de maintenant avec ce rythme.
    /// </summary>
    public void OnLifeRegen(int intervalMs)
    {
        if (intervalMs is < 100 or > 60_000) return;
        var c = Current;
        if (c.CurrentLife is { } now) c.Life = now;
        c.LifeAt = DateTime.Now;
        c.RegenMs = intervalMs;
        Remember(c);
    }

    /// <summary>Début d'un tour de jeu (GTS) : combattant et durée du tour en millisecondes (0 si absente).</summary>
    public void OnTurnStarted(long fighter, int durationMs)
    {
        var c = Current;
        _turns[Stream] = (fighter, DateTime.Now, durationMs);
        // Tour d'un autre combattant : celui du personnage est fini
        if (c.Id == 0 || c.Id != fighter)
        {
            if (c.TurnStart != null) { c.TurnStart = null; Changed?.Invoke(); }
            return;
        }
        c.TurnStart = DateTime.Now;
        c.TurnMs = durationMs;
        c.InFight = true;
        c.LastMove = DateTime.Now;
        if (Pinned != null) Pinned = c.Stream;
        Changed?.Invoke();
        Notice?.Invoke(c, GameNotice.Turn, null);
    }

    /// <summary>
    /// Le client de cette fenêtre lance un sort ou attaque : c'est son tour, son personnage devient le personnage affiché.
    /// Le message est reçu sur la connexion client -> serveur ; le personnage est suivi sur la connexion inverse.
    /// </summary>
    void OnOwnAction(int spell)
    {
        var stream = Reverse(Stream);
        _ownActions[stream] = (spell, DateTime.Now);
        if (!_characters.TryGetValue(stream, out var c)) return;
        LearnFromTurn(c);
        c.InFight = true;
        c.LastMove = DateTime.Now;
        if (Pinned != null) Pinned = stream;
        Changed?.Invoke();
    }

    /// <summary>Dernier début de tour vu sur chaque connexion : combattant, heure, durée (ms).</summary>
    readonly Dictionary<string, (long Fighter, DateTime Time, int Ms)> _turns = new();

    /// <summary>Le client de cette fenêtre se déplace ou passe son tour : c'est le tour de son personnage.</summary>
    public void OnOwnTurnAction()
    {
        var stream = Reverse(Stream);
        if (!_characters.TryGetValue(stream, out var c)) return;
        LearnFromTurn(c);
    }

    /// <summary>
    /// Seul le combattant dont c'est le tour peut agir : si cette fenêtre agit pendant un tour, son personnage est ce
    /// combattant. Application relancée pendant le jeu : son identifiant est ainsi appris sans relancer la fenêtre, et le
    /// temps de son tour s'affiche aussitôt.
    /// </summary>
    void LearnFromTurn(HuntCharacter c)
    {
        if (c.Id != 0 || !_turns.TryGetValue(c.Stream, out var turn)) return;
        if (DateTime.Now - turn.Time > TimeSpan.FromMilliseconds(Math.Max(turn.Ms, 30_000) + 2000)) return;
        c.Id = turn.Fighter;
        c.InFight = true;
        c.TurnStart = turn.Time;
        c.TurnMs = turn.Ms;
        Log?.Invoke($"Personnage de la {c.Label} reconnu à son tour de jeu.");
        IdentifyById(c);
        Remember(c);
        Changed?.Invoke();
    }

    public void ResetEconome()
    {
        Shown?.UsedSpells.Clear();
        Changed?.Invoke();
    }

    /// <summary>Forme d'un message de fin de combat pour le journal, sans son contenu (il contient des noms de joueurs) : nombre de champs par segment.</summary>
    static string Shape(string[] parts) => $"{parts.Length} segments, champs : " + string.Join(" ", parts.Select(p => p.Split(';').Length));

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
