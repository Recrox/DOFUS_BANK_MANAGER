using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hunt;

/// <summary>
/// Onglet Chasse : groupes de monstres de la map, archi-monstres aperçus, caractéristiques utiles (sagesse, prospection,
/// prospection du groupe) et résultats de combat. Tout est lu passivement (REGLES.md, règles 1 et 3) ; des messages lus,
/// seuls les monstres, les niveaux, la prospection et la ligne du personnage sont gardés : les noms des autres joueurs
/// ne sont jamais conservés (règle 4).
/// </summary>
public sealed partial class HuntTracker
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
    /// <summary>Fin du tour de jeu d'un de vos personnages (début du tour d'un autre combattant, ou fin du combat).</summary>
    public event Action<HuntCharacter>? TurnEnded;
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
