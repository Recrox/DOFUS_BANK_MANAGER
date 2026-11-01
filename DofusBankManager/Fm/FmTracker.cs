using System.Globalization;
using System.IO;
using System.Text.Json;
using DofusBankManager.Data;

namespace DofusBankManager.Fm;

/// <summary>Variation d'une stat. Stat : identifiant de l'effet « bonus » de la stat (ex. 118 = Force).</summary>
public sealed record FmStatChange(int Stat, int Delta);

public sealed class FmEvent
{
    public DateTime Time { get; set; }
    public int? RuneTemplateId { get; set; }
    public string Outcome { get; set; } = "";
    public List<FmStatChange> Changes { get; set; } = new();
    public double WeightGained { get; set; }
    public double WeightLost { get; set; }
    public double ReliquatBefore { get; set; }
    public double ReliquatAfter { get; set; }
}

public sealed class FmItem
{
    public long Uid { get; set; }
    public int TemplateId { get; set; }
    public string Effects { get; set; } = "";
    public double Reliquat { get; set; }
    public DateTime LastUsed { get; set; }
    public List<FmEvent> History { get; set; } = new();
}

/// <summary>
/// Suivi de la forgemagie : à chaque rune passée sur un objet (changement de ses caractéristiques
/// pendant un atelier), calcule le poids gagné/perdu, classe le résultat et estime le reliquat.
///
/// Modèle de reliquat (estimation, corrigeable à la main) :
///   succès critique (gain sans perte)  : si reliquat ≥ poids gagné, il est consommé d'autant
///   succès neutre (gain et perte)      : reliquat += poids perdu − poids gagné (minimum 0)
///   échec (perte sans gain)            : reliquat += poids perdu
///   échec sans effet                   : inchangé
/// </summary>
public sealed class FmTracker
{
    public const string SuccesCritique = "Succès critique", SuccesNeutre = "Succès neutre",
        Echec = "Échec", EchecSansEffet = "Échec sans effet";

    const int RuneTypeId = 78;
    /// <summary>Écart maximal entre la consommation d'une rune et la nouvelle version de l'objet.</summary>
    static readonly TimeSpan RuneWindow = TimeSpan.FromSeconds(3);

    readonly GameData _data;
    readonly Dictionary<int, double> _weights;
    /// <summary>Effet -> stat (effet bonus de référence). Un malus pointe vers son bonus (-Force -> +Force).</summary>
    readonly Dictionary<int, int> _statOfEffect = new();
    /// <summary>Stat -> effet malus correspondant, pour afficher « -3 en force ».</summary>
    readonly Dictionary<int, int> _malusOfStat = new();

    /// <summary>Runes consommées pas encore rattachées à un passage (plusieurs en cas de « Fusionner tout »).</summary>
    readonly Queue<(int Template, DateTime Time)> _pendingRunes = new();

    public Dictionary<long, FmItem> Items { get; private set; } = new();
    public long? CurrentUid { get; private set; }

    public event Action? Changed;
    public event Action<string>? Log;

    static string FmPath => Path.Combine(GameData.AppDir, "fm.json");
    public static string WeightsPath => Path.Combine(GameData.AppDir, "fm_poids.json");

    public FmTracker(GameData data)
    {
        _data = data;
        _weights = LoadWeights();

        // Un bonus et son malus partagent la caractéristique ET le libellé (« +#1 en force » / « -#1 en force »).
        // La caractéristique seule ne suffit pas : Vitalité et « Vole PDV » ont la même.
        foreach (var group in data.Effects.Values.GroupBy(e => (e.Characteristic, Suffix(e.Description))))
        {
            var bonus = group.Where(e => !e.IsMalus).OrderByDescending(e => _weights.ContainsKey(e.Id)).ThenBy(e => e.Id).FirstOrDefault();
            var malus = group.Where(e => e.IsMalus).OrderBy(e => e.Id).FirstOrDefault();
            int stat = bonus?.Id ?? malus!.Id;
            foreach (var e in group) _statOfEffect[e.Id] = e.Characteristic > 0 ? stat : e.Id;
            if (malus != null && bonus != null && group.Key.Characteristic > 0) _malusOfStat[stat] = malus.Id;
        }
        Load();
    }

    /// <summary>Libellé sans la valeur : « +#1{~1~2 à }#2 en force » -> « en force ».</summary>
    static string Suffix(string description) =>
        System.Text.RegularExpressions.Regex.Replace(description, @"^[+-]?#1\{[^}]*\}#2\s*", "").Trim();

