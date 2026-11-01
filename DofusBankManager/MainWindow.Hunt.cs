using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using DofusBankManager.Data;
using DofusBankManager.Hdv;
using DofusBankManager.Hunt;

namespace DofusBankManager;

public sealed record HuntGroupRow(long Id, string Monsters, int Count, int TotalLevel, string Stars, double? Xp,
    string Drops, double? DropValue, bool HasArchi, string Archi, long? ArchiPrice);

public sealed record ArchiRow(string Time, DateTime TimeSort, string Name, int Level, string Map, long? Price, string PriceSeen);

public sealed record FightRow(string Time, DateTime TimeSort, string Duration, string Monsters, long Xp, long Kamas,
    string Drops, double DropValue);

public sealed record ProjectionRow(string Span, double Xp, double Kamas, double Drops, double Total);

/// <summary>
/// Onglet Chasse : monstres de la map avec l'XP et les drops estimés d'après les combats déjà faits, alerte sonore et
/// historique des archi-monstres aperçus, et rentabilité de la session projetée sur 30, 60 et 120 minutes.
/// Les données du jeu n'ont ni XP ni drops des monstres : tout est appris des fins de combat du personnage.
/// </summary>
public partial class MainWindow
{
    HuntTracker? _hunt;

    void InitHunt(GameData data)
    {
        _hunt = new HuntTracker(data, () => _tracker.State.CharacterId);
        _hunt.Log += AddLog;
        _hunt.Changed += RefreshHunt;
        _hunt.ArchiSpotted += OnArchiSpotted;
        _tracker.MapChanged += _hunt.OnMapChanged;
        _tracker.MapSprites += s => Safe(() => _hunt.OnSprites(s));
        _tracker.CharacterStats += s => Safe(() => _hunt.OnStats(s));
        _tracker.FightEnded += s => Safe(() => _hunt.OnFightEnd(s));
        _tracker.PartyChanged += s => Safe(() => _hunt.OnParty(s));
        HuntSound.IsChecked = _hunt.SoundAlert;
    }

