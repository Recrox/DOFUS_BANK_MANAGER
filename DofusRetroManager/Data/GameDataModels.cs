using System.IO;
using System.Net.Http;
using System.Xml.Linq;

namespace DofusRetroManager.Data;

public sealed record ItemInfo(int Id, string Name, int Level, int TypeId, string TypeName, int Gfx)
{
    /// <summary>Caractéristique -> valeur maximale de l'objet (champ « ef » du fichier de langue). Voir ItemStats.</summary>
    public IReadOnlyDictionary<int, int> MaxStats { get; init; } = NoStats;

    static readonly Dictionary<int, int> NoStats = new();
}

public sealed record Ingredient(int ItemId, int Quantity);

/// <summary>
/// Monstre (fichier de langue « monsters ») : sert à nommer le contenu des pierres d'âme. Grade3Level : niveau du
/// grade 3 (le plus haut s'il en a moins), pris pour un boss affiché sur sa map sans niveau (-1).
/// </summary>
public sealed record MonsterInfo(int Id, string Name, int RaceId, string Race, int LevelMin, int LevelMax, int Grade3Level)
{
    /// <summary>Illustration du monstre : clips/artworks/big/&lt;Gfx&gt;.swf du client installé (champ « g » du fichier de langue).</summary>
    public int Gfx { get; init; }
    /// <summary>Niveaux des grades g1…g5, dans l'ordre.</summary>
    public IReadOnlyList<int> Grades { get; init; } = [];

    /// <summary>Race « Archi-monstres » du fichier de langue (MR[78]).</summary>
    public bool IsArchi => RaceId == 78;

    /// <summary>Boss (champ « d » du fichier de langue : gardien de donjon, ex. Bouftou Royal, Moon).</summary>
    public bool IsBoss { get; init; }

    /// <summary>Grade (0 à 4) d'un monstre de ce niveau : celui du même niveau, sinon le plus proche.</summary>
    public int GradeOf(int level) =>
        Grades.Count == 0 ? 0 : Grades.Select((l, i) => (l, i)).MinBy(x => Math.Abs(x.l - level)).i;
}

/// <summary>
/// Drop officiel d'un monstre (fichier de langue « monsterdrops », MD[monstre][objet]) :
/// Rates = taux en % par grade (t), MinProspection = prospection de groupe minimale (pp),
/// MaxPerFight = exemplaires au plus par combat (dc, 0 = sans limite), CardOdds = 1 chance sur CardOdds pour un paquet
/// de cartes (sr, 0 sinon), Conditional = drop soumis à une condition (critère attendu dans sc : trophée, jeton,
/// événement…) ou objet de quête.
/// </summary>
public sealed record MonsterDrop(int ItemId, IReadOnlyList<double> Rates, int MinProspection, int MaxPerFight,
    int CardOdds, bool Conditional);

/// <summary>Icône d'un sort, comme dans le jeu : fond (icons/back), symbole (icons/up) et leurs couleurs (champ « i » du fichier de langue).</summary>
public sealed record SpellLook(int Back, int Up, int Background, int Frame, int Print);

/// <summary>
/// Map (fichier de langue « maps ») : coordonnées et zone, ex. « [4,-18] Amakna (Village d'Amakna) ».
/// SuperArea : 0 = continent amaknien, 3 = Incarnam (zone de départ).
/// </summary>
public sealed record MapInfo(int Id, int X, int Y, string Area, string SubArea, int SuperArea = 0)
{
    public string Label => $"[{X},{Y}] {Area}" + (SubArea.Length > 0 && SubArea != Area ? $" ({SubArea})" : "");
}

/// <summary>
/// Point d'intérêt de la carte du monde (fichier de langue « hints ») : temple, HDV, atelier, zaap, donjon…
/// Gfx : icône exportée par clips/maps/hints.swf du client.
/// </summary>
public sealed record MapHint(string Name, int Category, int Gfx, MapInfo Map);

/// <summary>Récolte d'une ressource par un métier : objet obtenu (compétence du fichier de langue « skills »).</summary>
public sealed record HarvestSkill(int JobId, string Job, int ItemId);

/// <summary>
/// Ressource récoltable (objet interactif de type 1 du fichier de langue « interactiveobjects »), ex. « If » coupé par
/// le bûcheron pour du Bois d'If. Une même ressource peut se récolter par plusieurs métiers (Lin : paysan et alchimiste).
/// </summary>
/// <summary>
/// Carte du classeur TTG (fichier de langue « ttg ») : numéro (1 à 464, brillantes à partir de 10001), créature représentée,
/// famille, rareté (1 à 4), objet carte (aucun pour une brillante : elle n'existe que dans le classeur).
/// </summary>
public sealed record TtgCard(int Id, string Name, int Family, int Rarity, int ItemId, bool Shiny)
{
    /// <summary>Numéro affiché dans le livre : celui de la carte normale (« #346 »).</summary>
    public int Number => Shiny ? Id - 10000 : Id;
}

/// <summary>Famille de cartes TTG : nom et objet (paquet) qui sert d'icône.</summary>
public sealed record TtgFamily(int Id, string Name, int ItemId);

public sealed record HarvestResource(int Id, string Name, IReadOnlyList<HarvestSkill> Skills)
{
    /// <summary>Objet obtenu par le premier métier : sert d'icône.</summary>
    public int ItemId => Skills.Count > 0 ? Skills[0].ItemId : 0;
}

public sealed record Recipe(int ResultId, IReadOnlyList<Ingredient> Ingredients, IReadOnlyList<string> Jobs);

/// <summary>Effet d'objet. Characteristic est commune au bonus et au malus d'une même stat (ex. +Force / -Force).</summary>
public sealed record EffectInfo(int Id, string Description, bool IsMalus, int Characteristic);