    // --- Évènements venant du réseau (DofusTracker.CraftItemSeen / CraftItemConsumed / CraftOpened) ---

    /// <summary>Objet en cours de modification dans l'atelier : modèle, uid et caractéristiques connues.</summary>
    (int Template, long Uid, string Effects)? _work;

    /// <summary>Résultat annoncé par le jeu (message 117 ou 194) en attente de la nouvelle version de l'objet.</summary>
    (string Outcome, DateTime Time)? _pendingOutcome;
    /// <summary>Dernier passage classé d'après les stats seules : un message du jeu peut encore le corriger.</summary>
    FmEvent? _awaitingMessage;

    public void OnCraftOpened()
    {
        _work = null;
        _pendingOutcome = null;
        _awaitingMessage = null;
    }

    /// <summary>
    /// Objet vu dans l'atelier. Retro recrée l'objet (nouvel uid) à chaque rune qui modifie ses stats :
    /// succès critique, succès neutre ou échec avec perte. Chaque nouvelle version de l'objet en cours
    /// est donc un passage, comparé à la version précédente. L'échec sans effet ne recrée pas l'objet :
    /// il est détecté par son message (183, voir OnCraftInfo).
    /// </summary>
    public void OnCraftItemSeen(long uid, int templateId, string effects)
    {
        var it = _data.GetItem(templateId);
        if (it.TypeId == RuneTypeId || ParseStats(effects).Count == 0) return;

        if (_work is not { } work || work.Template != templateId)
        {
            _work = (templateId, uid, effects); // nouvel objet posé : état de départ
            return;
        }
        _work = (templateId, uid, effects);

        var changes = Diff(work.Effects, effects);
        if (changes.Count == 0)
        {
            // Même version re-signalée (OAKO puis EMK…), ou objet renvoyé sous un nouvel uid sans changement
            // de stats (échec sans effet) : pas de passage, il est compté par son message (183, voir OnCraftInfo).
            // La fiche suit simplement le nouvel uid.
            if (work.Uid != uid && Items.Remove(work.Uid, out var moved))
            {
                moved.Uid = uid;
                Items[uid] = moved;
                if (CurrentUid == work.Uid) CurrentUid = uid;
                Save();
            }
            return;
        }

        // La fiche de l'objet suit sa nouvelle version
        if (!Items.Remove(work.Uid, out var item)) item = Items.GetValueOrDefault(uid) ?? new FmItem { TemplateId = templateId };
        item.Uid = uid;
        Items[uid] = item;
        item.Effects = effects;
        item.LastUsed = DateTime.Now;
        CurrentUid = uid;

        var ev = new FmEvent
        {
            Time = DateTime.Now, Changes = changes,
            WeightGained = changes.Where(c => c.Delta > 0).Sum(c => c.Delta * Weight(c.Stat)),
            WeightLost = changes.Where(c => c.Delta < 0).Sum(c => -c.Delta * Weight(c.Stat)),
            RuneTemplateId = DequeueRune(),
        };

        if (_pendingOutcome is { } p && DateTime.Now - p.Time < RuneWindow)
        {
            ev.Outcome = p.Outcome; // le jeu a déjà annoncé le résultat
            _pendingOutcome = null;
            _awaitingMessage = null;
        }
        else
        {
            // Sans message : succès critique si l'objet gagne, sinon succès neutre (gain et perte
            // sur la même stat peuvent s'annuler) ; un message 117/194 qui suit corrigera.
            bool hasLoss = changes.Any(c => c.Delta < 0), hasGain = changes.Any(c => c.Delta > 0);
            ev.Outcome = hasGain && !hasLoss ? SuccesCritique : hasGain ? SuccesNeutre : hasLoss ? Echec : SuccesNeutre;
            _awaitingMessage = ev;
        }
        AddEvent(item, ev);
    }

