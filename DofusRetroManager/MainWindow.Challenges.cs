using System.Globalization;
using System.Text;
using DofusRetroManager.Data;
using DofusRetroManager.Hunt;

namespace DofusRetroManager;

/// <summary>Challenges que l'application sait suivre d'après les seules actions du personnage (sorts, arme, tours, vie).</summary>
public enum ChallengeKind { None, Econome, Versatile, Barbare, Mystique, Borne, Intouchable }

/// <summary>
/// Aides aux challenges (superposition en jeu) : pour chaque challenge annoncé par le jeu et suivable, l'état d'après les
/// actions du personnage, et ses sorts disponibles, déjà lancés ou interdits. Les autres challenges (déplacements, cases,
/// ordre des morts, autres combattants) demanderaient des données qui ne sont pas lues (REGLES.md, règle 4) : pas d'aide.
/// Simple affichage : c'est toujours le joueur qui choisit et lance ses sorts (règle 3).
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Challenge reconnu par son nom (fichier de langue « fightChallenge ») : un même challenge existe sous plusieurs
    /// identifiants (ex. Barbare : 9 et 101).
    /// </summary>
    ChallengeKind KindOf(int id)
    {
        var name = _data?.FightChallenges.GetValueOrDefault(id) ?? "";
        var plain = new string(name.Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray()).Trim().ToLowerInvariant();
        return plain switch
        {
            "econome" => ChallengeKind.Econome,
            "versatile" => ChallengeKind.Versatile,
            "barbare" => ChallengeKind.Barbare,
            "mystique" => ChallengeKind.Mystique,
            "borne" => ChallengeKind.Borne,
            "intouchable" => ChallengeKind.Intouchable,
            _ => ChallengeKind.None,
        };
    }

    string ActionName(int spell) => spell == HuntTracker.Weapon ? "l'arme"
        : _data!.Spells.GetValueOrDefault(spell) is { Length: > 0 } n ? n : $"sort {spell}";

    /// <summary>État d'un challenge suivable d'après les actions du personnage : raté (et pourquoi), sinon où il en est.</summary>
    (bool Failed, string Why) Track(HuntCharacter c, ChallengeKind kind)
    {
        var spells = c.UsedSpells.Where(s => s != HuntTracker.Weapon).Distinct().ToList();
        switch (kind)
        {
            case ChallengeKind.Econome:
                var twice = c.UsedSpells.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => ActionName(g.Key)).ToList();
                return twice.Count > 0 ? (true, "relancé : " + string.Join(", ", twice)) : (false, $"{c.UsedSpells.Count} action(s) différente(s)");
            case ChallengeKind.Versatile:
                return c.VersatileBroken ? (true, "une action relancée dans un même tour") : (false, $"{c.TurnSpells.Count} action(s) ce tour");
            case ChallengeKind.Barbare:
                return spells.Count > 0 ? (true, "sort lancé : " + string.Join(", ", spells.Select(ActionName))) : (false, "aucun sort lancé");
            case ChallengeKind.Mystique:
                return c.UsedSpells.Contains(HuntTracker.Weapon) ? (true, "arme utilisée") : (false, "aucune attaque à l'arme");
            case ChallengeKind.Borne:
                var kinds = c.UsedSpells.Distinct().ToList();
                return kinds.Count > 1 ? (true, "plusieurs actions : " + string.Join(", ", kinds.Select(ActionName)))
                    : kinds.Count == 1 ? (false, "seulement " + ActionName(kinds[0])) : (false, "première action à choisir");
            case ChallengeKind.Intouchable:
                return c.FightLifeLost > 0 ? (true, $"{c.FightLifeLost:N0} PV perdus") : (false, "aucun PV perdu");
            default:
                return (false, "");
        }
    }

    /// <summary>
    /// Aides du personnage affiché (superposition) : une grille de ses sorts et de son arme, dont l'état combine les
    /// challenges annoncés (1 disponible, 2 à relancer, 0 déjà lancé ou interdit, -1 challenge raté), et une ligne d'état.
    /// Rien hors des challenges annoncés et suivables.
    /// </summary>
    void ShowChallenges(HuntCharacter? me)
    {
        var active = me == null ? [] : me.Challenges.Select(kv => (Kind: KindOf(kv.Key), Result: kv.Value))
            .Where(x => x.Kind != ChallengeKind.None).DistinctBy(x => x.Kind).ToList();
        if (me == null || active.Count == 0)
        {
            EconomeStatus.Text = "";
            EconomeSpells.ItemsSource = null;
            return;
        }
        var kinds = active.Select(a => a.Kind).ToHashSet();
        var used = me.UsedSpells.ToHashSet();
        var twice = me.UsedSpells.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var turn = me.TurnSpells.ToHashSet();
        int? first = me.UsedSpells.Count > 0 ? me.UsedSpells[0] : null;

        // Grille seulement pour les challenges qui portent sur les sorts (pas pour Intouchable seul)
        var spellIds = kinds.Any(k => k != ChallengeKind.Intouchable)
            ? _hunt!.SpellsOf(me).Prepend(HuntTracker.Weapon).Concat(me.UsedSpells).Distinct().ToList() : [];
        EconomeSpells.ItemsSource = spellIds.Select(id =>
        {
            int state = 1;
            var notes = new List<string>();
            void Set(int s, string note) { notes.Add(note); state = Rank(s) > Rank(state) ? s : state; }
            bool weapon = id == HuntTracker.Weapon;
            if (kinds.Contains(ChallengeKind.Econome))
                if (twice.Contains(id)) Set(-1, "lancé 2 fois (Économe raté)"); else if (used.Contains(id)) Set(0, "déjà lancé (Économe)");
            if (kinds.Contains(ChallengeKind.Versatile) && turn.Contains(id)) Set(0, "déjà lancé ce tour (Versatile)");
            if (kinds.Contains(ChallengeKind.Barbare) && !weapon) Set(used.Contains(id) ? -1 : 0, "interdit (Barbare : l'arme seulement)");
            if (kinds.Contains(ChallengeKind.Mystique) && weapon) Set(used.Contains(id) ? -1 : 0, "interdite (Mystique : sorts seulement)");
            if (kinds.Contains(ChallengeKind.Borne) && first is { } f)
                if (id == f) Set(2, "à relancer (Borné)"); else Set(used.Contains(id) ? -1 : 0, "interdit (Borné)");
            return new EconomeSpell(id, weapon ? "Arme (corps-à-corps)" : ActionName(id),
                notes.Count == 0 ? "disponible" : string.Join(" · ", notes), state, _data!.SpellLooks.GetValueOrDefault(id));
        }).ToList();

        EconomeStatus.Text = string.Join(" · ", active.Select(a =>
        {
            var (failed, why) = Track(me, a.Kind);
            var name = a.Kind switch
            {
                ChallengeKind.Econome => "Économe", ChallengeKind.Borne => "Borné", _ => a.Kind.ToString(),
            };
            return a.Result switch
            {
                true => $"{name} ✓",
                false => $"{name} ✗",
                _ => failed ? $"{name} ✗ ({why})" : $"{name} : {why}",
            };
        }));

        // Ordre de gravité d'un état : raté, puis déjà lancé ou interdit, puis à relancer, puis disponible
        static int Rank(int s) => s switch { -1 => 3, 0 => 2, 2 => 1, _ => 0 };
    }
}
