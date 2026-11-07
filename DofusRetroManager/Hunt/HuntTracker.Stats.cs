using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hunt;

public sealed partial class HuntTracker
{
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
            if (c.InFight && hp < c.Life) c.FightLifeLost += c.Life!.Value - hp!.Value; // Intouchable
            c.Life = hp;
            c.MaxLife = hpMax;
            Changed?.Invoke();
            return;
        }
    }
}
