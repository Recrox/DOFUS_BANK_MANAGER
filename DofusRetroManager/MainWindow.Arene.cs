using System.Windows;
using System.Windows.Threading;
using DofusRetroManager.Hunt;

namespace DofusRetroManager;

/// <summary>Challenge suivi dans le chronomètre de l'onglet Combat. State : 1 réussi/en bonne voie, 0 en cours, -1 raté.</summary>
public sealed record ChallengeRow(string Character, string Name, string Rule, string Status, int State, bool Announced, int Gfx = 0);

/// <summary>Combat compté par le chronomètre (les personnages d'un même combat sont réunis).</summary>
public sealed record ArenaFightRow(string Time, DateTime TimeSort, string Characters, string Monsters, long Kamas, double DropValue, long Xp)
{
    public double Total => Kamas + DropValue;
}

/// <summary>
/// Onglet Combat, chronomètre (ex-onglet Arène) : challenges Économe et Barbare du combat (annoncés par le serveur, et suivis d'après les sorts lancés),
/// et chronomètre de rentabilité : combats terminés pendant qu'il tourne, rythme et gains moyens, en temps réel.
/// Simple affichage : c'est toujours le joueur qui choisit ses actions (REGLES.md, règle 3).
/// </summary>
public partial class MainWindow
{
    /// <summary>Deux fins de combat plus rapprochées que ce délai sont le même combat (multi-compte).</summary>
    static readonly TimeSpan SameFight = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Deux fins de combat de vos personnages sont le même combat (groupés en jeu ou non) : fin à SameFight près et mêmes
    /// monstres affrontés (modèles et niveaux).
    /// </summary>
    static bool IsSameFight(FightRecord a, FightRecord b) =>
        (a.Time - b.Time).Duration() <= SameFight && a.Monsters.Count == b.Monsters.Count
        && a.Monsters.OrderBy(m => m.Id).ThenBy(m => m.Level).SequenceEqual(b.Monsters.OrderBy(m => m.Id).ThenBy(m => m.Level));

    /// <summary>Périodes où le chronomètre a tourné ; la dernière est ouverte (End null) tant qu'il tourne.</summary>
    readonly List<(DateTime Start, DateTime? End)> _chrono = new();
    readonly DispatcherTimer _chronoTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    bool ChronoRunning => _chrono.Count > 0 && _chrono[^1].End == null;

    TimeSpan ChronoElapsed => _chrono.Aggregate(TimeSpan.Zero, (t, p) => t + ((p.End ?? DateTime.Now) - p.Start));

    void InitArene()
    {
        _chronoTimer.Tick += (_, _) => RefreshChrono();
        InitAreneGroups();
        _hunt!.Changed += RefreshArene;
        RefreshArene();
    }

    void ChronoStart_Click(object sender, RoutedEventArgs e)
    {
        if (ChronoRunning) _chrono[^1] = (_chrono[^1].Start, DateTime.Now);
        else _chrono.Add((DateTime.Now, null));
        if (ChronoRunning) _chronoTimer.Start(); else _chronoTimer.Stop();
        RefreshChrono();
    }

    void ChronoReset_Click(object sender, RoutedEventArgs e)
    {
        bool running = ChronoRunning;
        _chrono.Clear();
        if (running) _chrono.Add((DateTime.Now, null));
        RefreshChrono();
    }

    void RefreshArene()
    {
        if (!_ready || _data == null || _hunt == null) return;
        RefreshChallenges();
        RefreshChrono();
    }

