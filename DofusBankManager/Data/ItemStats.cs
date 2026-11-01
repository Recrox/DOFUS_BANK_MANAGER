namespace DofusBankManager.Data;

/// <summary>
/// Caractéristique d'un objet. Characteristic : identifiant utilisé par le champ « ef » du fichier de langue
/// (et par EffectInfo.Characteristic). Effects : effets « bonus » qui la portent, tels que les renvoie
/// FmTracker.ParseStats pour un objet possédé. Main : proposée dans les filtres de l'onglet Brisage.
/// </summary>
public sealed record StatDef(int Characteristic, string Name, int[] Effects, bool Main = false);

public static class ItemStats
{
    public static readonly StatDef[] All =
    [
        new(11, "Vitalité", [125], true),
        new(12, "Sagesse", [124, 606], true),
        new(10, "Force", [118, 607], true),
        new(15, "Intelligence", [126, 611], true),
        new(13, "Chance", [123, 608], true),
        new(14, "Agilité", [119, 609], true),
        new(1, "PA", [111], true),
        new(23, "PM", [128], true),
        new(19, "Portée", [117], true),
        new(26, "Invocations", [182], true),
        new(18, "Coups critiques", [115], true),
        new(17, "% Dommages", [138], true),
        new(80, "Dommages", [112, 121], true),
        new(49, "Soins", [178], true),
        new(44, "Initiative", [174], true),
        new(48, "Prospection", [176], true),
        new(70, "Dommages aux pièges", [225]),
        new(69, "% Dommages aux pièges", [226]),
        new(76, "% Dommages finaux", [2008]),
        new(50, "Renvoi de dommages", [220]),
        new(33, "% Résistance terre", [210]),
        new(34, "% Résistance feu", [213]),
        new(35, "% Résistance eau", [211]),
        new(36, "% Résistance air", [212]),
        new(37, "% Résistance neutre", [214]),
        new(83, "Résistance terre", [240]),
        new(84, "Résistance feu", [243]),
        new(85, "Résistance eau", [241]),
        new(86, "Résistance air", [242]),
        new(87, "Résistance neutre", [244]),
        new(27, "% Esquive PA", [160]),
        new(28, "% Esquive PM", [161]),
        new(96, "Pods", [158]),
    ];

    static readonly Dictionary<int, StatDef> ByCharacteristic = All.ToDictionary(s => s.Characteristic);

    public static string Name(int characteristic) =>
        ByCharacteristic.TryGetValue(characteristic, out var s) ? s.Name : $"Caractéristique {characteristic}";

    /// <summary>Rang d'affichage (ordre de la table ci-dessus, inconnues à la fin).</summary>
    public static int Order(int characteristic) =>
        ByCharacteristic.TryGetValue(characteristic, out var s) ? Array.IndexOf(All, s) : int.MaxValue;

    /// <summary>Ex. « +200 Vitalité », « -10 Force ».</summary>
    public static string Format(int characteristic, int value) => $"{value:+0;-0} {Name(characteristic)}";

    /// <summary>
    /// Caractéristiques réelles d'un objet possédé, à partir des stats déjà regroupées par FmTracker.ParseStats
    /// (effet bonus -> valeur, malus déduits).
    /// </summary>
    public static Dictionary<int, int> FromParsed(IReadOnlyDictionary<int, int> parsed)
    {
        var result = new Dictionary<int, int>();
        foreach (var s in All)
        {
            int v = s.Effects.Sum(e => parsed.GetValueOrDefault(e));
            if (v != 0) result[s.Characteristic] = v;
        }
        return result;
    }
}
