using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hunt;

public sealed partial class HuntTracker
{
    /// <summary>
    /// Sprites de la map : "+cellule;direction;bonus;id;monstres;-3;apparences;niveaux;…" pour un groupe de monstres,
    /// "-id" pour un sprite retiré. Les autres sprites (joueurs, PNJ, marchands…) sont ignorés sans être lus.
    /// </summary>
    public void OnSprites(string body)
    {
        var c = Current;
        bool changed = false;
        foreach (var s in body.Split('|'))
        {
            if (s.Length < 2) continue;
            if (s[0] == '-')
            {
                if (long.TryParse(s.AsSpan(1), out var gone)) changed |= c.Groups.Remove(gone);
                continue;
            }
            if (s[0] is not ('+' or '~')) continue;
            var f = s[1..].Split(';');
            // Personnage de la fenêtre, reconnu par son nom (application relancée après le jeu : identifiant inconnu,
            // nécessaire pour le début de son tour). Seul son identifiant est gardé ; les autres joueurs sont ignorés.
            if (c.Id == 0 && c.Name.Length > 0 && f.Length > 5 && f[5] is not ("-3" or "-1" or "-2")
                && string.Equals(f[4], c.Name, StringComparison.OrdinalIgnoreCase) && long.TryParse(f[3], out var own) && own > 0)
            {
                c.Id = own;
                Remember(c);
                Log?.Invoke($"Identifiant de {c.Name} retrouvé sur la map.");
                continue;
            }
            if (f.Length < 8 || f[5] != "-3" || !long.TryParse(f[3], out var id)) continue;

            var ids = f[4].Split(',');
            var levels = f[7].Split(',');
            var monsters = new List<GroupMonster>();
            // Un boss est affiché sans niveau (-1, ex. Kralamoure Géant dans son antre) : on prend son grade 3
            for (int i = 0; i < ids.Length; i++)
                if (int.TryParse(ids[i], out var m))
                {
                    int level = i < levels.Length && int.TryParse(levels[i], out var l) ? l : 0;
                    monsters.Add(new GroupMonster(m, level >= 1 ? level : _data.GetMonster(m).Grade3Level));
                }
            if (monsters.Count == 0) continue;

            bool known = c.Groups.ContainsKey(id);
            c.Groups[id] = new MapGroup(id, int.TryParse(f[2], out var bonus) ? bonus : 0, monsters, DateTime.Now);
            changed = true;
            if (!known) { CheckArchis(c.MapId, monsters); CheckAlerts(c, monsters); }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Dernière alerte par map et par règle : plusieurs fenêtres sur la même map, ou un groupe revu, ne sonnent qu'une fois.</summary>
    readonly Dictionary<(string Map, MonsterAlert Alert), DateTime> _alerted = new();

    void CheckAlerts(HuntCharacter c, List<GroupMonster> monsters)
    {
        var now = DateTime.Now;
        foreach (var alert in _store.Alerts.Where(a => a.Enabled))
        {
            int count = monsters.Count(m => m.Id == alert.MonsterId && m.Level >= alert.MinLevel && m.Level <= alert.MaxLevel);
            if (count < Math.Max(1, alert.MinCount)) continue;
            if (_alerted.TryGetValue((c.MapId, alert), out var last) && now - last < ArchiRepeat) continue;
            _alerted[(c.MapId, alert)] = now;
            AlertMatched?.Invoke(c, alert, count);
        }
    }

    void CheckArchis(string mapId, List<GroupMonster> monsters)
    {
        foreach (var m in monsters.Where(m => _data.GetMonster(m.Id).IsArchi))
        {
            var now = DateTime.Now;
            // Multi-compte : plusieurs fenêtres sur la même map voient le même archi, une seule alerte
            if (_store.Archis.Any(a => a.MonsterId == m.Id && a.MapId == mapId && now - a.Time < ArchiRepeat)) continue;
            var sighting = new ArchiSighting { Time = now, MonsterId = m.Id, Level = m.Level, MapId = mapId };
            _store.Archis.Insert(0, sighting);
            if (_store.Archis.Count > MaxArchis) _store.Archis.RemoveRange(MaxArchis, _store.Archis.Count - MaxArchis);
            Save();
            Log?.Invoke($"Archi-monstre repéré : {_data.GetMonster(m.Id).Name} (niveau {m.Level}, map {mapId}).");
            ArchiSpotted?.Invoke(sighting);
        }
    }
}
