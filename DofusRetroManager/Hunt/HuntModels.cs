using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hunt;

/// <summary>Monstre d'un groupe : modèle (fichier de langue « monsters ») et niveau.</summary>
public sealed record GroupMonster(int Id, int Level);

/// <summary>
/// Alerte du joueur : prévenir (son et bulle) quand un groupe de la map contient au moins MinCount monstres de ce type,
/// de niveau MinLevel à MaxLevel (ex. 3 Craqueleurs des plaines de niveau 49 au plus).
/// </summary>
public sealed class MonsterAlert
{
    public int MonsterId { get; set; }
    public int MinCount { get; set; } = 1;
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 9999;
    public bool Enabled { get; set; } = true;
}

/// <summary>Groupe de monstres présent sur la map. Bonus : étoiles du groupe, en % d'XP et de drop.</summary>
public sealed record MapGroup(long SpriteId, int Bonus, List<GroupMonster> Monsters, DateTime SeenAt);

/// <summary>Archi-monstre aperçu sur une map.</summary>
public sealed class ArchiSighting
{
    public DateTime Time { get; set; }
    public int MonsterId { get; set; }
    public int Level { get; set; }
    public string MapId { get; set; } = "";
}

public sealed class FightDrop
{
    public int ItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Combat terminé : ce qu'a gagné le personnage, les monstres affrontés et ses stats à ce moment.</summary>
public sealed class FightRecord
{
    public DateTime Time { get; set; }
    /// <summary>Personnage du joueur qui a fait le combat (multi-compte : un combat par personnage).</summary>
    public string Character { get; set; } = "";
    public int DurationSeconds { get; set; }
    public long Xp { get; set; }
    public long Kamas { get; set; }
    public List<FightDrop> Drops { get; set; } = new();
    public List<GroupMonster> Monsters { get; set; } = new();
    public int Wisdom { get; set; }
    public int Prospection { get; set; }
    public int GroupProspection { get; set; }
}

/// <summary>
/// Personnage suivi sur une connexion au jeu. En multi-compte, chaque fenêtre a sa propre connexion :
/// sagesse, prospection, groupe et map sont tenus séparément pour chacune.
/// </summary>
public sealed class HuntCharacter(string stream, int number)
{
    public string Stream { get; } = stream;
    public int Number { get; } = number;
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int? Level { get; set; }
    public int? Wisdom { get; set; }
    public int? Prospection { get; set; }
    /// <summary>Initiative (onglet Switcheur : ordre de jeu des personnages).</summary>
    public int? Initiative { get; set; }
    /// <summary>Membres du groupe : identifiant -> (niveau, prospection). Vide hors groupe.</summary>
    public Dictionary<long, (int Level, int Prospection)> Party { get; } = new();
    public string MapId { get; set; } = "";
    public Dictionary<long, MapGroup> Groups { get; } = new();
    /// <summary>Dernier changement de map : la fenêtre où l'on joue est celle qui bouge.</summary>
    public DateTime LastMove { get; set; }
    /// <summary>Dernier message reçu sur cette connexion.</summary>
    public DateTime LastActivity { get; set; } = DateTime.Now;

    /// <summary>Économe : en combat, et sorts déjà lancés dans ce combat (dans l'ordre).</summary>
    public bool InFight { get; set; }
    public List<int> UsedSpells { get; } = new();
    /// <summary>Sorts reçus sur cette connexion (la liste peut arriver avant le nom du personnage).</summary>
    public List<int>? KnownSpells { get; set; }

    /// <summary>Points de vie du personnage (As, puis GTM en combat).</summary>
    public int? Life { get; set; }
    public int? MaxLife { get; set; }
    /// <summary>Heure du dernier relevé des points de vie : hors combat, ils remontent ensuite d'un point par intervalle de RegenMs.</summary>
    public DateTime LifeAt { get; set; } = DateTime.Now;
    /// <summary>Régénération hors combat : un point de vie toutes les RegenMs millisecondes (ILS du jeu, sinon 1 s).</summary>
    public double RegenMs { get; set; } = 1000;
    /// <summary>Points de vie estimés maintenant : ceux relevés, plus la régénération hors combat depuis (jamais au-delà du maximum).</summary>
    public int? CurrentLife => Life is not { } l ? null
        : InFight || MaxLife is not { } m ? l
        : (int)Math.Min(m, l + Math.Max(0, (DateTime.Now - LifeAt).TotalMilliseconds) / Math.Max(100, RegenMs));
    public int? LifePercent => CurrentLife is { } l && MaxLife is { } m && m > 0 ? (int)Math.Round(100.0 * Math.Clamp(l, 0, m) / m) : null;

