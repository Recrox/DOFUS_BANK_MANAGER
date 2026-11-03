namespace DofusBankManager.Data;

/// <summary>Rune obtenue au brisage : objet (pour son prix HDV), nom et quantité estimée.</summary>
public sealed record RuneYield(int ItemId, string Name, double Quantity);

/// <summary>
/// Estimation des runes obtenues en brisant un objet. Aucune formule officielle n'existe pour Retro : on applique
/// la formule communautaire, pour chaque caractéristique positive :
///   poids de runes = jet × poids par point × niveau de l'objet × coefficient / 10 000
/// puis ce poids est réparti en runes, de la plus grosse (Ra) à la plus petite (rune simple, quantité décimale).
/// Le poids d'une rune est sa valeur × poids par point, arrondi à l'entier supérieur (Ra Vi : 30 × 0,25 → 8).
/// </summary>
public static class RuneBreaking
{
    /// <summary>Coefficient de brisage, en % : la Coiffe du Kitsou (niveau 61, 150 de vitalité) donne 1 Ra Vi.</summary>
    const double Coefficient = 36;

    sealed record Line(double PointWeight, (string Rune, int Value)[] Runes);

    /// <summary>Caractéristique (voir ItemStats) -> poids par point et runes, de la plus petite à la plus grosse.</summary>
    static readonly Dictionary<int, Line> Table = new()
    {
        [11] = new(0.25, [("Rune Vi", 3), ("Rune Pa Vi", 10), ("Rune Ra Vi", 30)]),
        [12] = new(3, [("Rune Sa", 1), ("Rune Pa Sa", 3), ("Rune Ra Sa", 10)]),
        [10] = new(1, [("Rune Fo", 1), ("Rune Pa Fo", 3), ("Rune Ra Fo", 10)]),
        [15] = new(1, [("Rune Ine", 1), ("Rune Pa Ine", 3), ("Rune Ra Ine", 10)]),
        [13] = new(1, [("Rune Cha", 1), ("Rune Pa Cha", 3), ("Rune Ra Cha", 10)]),
        [14] = new(1, [("Rune Age", 1), ("Rune Pa Age", 3), ("Rune Ra Age", 10)]),
        [1] = new(100, [("Rune Ga Pa", 1)]),
        [23] = new(90, [("Rune Ga Pme", 1)]),
        [19] = new(51, [("Rune Po", 1)]),
        [26] = new(30, [("Rune Invo", 1)]),
        [18] = new(30, [("Rune Cri", 1)]),
        [17] = new(2, [("Rune Do Per", 1), ("Rune Pa Do Per", 3), ("Rune Ra Do Per", 10)]),
        [80] = new(20, [("Rune Do", 1)]),
        [49] = new(20, [("Rune So", 1)]),
        [44] = new(0.1, [("Rune Ini", 10), ("Rune Pa Ini", 30), ("Rune Ra Ini", 100)]),
        [48] = new(3, [("Rune Prospe", 1), ("Rune Pa Prospe", 3)]),
        [70] = new(15, [("Rune Pi", 1), ("Rune Pa Pi", 3)]),
        [69] = new(2, [("Rune Pi Per", 1), ("Rune Pa Pi Per", 3), ("Rune Ra Pi Per", 10)]),
        [50] = new(30, [("Rune Do Ren", 1)]),
        [33] = new(4, [("Rune Ré Per Terre", 1)]),
        [34] = new(4, [("Rune Ré Per Feu", 1)]),
        [35] = new(4, [("Rune Ré Per Eau", 1)]),
        [36] = new(4, [("Rune Ré Per Air", 1)]),
        [37] = new(4, [("Rune Ré Per Neutre", 1)]),
        [83] = new(5, [("Rune Ré Terre", 1)]),
        [84] = new(5, [("Rune Ré Feu", 1)]),
        [85] = new(5, [("Rune Ré Eau", 1)]),
        [86] = new(5, [("Rune Ré Air", 1)]),
        [87] = new(5, [("Rune Ré Neutre", 1)]),
        [96] = new(0.25, [("Rune Pod", 10), ("Rune Pa Pod", 30), ("Rune Ra Pod", 100)]),
    };

    /// <summary>Runes de forgemagie (type 78) par nom, construit une fois par chargement des données.</summary>
    static (GameData Data, Dictionary<string, int> Ids)? _runeIds;

    static Dictionary<string, int> RuneIds(GameData data)
    {
        if (_runeIds is not { } cache || cache.Data != data)
            _runeIds = cache = (data, data.Items.Values.Where(i => i.TypeId == 78)
                .GroupBy(i => i.Name).ToDictionary(g => g.Key, g => g.First().Id));
        return cache.Ids;
    }

    /// <summary>Identifiant d'une rune de forgemagie d'après son nom (ex. « Rune Vi »), null si absente des données.</summary>
    public static int? RuneId(GameData data, string name) => RuneIds(data).TryGetValue(name, out var id) ? id : null;

    /// <summary>Runes estimées pour un objet de ce niveau et ces caractéristiques (les malus ne donnent rien).</summary>
    public static List<RuneYield> Estimate(GameData data, int level, IReadOnlyDictionary<int, int> stats)
    {
        var result = new List<RuneYield>();
        foreach (var (characteristic, value) in stats.OrderBy(kv => ItemStats.Order(kv.Key)))
        {
            if (value <= 0 || !Table.TryGetValue(characteristic, out var line)) continue;
            double weight = value * line.PointWeight * level * Coefficient / 10_000;

            // De la plus grosse rune à la plus petite ; le reste va en runes simples (quantité décimale)
            for (int i = line.Runes.Length - 1; i >= 0 && weight > 0; i--)
            {
                var (rune, runeValue) = line.Runes[i];
                double runeWeight = Math.Ceiling(runeValue * line.PointWeight);
                double qty = i == 0 ? weight / runeWeight : Math.Floor(weight / runeWeight);
                if (qty <= 0) continue;
                weight -= qty * runeWeight;
                result.Add(new RuneYield(RuneIds(data).GetValueOrDefault(rune), rune, Math.Round(qty, 2)));
            }
        }
        return result;
    }
}
