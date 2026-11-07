using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hunt;

public sealed partial class HuntTracker
{
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
        bool wasTurn = c.TurnStart != null;
        c.TurnStart = null;
        c.SylvestreTurns = 0;
        c.SylvestrePending = null;
        if (wasTurn) TurnEnded?.Invoke(c);
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
}