    /// <summary>Début et durée (ms) du tour de jeu du personnage en cours (null : ce n'est pas son tour).</summary>
    public DateTime? TurnStart { get; set; }
    public int TurnMs { get; set; }

    /// <summary>Niveau de chaque sort du personnage (SL), pour la durée de ses effets.</summary>
    public Dictionary<int, int> SpellLevels { get; } = new();
    /// <summary>
    /// Puissance Sylvestre (Sadida, onglet Switcheur) : tours restants sous forme d'arbre. Lancé pendant un tour, le sort
    /// prend effet à la fin de ce tour (SylvestrePending), puis chaque fin de tour du personnage en retire un.
    /// </summary>
    public int SylvestreTurns { get; set; }
    public int? SylvestrePending { get; set; }
    public bool IsTree => InFight && SylvestreTurns > 0;

    /// <summary>Challenges du combat en cours (message Gd) : identifiant -> réussi (true), raté (false) ou en cours (null).</summary>
    public Dictionary<int, bool?> Challenges { get; } = new();

    /// <summary>Connexion qui a donné quelque chose d'utile (un personnage, des stats ou une map).</summary>
    public bool HasData => Name.Length > 0 || Wisdom != null || Prospection != null || MapId.Length > 0;

    public string Label => Name.Length > 0 ? Name : $"Connexion {Number}";

    /// <summary>Prospection du groupe (somme des membres), sinon celle du personnage.</summary>
    public int? GroupProspection => Party.Count > 0 ? Party.Values.Sum(p => p.Prospection) : Prospection;
    /// <summary>Niveau du groupe (somme des membres), sinon celui du personnage.</summary>
    public int? GroupLevel => Party.Count > 0 ? Party.Values.Sum(p => p.Level) : Level;
}

/// <summary>Évènements notifiés dans l'onglet Switcheur.</summary>
public enum GameNotice { Fight, Turn, Exchange, Party }

/// <summary>
/// Dernier état connu d'un de vos personnages, gardé d'un lancement à l'autre (chasse.json) : l'application, relancée
/// pendant que le jeu est ouvert (mise à jour…), retrouve son ordre de jeu, sa vie et son identifiant sans reconnexion.
/// </summary>
public sealed class KnownCharacter
{
    public long Id { get; set; }
    public int? Level { get; set; }
    public int? Initiative { get; set; }
    public int? Wisdom { get; set; }
    public int? Prospection { get; set; }
    public int? Life { get; set; }
    public int? MaxLife { get; set; }
    public DateTime LifeAt { get; set; }
    public double RegenMs { get; set; } = 1000;

    /// <summary>Vie estimée maintenant (hors combat : régénération depuis le dernier relevé), en pourcentage.</summary>
    public int? LifePercent => Life is not { } l || MaxLife is not { } m || m <= 0 ? null
        : (int)Math.Round(100.0 * Math.Clamp(l + Math.Max(0, (DateTime.Now - LifeAt).TotalMilliseconds) / Math.Max(100, RegenMs), 0, m) / m);
}

sealed class HuntData
{
    public List<ArchiSighting> Archis { get; set; } = new();
    public List<FightRecord> Fights { get; set; } = new();
    public DateTime SessionStart { get; set; } = DateTime.Now;
    /// <summary>Session en pause depuis (null : en cours) et durée des pauses déjà terminées, en secondes.</summary>
    public DateTime? SessionPausedAt { get; set; }
    public double SessionPausedSeconds { get; set; }
    public bool SoundAlert { get; set; } = true;
    /// <summary>Volume de l'alerte archi-monstre, de 0 à 100.</summary>
    public int SoundVolume { get; set; } = 70;
    public bool ArchiPopup { get; set; } = true;
    public List<MonsterAlert> Alerts { get; set; } = new();
    /// <summary>Sorts connus par personnage (nom -> sorts), pour l'Économe même si l'appli démarre en jeu.</summary>
    public Dictionary<string, List<int>> Spells { get; set; } = new();
    /// <summary>Classe et sexe par personnage (nom -> apparence), pour son icône même si l'appli démarre en jeu.</summary>
    public Dictionary<string, CharacterLook> Looks { get; set; } = new();
    /// <summary>Dernier état connu de vos personnages (nom -> identifiant, niveau, initiative, vie…).</summary>
    public Dictionary<string, KnownCharacter> Known { get; set; } = new();
}