    /// <summary>
    /// Économe (5) : chaque action une seule fois par combat ; Barbare (9) : aucun sort, seulement l'arme. Le serveur annonce
    /// les challenges du combat (Gd) et leur résultat ; en attendant, l'état est déduit des sorts lancés par le personnage.
    /// </summary>
    void RefreshChallenges()
    {
        // Challenges annoncés par le jeu (Gd) : leur résultat quand il est connu ; en attendant, pour ceux qu'on sait suivre
        // (Économe, Versatile, Barbare, Mystique, Borné, Intouchable), l'état déduit des actions du personnage
        var rows = new List<ChallengeRow>();
        foreach (var c in _hunt!.Characters.Where(c => c.Challenges.Count > 0 && (c.Name.Length == 0 || InAreneGroup(c.Name))).OrderBy(c => c.Label))
            foreach (var (id, result) in c.Challenges)
            {
                var kind = KindOf(id);
                var (failed, why) = kind == ChallengeKind.None ? (false, "") : Track(c, kind);
                var (status, state) = result switch
                {
                    true => ("Réussi", 1),
                    false => ("Raté", -1),
                    _ when failed => ($"Raté ({why})", -1),
                    _ => (why.Length > 0 ? $"En cours · {why}" : "En cours", 0),
                };
                rows.Add(new ChallengeRow(c.Label, _data!.FightChallenges.GetValueOrDefault(id, $"Challenge n° {id}"),
                    _data.FightChallengeRules.GetValueOrDefault(id, ""), status, state, true, _data.FightChallengeGfx.GetValueOrDefault(id, -1)));
            }
        // Un même challenge au même état pour plusieurs personnages (combat à plusieurs) : une seule icône, les personnages au survol
        rows = rows.GroupBy(r => (r.Name, r.Rule, r.Status, r.State, r.Announced, r.Gfx))
            .Select(g => g.First() with { Character = g.Count() == 1 ? g.First().Character : $"{g.Count()} personnages : {string.Join(", ", g.Select(r => r.Character))}" })
            .ToList();
        AreneChallenges.ItemsSource = rows;
        AreneChallengesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AreneOtherChallenges.Text = "";
    }

    void RefreshChrono()
    {
        if (!_ready || _data == null || _hunt == null) return;
        var elapsed = ChronoElapsed;
        ChronoTime.Text = $"{(int)elapsed.TotalHours:00}h{elapsed.Minutes:00}min{elapsed.Seconds:00}s";
        ChronoStart.Content = ChronoRunning ? "⏸ Pause" : _chrono.Count == 0 ? "▶ Démarrer" : "▶ Reprendre";

        // Fins de combat pendant que le chronomètre tournait, réunies par combat (plusieurs personnages)
        var records = _hunt.Fights.Where(f => InAreneGroup(f.Character) && _chrono.Any(p => f.Time >= p.Start && f.Time <= (p.End ?? DateTime.Now)))
            .OrderBy(f => f.Time).ToList();
        var fights = new List<List<FightRecord>>();
        foreach (var f in records)
            // Plusieurs combats peuvent finir en même temps (personnages séparés) : on cherche le bon parmi les derniers
            if (fights.FindLast(g => IsSameFight(g[0], f)) is { } same) same.Add(f);
            else fights.Add([f]);

        double DropValue(FightRecord f) => f.Drops.Sum(d => d.Quantity * (DropPrice(d.ItemId) ?? 0));
        var rows = fights.Select(g => new ArenaFightRow(g[0].Time.ToString("HH:mm:ss"), g[0].Time,
            string.Join(", ", g.Select(f => f.Character).Distinct()),
            string.Join(", ", g[0].Monsters.GroupBy(m => m.Id).Select(x => (x.Count() > 1 ? $"{x.Count()}× " : "") + _data.GetMonster(x.Key).Name)),
            g.Sum(f => f.Kamas), g.Sum(DropValue), g.Sum(f => f.Xp))).ToList();
        SetRows(ChronoGrid, rows.AsEnumerable().Reverse().ToList());

        int n = rows.Count;
        double kamas = rows.Sum(r => r.Kamas), drops = rows.Sum(r => r.DropValue), total = kamas + drops, xp = rows.Sum(r => r.Xp);
        ChronoFights.Text = n == 0
            ? (_chrono.Count == 0 ? "Démarrez le chronomètre : chaque fin de combat sera comptée." : "Aucun combat terminé pour l'instant.")
            : $"{n} combat(s) effectué(s) · moyenne de 1 combat / {Duration(elapsed.TotalSeconds / n)}"
              + (elapsed.TotalHours > 0 ? $" ({n / elapsed.TotalHours:0.#} combats / heure)" : "");
        ChronoGains.Text = n == 0 ? ""
            : $"Moyenne de {total / n:N0} k / combat · {total:N0} k au total (kamas {kamas:N0} + drops {drops:N0})";
        ChronoRate.Text = n == 0 || elapsed.TotalHours <= 0 ? ""
            : $"Rythme : {total / elapsed.TotalHours:N0} k / heure · {xp:N0} XP ({xp / elapsed.TotalHours:N0} XP / heure)";
    }

    /// <summary>Durée lisible : « 50 s », « 2 min 05 s ».</summary>
    static string Duration(double seconds) =>
        seconds < 60 ? $"{seconds:0} s" : $"{(int)(seconds / 60)} min {(int)seconds % 60:00} s";
}