    /// <summary>Message de résultat du jeu : 117 = échec avec perte, 183 = échec sans effet, 194 = succès neutre.</summary>
    public void OnCraftInfo(int code)
    {
        if (code == 183)
        {
            // L'objet n'est pas recréé : le passage n'est connu que par ce message
            if (WorkItem() is not { } item) return;
            AddEvent(item, new FmEvent { Time = DateTime.Now, Outcome = EchecSansEffet, RuneTemplateId = DequeueRune() });
            return;
        }

        string? outcome = code switch { 117 => Echec, 194 => SuccesNeutre, _ => null };
        if (outcome == null) return;

        // Message après la nouvelle version de l'objet : on corrige le passage déjà enregistré
        if (_awaitingMessage is { } ev && DateTime.Now - ev.Time < RuneWindow
            && CurrentUid is { } uid && Items.TryGetValue(uid, out var current) && current.History.LastOrDefault() == ev)
        {
            _awaitingMessage = null;
            ev.Outcome = outcome;
            current.Reliquat = ev.ReliquatBefore;
            ApplyReliquat(current, ev);
            Log?.Invoke($"FM : résultat confirmé par le jeu ({outcome}), reliquat {current.Reliquat:0.##}.");
            Save();
            Changed?.Invoke();
            return;
        }
        _pendingOutcome = (outcome, DateTime.Now); // message avant la nouvelle version de l'objet
    }

    /// <summary>Fiche de l'objet posé dans l'atelier, créée au besoin.</summary>
    FmItem? WorkItem()
    {
        if (_work is not { } work) return null;
        if (!Items.TryGetValue(work.Uid, out var item))
            Items[work.Uid] = item = new FmItem { Uid = work.Uid, TemplateId = work.Template, Effects = work.Effects };
        item.LastUsed = DateTime.Now;
        CurrentUid = work.Uid;
        return item;
    }

