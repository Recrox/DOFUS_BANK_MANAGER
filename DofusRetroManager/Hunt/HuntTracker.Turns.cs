using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hunt;

public sealed partial class HuntTracker
{
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
            {
                spells.Add((id, f.Length >= 3 && int.TryParse(f[2], out var pos) ? pos : 99));
                if (f.Length >= 2 && int.TryParse(f[1], out var level)) c.SpellLevels[id] = level;
            }
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
        c.TurnSpells.Clear();
        c.VersatileBroken = false;
        c.FightLifeLost = 0;
        c.SylvestreTurns = 0;
        c.SylvestrePending = null;
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
        if (c.TurnSpells.Contains(spell)) c.VersatileBroken = true; // Versatile : même action deux fois dans le tour
        c.TurnSpells.Add(spell);
        // Puissance Sylvestre réussie (un échec critique, 302, n'a pas d'effet)
        if (f[1] == "300" && IsSylvestre(spell)) c.SylvestrePending = SylvestreDuration(c, spell);
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
            if (c.TurnStart != null)
            {
                c.TurnStart = null;
                EndTurn(c);
                Changed?.Invoke();
                TurnEnded?.Invoke(c);
            }
            return;
        }
        c.TurnStart = DateTime.Now;
        c.TurnMs = durationMs;
        c.InFight = true;
        c.TurnSpells.Clear(); // Versatile : nouveau tour, toutes les actions redeviennent possibles
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

    /// <summary>Puissance Sylvestre (Sadida) : sort 197 et ses variantes, reconnus par leur nom dans le fichier de langue.</summary>
    HashSet<int>? _sylvestre;
    bool IsSylvestre(int spell) => (_sylvestre ??= _data.Spells.Where(kv => kv.Value.Trim().Equals("Puissance Sylvestre", StringComparison.OrdinalIgnoreCase))
        .Select(kv => kv.Key).Append(197).ToHashSet()).Contains(spell);

    /// <summary>Durée du sort au niveau du personnage (fichier de langue : 2, 3 ou 4 tours) ; niveau inconnu : le plus haut.</summary>
    int SylvestreDuration(HuntCharacter c, int spell)
    {
        var turns = _data.SpellTurns.GetValueOrDefault(spell) ?? [2, 2, 3, 3, 4, 4];
        int level = c.SpellLevels.GetValueOrDefault(spell);
        return level is >= 1 and <= 6 && turns[level - 1] > 0 ? turns[level - 1] : turns.Max();
    }

    /// <summary>Fin d'un tour du personnage : Puissance Sylvestre lancée pendant ce tour prend effet, sinon un tour d'arbre de moins.</summary>
    static void EndTurn(HuntCharacter c)
    {
        if (c.SylvestrePending is { } n) { c.SylvestreTurns = n - 1; c.SylvestrePending = null; }
        else if (c.SylvestreTurns > 0) c.SylvestreTurns--;
    }

    public void ResetEconome()
    {
        if (Shown is { } c)
        {
            c.UsedSpells.Clear();
            c.TurnSpells.Clear();
            c.VersatileBroken = false;
            c.FightLifeLost = 0;
        }
        Changed?.Invoke();
    }
}