    /// <summary>
    /// Ces messages contiennent aussi des noms de joueurs : en cas d'erreur, rien n'est recopié dans le journal
    /// (contrairement aux autres messages ignorés).
    /// </summary>
    void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { AddLog($"Message de chasse ignoré ({ex.GetType().Name})."); }
    }

    void OnArchiSpotted(ArchiSighting a)
    {
        if (_hunt?.SoundAlert == true) PlayAlert();
        if (_data != null) HuntStatus.Text = $"⚠ Archi-monstre sur la map : {_data.GetMonster(a.MonsterId).Name} (niveau {a.Level})";
    }

    static void PlayAlert()
    {
        var alarm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
        try
        {
            if (File.Exists(alarm)) new SoundPlayer(alarm).Play();
            else SystemSounds.Exclamation.Play();
        }
        catch { SystemSounds.Exclamation.Play(); }
    }

    void HuntSound_Changed(object sender, RoutedEventArgs e)
    {
        if (_hunt != null) _hunt.SoundAlert = HuntSound.IsChecked == true;
    }

    void HuntTestSound_Click(object sender, RoutedEventArgs e) => PlayAlert();

    void HuntNewSession_Click(object sender, RoutedEventArgs e) => _hunt?.NewSession();

    void HuntClearArchis_Click(object sender, RoutedEventArgs e) => _hunt?.ClearArchis();

    /// <summary>Ce que les combats passés apprennent sur un monstre : XP de base (hors sagesse) et objets obtenus.</summary>
    sealed class MonsterLearning
    {
        public double XpSum;
        public int Instances;
        public int Fights;
        public Dictionary<int, (int Fights, int Quantity)> Items = new();
        public double? Xp => Instances > 0 ? XpSum / Instances : null;
    }

    /// <summary>
    /// Apprend, pour chaque monstre, une XP de base et ses drops à partir des fins de combat. L'XP d'un combat est
    /// ramenée sans sagesse (÷ (1 + sagesse/100)), puis répartie entre les monstres selon leur niveau.
    /// Les drops d'un combat sont attribués à tous ses monstres : avec des groupes variés, la fréquence se précise.
    /// </summary>
    Dictionary<int, MonsterLearning> Learn()
    {
        var result = new Dictionary<int, MonsterLearning>();
        foreach (var f in _hunt!.Fights.Where(f => f.Monsters.Count > 0))
        {
            double baseXp = f.Xp / (1 + f.Wisdom / 100.0);
            double levels = f.Monsters.Sum(m => Math.Max(1, m.Level));
            foreach (var m in f.Monsters)
            {
                var l = Get(m.Id);
                l.XpSum += baseXp * Math.Max(1, m.Level) / levels;
                l.Instances++;
            }
            foreach (var id in f.Monsters.Select(m => m.Id).Distinct())
            {
                var l = Get(id);
                l.Fights++;
                foreach (var d in f.Drops)
                {
                    var (n, q) = l.Items.GetValueOrDefault(d.ItemId);
                    l.Items[d.ItemId] = (n + 1, q + d.Quantity);
                }
            }
        }
        return result;

        MonsterLearning Get(int id) => result.TryGetValue(id, out var l) ? l : result[id] = new MonsterLearning();
    }

    void RefreshHunt()
    {
        if (!_ready || _data == null || _hunt == null) return;
        var learned = Learn();
        int wisdom = _hunt.Wisdom ?? 0;

        HuntWisdom.Text = _hunt.Wisdom?.ToString("N0") ?? "?";
        HuntProspection.Text = _hunt.Prospection?.ToString("N0") ?? "?";
        HuntGroupProspection.Text = _hunt.GroupProspection?.ToString("N0") ?? "?";
        HuntGroupProspection.ToolTip = _hunt.Party.Count > 0
            ? $"Somme des {_hunt.Party.Count} membres du groupe : " + string.Join(" + ", _hunt.Party.Values.Select(p => p.Prospection))
            : "Pas de groupe : prospection du personnage seul";

        // Groupes de la map
        var groups = _hunt.Groups.Values.OrderByDescending(g => g.Monsters.Sum(m => m.Level)).Select(g =>
        {
            var names = string.Join(", ", g.Monsters.GroupBy(m => m.Id).Select(x =>
                (x.Count() > 1 ? $"{x.Count()}× " : "") + _data.GetMonster(x.Key).Name
                + " (" + string.Join("/", x.Select(m => m.Level)) + ")"));
            double factor = (1 + wisdom / 100.0) * (1 + g.Bonus / 100.0);
            double? xp = g.Monsters.All(m => learned.GetValueOrDefault(m.Id)?.Xp != null)
                ? g.Monsters.Sum(m => learned[m.Id].Xp!.Value) * factor : null;

            // Drops : pour chaque objet, la plus forte fréquence parmi les monstres du groupe
            var drops = g.Monsters.Select(m => m.Id).Distinct()
                .Where(learned.ContainsKey)
                .SelectMany(id => learned[id].Items.Select(kv =>
                    (Item: kv.Key, Chance: (double)kv.Value.Fights / learned[id].Fights, Qty: (double)kv.Value.Quantity / kv.Value.Fights)))
                .GroupBy(d => d.Item).Select(x => x.MaxBy(d => d.Chance))
                .Select(d => (d.Item, d.Chance, d.Qty, Value: d.Chance * d.Qty * (UnitPrice(d.Item) ?? 0)))
                .OrderByDescending(d => d.Value).ThenByDescending(d => d.Chance).ToList();
            var dropText = string.Join(", ", drops.Take(4).Select(d => $"{_data.GetItem(d.Item).Name} {d.Chance:0%}"));

            var archi = g.Monsters.FirstOrDefault(m => _data.GetMonster(m.Id).IsArchi);
            long? archiPrice = archi != null ? _pda?.Prices.GetValueOrDefault(PdaTracker.Key(archi.Id, PdaTracker.Archi))?.Price : null;
            return new HuntGroupRow(g.SpriteId, names, g.Monsters.Count, g.Monsters.Sum(m => m.Level),
                g.Bonus > 0 ? $"+{g.Bonus} %" : "", xp, dropText, drops.Count > 0 ? drops.Sum(d => d.Value) : null,
                archi != null, archi != null ? _data.GetMonster(archi.Id).Name : "", archiPrice);
        }).ToList();
        SetRows(HuntGroupGrid, groups);
        HuntMap.Text = _hunt.MapId.Length > 0 ? $"Map {_hunt.MapId} · {groups.Count} groupe(s)" : "Map inconnue : changez de map en jeu";
        HuntEmpty.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!groups.Any(g => g.HasArchi)) HuntStatus.Text = "";

        // Archi-monstres aperçus, avec leur dernier prix de pierre relevé
        SetRows(HuntArchiGrid, _hunt.Archis.Select(a =>
        {
            var price = _pda?.Prices.GetValueOrDefault(PdaTracker.Key(a.MonsterId, PdaTracker.Archi));
            return new ArchiRow(Ago(a.Time), a.Time, _data.GetMonster(a.MonsterId).Name, a.Level, a.MapId,
                price?.Price, price != null ? Ago(price.SeenAt) : "");
        }).ToList());

        // Combats et rentabilité de la session
        double Value(FightRecord f) => f.Drops.Sum(d => d.Quantity * (UnitPrice(d.ItemId) ?? 0));
        var session = _hunt.Fights.Where(f => f.Time >= _hunt.SessionStart).ToList();
        SetRows(HuntFightGrid, session.AsEnumerable().Reverse().Take(100).Select(f => new FightRow(
            f.Time.ToString("HH:mm"), f.Time, $"{f.DurationSeconds / 60}:{f.DurationSeconds % 60:00}",
            string.Join(", ", f.Monsters.GroupBy(m => m.Id).Select(x => (x.Count() > 1 ? $"{x.Count()}× " : "") + _data.GetMonster(x.Key).Name)),
            f.Xp, f.Kamas,
            string.Join(", ", f.Drops.Select(d => $"{d.Quantity}× {_data.GetItem(d.ItemId).Name}")), Value(f))).ToList());

        var minutes = Math.Max(1, (DateTime.Now - _hunt.SessionStart).TotalMinutes);
        double xpTotal = session.Sum(f => f.Xp), kamasTotal = session.Sum(f => f.Kamas), dropsTotal = session.Sum(Value);
        int unpriced = session.SelectMany(f => f.Drops).Select(d => d.ItemId).Distinct().Count(id => UnitPrice(id) == null);
        HuntSession.Text = $"Session depuis {_hunt.SessionStart:HH:mm} ({minutes:0} min) · {session.Count} combat(s) · "
            + $"{xpTotal:N0} XP · {kamasTotal:N0} kamas · drops {Kamas(dropsTotal)}"
            + (unpriced > 0 ? $" ({unpriced} objet(s) sans prix HDV, comptés 0)" : "");
        HuntProjection.ItemsSource = session.Count == 0 ? null : new[] { 30, 60, 120 }.Select(span =>
        {
            double k = span / minutes;
            return new ProjectionRow($"{span} min", xpTotal * k, kamasTotal * k, dropsTotal * k, (kamasTotal + dropsTotal) * k);
        }).ToList();
        HuntLearned.Text = $"Estimations apprises de {_hunt.Fights.Count(f => f.Monsters.Count > 0)} combat(s) "
            + $"({learned.Count} monstre(s) connus). Sagesse et étoiles du groupe appliquées à l'XP.";
    }
}
