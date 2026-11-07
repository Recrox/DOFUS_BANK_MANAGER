using System.IO;
using System.Net.Http;
using System.Xml.Linq;

namespace DofusRetroManager.Data;

public sealed partial class GameData
{
    /// <summary>Quête du Dofus Ocre (« L'éternelle moisson. »), et le PNJ à qui rapporter les âmes.</summary>
    const int OcreQuest = 439, OcreNpc = 1077;

    /// <summary>
    /// Étapes de la quête du Dofus Ocre, d'après le fichier de langue « quests » : Q.q[quête].s = étapes, Q.o = objectifs
    /// (type 12 : « Rapporter #3 âme de #2 à #1 », p = [PNJ, monstre, nombre]). Le fichier ne dit pas à quelle étape
    /// appartient un objectif : ils sont rangés dans l'ordre, et chaque étape en compte autant que de pierres d'âme
    /// parfaites données en récompense (Q.s[étape].r[2]) ; la dernière étape, sans pierre, prend le reste.
    /// Si les comptes ne tombent pas juste (données changées), la quête n'est pas proposée.
    /// </summary>
    static void LoadOcre(GameData data, AsObject file)
    {
        if (file.GetValueOrDefault("Q") is not AsObject Q || Q.GetValueOrDefault("q") is not AsObject quests
            || Q.GetValueOrDefault("s") is not AsObject steps || Q.GetValueOrDefault("o") is not AsObject objectives
            || quests.GetValueOrDefault(OcreQuest.ToString()) is not AsObject quest || quest.GetValueOrDefault("s") is not List<object?> stepIds)
            return;
        var name = Avm1.ToStr(quest.GetValueOrDefault("n"));
        var souls = objectives
            .Select(kv => (Id: int.TryParse(kv.Key, out var id) ? id : 0, O: kv.Value as AsObject))
            .Where(x => x.O != null && (int)Avm1.ToNum(x.O.GetValueOrDefault("t")) == 12 && x.O.GetValueOrDefault("p") is List<object?> { Count: >= 3 } p
                        && (int)Avm1.ToNum(p[0]) == OcreNpc)
            .OrderBy(x => x.Id)
            .Select(x => (int)Avm1.ToNum(((List<object?>)x.O!["p"]!)[1]))
            .ToList();
        // Étapes « L'éternelle moisson. » (la suivante, « L'ultime Epreuve. », ne demande pas d'âme)
        var sizes = stepIds.Select(id => steps.GetValueOrDefault(Avm1.ToStr(id)) as AsObject)
            .Where(s => s != null && Avm1.ToStr(s.GetValueOrDefault("n")) == name)
            .Select(s => s!.GetValueOrDefault("r") is List<object?> { Count: > 2 } r && r[2] is List<object?> stones
                ? stones.OfType<List<object?>>().Sum(x => x.Count > 1 ? (int)Avm1.ToNum(x[1]) : 0) : -1)
            .ToList();
        int known = sizes.Where(s => s > 0).Sum();
        if (sizes.Count == 0 || sizes.Count(s => s <= 0) != 1 || sizes[^1] > 0 || known >= souls.Count) return;
        sizes[^1] = souls.Count - known;
        int at = 0;
        foreach (var size in sizes)
        {
            data.OcreSteps.Add(souls.GetRange(at, size));
            at += size;
        }
    }
}