    void AddEvent(FmItem item, FmEvent ev)
    {
        ev.ReliquatBefore = item.Reliquat;
        ApplyReliquat(item, ev);
        item.History.Add(ev);
        Log?.Invoke($"FM {_data.GetItem(item.TemplateId).Name} : {ev.Outcome}, +{ev.WeightGained:0.##} / -{ev.WeightLost:0.##} poids, reliquat {item.Reliquat:0.##}.");
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Met à jour le reliquat selon le résultat. On raisonne sur la variation nette de poids
    /// (gain − perte), qui reste juste quand le gain et la perte touchent la même stat.
    /// </summary>
    static void ApplyReliquat(FmItem item, FmEvent ev)
    {
        double before = ev.ReliquatBefore, net = ev.WeightGained - ev.WeightLost;
        item.Reliquat = ev.Outcome switch
        {
            SuccesCritique => net > 0 && before >= net ? before - net : before,
            SuccesNeutre => Math.Max(0, before - net),
            Echec => before + Math.Max(0, -net),
            _ => before,
        };
        ev.ReliquatAfter = item.Reliquat;
    }

    public void OnItemConsumed(long uid, int templateId, int count)
    {
        if (_data.GetItem(templateId).TypeId != RuneTypeId) return;

        for (int i = 0; i < Math.Min(count, 100); i++)
        {
            // La rune peut être signalée juste après la nouvelle version de l'objet :
            // on la rattache au plus ancien passage récent qui n'a pas encore sa rune
            var waiting = CurrentUid is { } cur && Items.TryGetValue(cur, out var item)
                ? item.History.Where(e => e.RuneTemplateId == null && DateTime.Now - e.Time < RuneWindow).FirstOrDefault()
                : null;
            if (waiting != null) waiting.RuneTemplateId = templateId;
            else _pendingRunes.Enqueue((templateId, DateTime.Now));
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>Rune consommée autour de ce passage (par défaut : maintenant), s'il y en a une.</summary>
    int? DequeueRune(DateTime? at = null)
    {
        var time = at ?? DateTime.Now;
        DropStaleRunes(time);
        return _pendingRunes.TryPeek(out var r) && (r.Time - time).Duration() <= RuneWindow
            ? _pendingRunes.Dequeue().Template : null;
    }

    void DropStaleRunes(DateTime now)
    {
        while (_pendingRunes.TryPeek(out var r) && now - r.Time > RuneWindow) _pendingRunes.Dequeue();
    }

    /// <summary>
    /// Appelé régulièrement : oublie les runes qui n'ont été suivies d'aucun passage, et enregistre
    /// un résultat annoncé par le jeu dont la nouvelle version de l'objet n'a pas été vue.
    /// </summary>
    public void Tick()
    {
        // D'abord le résultat en attente : sa rune a été consommée au moment du message, il y a plus de
        // RuneWindow, et serait sinon oubliée juste avant
        if (_pendingOutcome is { } p && DateTime.Now - p.Time > RuneWindow)
        {
            _pendingOutcome = null;
            if (WorkItem() is { } item)
                AddEvent(item, new FmEvent { Time = p.Time, Outcome = p.Outcome, RuneTemplateId = DequeueRune(p.Time) });
        }
        DropStaleRunes(DateTime.Now);
    }

    // --- Actions de l'utilisateur ---

    public void Select(long uid) { if (Items.ContainsKey(uid)) CurrentUid = uid; }

    public void SetReliquat(long uid, double value)
    {
        if (!Items.TryGetValue(uid, out var item)) return;
        item.Reliquat = Math.Max(0, value);
        Save();
        Changed?.Invoke();
    }

    public void ClearHistory(long uid)
    {
        if (!Items.TryGetValue(uid, out var item)) return;
        item.History.Clear();
        item.Reliquat = 0;
        Save();
        Changed?.Invoke();
    }

    public void Forget(long uid)
    {
        if (!Items.Remove(uid)) return;
        if (CurrentUid == uid) CurrentUid = null;
        Save();
        Changed?.Invoke();
    }

    // --- Caractéristiques ---

    public double Weight(int stat) => _weights.GetValueOrDefault(stat);

    /// <summary>Valeur signée par stat (les malus comptent en négatif).</summary>
    public Dictionary<int, int> ParseStats(string effects)
    {
        var stats = new Dictionary<int, int>();
        foreach (var part in effects.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split('#');
            if (f.Length < 2 || !int.TryParse(f[0], NumberStyles.HexNumber, null, out var id)
                || !int.TryParse(f[1], NumberStyles.HexNumber, null, out var value)
                || !_data.Effects.TryGetValue(id, out var e))
                continue;
            int stat = _statOfEffect.GetValueOrDefault(id, id);
            bool negative = e.IsMalus && stat != id; // malus rattaché à son bonus
            stats[stat] = stats.GetValueOrDefault(stat) + (negative ? -value : value);
        }
        return stats;
    }

    List<FmStatChange> Diff(string before, string after)
    {
        var a = ParseStats(before);
        var b = ParseStats(after);
        return a.Keys.Union(b.Keys)
            .Select(k => new FmStatChange(k, b.GetValueOrDefault(k) - a.GetValueOrDefault(k)))
            .Where(c => c.Delta != 0)
            .OrderByDescending(c => c.Delta)
            .ToList();
    }

    /// <summary>Libellé d'une stat avec sa valeur signée, ex. « +12 en force » ou « -3 en sagesse ».</summary>
    public string Describe(int stat, int value) =>
        value < 0 && _malusOfStat.TryGetValue(stat, out var malus)
            ? _data.FormatEffect(malus, -value)
            : _data.FormatEffect(stat, value);

    // --- Poids des caractéristiques (modifiables dans fm_poids.json) ---

    /// <summary>Poids par point, indexés par effet « bonus ». Valeurs communautaires, à ajuster si besoin.</summary>
    static readonly Dictionary<int, double> DefaultWeights = new()
    {
        [111] = 100,  // PA
        [128] = 90,   // PM
        [117] = 51,   // Portée
        [182] = 30,   // Invocations
        [118] = 1,    // Force
        [119] = 1,    // Agilité
        [123] = 1,    // Chance
        [126] = 1,    // Intelligence
        [125] = 0.25, // Vitalité
        [124] = 3,    // Sagesse
        [174] = 0.1,  // Initiative
        [176] = 3,    // Prospection
        [158] = 0.25, // Pods
        [112] = 20,   // Dommages
        [138] = 2,    // % dommages
        [178] = 20,   // Soins
        [115] = 30,   // Coups critiques
        [225] = 15,   // Dommages aux pièges
        [226] = 2,    // % dommages aux pièges
        [220] = 10,   // Renvoi de dommages
        [210] = 6, [211] = 6, [212] = 6, [213] = 6, [214] = 6, // % résistances
        [240] = 2, [241] = 2, [242] = 2, [243] = 2, [244] = 2, // résistances fixes
    };

    static Dictionary<int, double> LoadWeights()
    {
        try
        {
            if (File.Exists(WeightsPath))
                return JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(WeightsPath))!
                    .Where(kv => int.TryParse(kv.Key, out _))
                    .ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(WeightsPath, JsonSerializer.Serialize(
                DefaultWeights.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
        return DefaultWeights;
    }

    // --- Persistance ---

    void Load()
    {
        try
        {
            if (File.Exists(FmPath))
                Items = JsonSerializer.Deserialize<Dictionary<long, FmItem>>(File.ReadAllText(FmPath)) ?? new();
        }
        catch { Items = new(); }
        CurrentUid = Items.Values.OrderByDescending(i => i.LastUsed).FirstOrDefault()?.Uid;
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(FmPath, JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log?.Invoke("Sauvegarde FM impossible : " + ex.Message); }
    }
}
