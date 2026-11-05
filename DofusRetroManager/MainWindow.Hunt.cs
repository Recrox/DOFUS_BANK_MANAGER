using System.IO;
using System.Media;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using DofusRetroManager.Data;
using DofusRetroManager.Hdv;
using DofusRetroManager.Hunt;

namespace DofusRetroManager;

public sealed record HuntGroupRow(long Id, string Monsters, int Count, int TotalLevel, string Stars, double? Xp,
    string XpText, string Drops, string OfficialDrops, IReadOnlyList<DropDetailRow> DropDetails, bool Expanded,
    double? DropValue, bool HasArchi, string Archi, long? ArchiPrice)
{
    public bool HasDropDetails => DropDetails.Count > 0;
}

/// <summary>Ligne du tableau déroulant des drops d'un groupe. Muted : bloqué ou sous condition (non compté).</summary>
public sealed record DropDetailRow(int ItemId, string Name, string Chance, string Monsters, string Prospection,
    string MaxPerFight, string Price, string Value, string State, bool Muted);

/// <summary>
/// Drop officiel d'un objet sur un groupe : meilleure chance parmi ses monstres (Count = nombre de monstres qui le donnent),
/// valeur espérée par combat (0 si bloqué par la prospection du groupe ou soumis à une condition).
/// </summary>
sealed record GroupDrop(int ItemId, double Chance, int Count, double Value, double? Price, int MinProspection,
    int MaxPerFight, bool Blocked, bool Conditional);

public sealed record ArchiRow(string Time, DateTime TimeSort, string Name, int Level, string Map, long? Price, string PriceSeen,
    CarteTarget? Goto) : IHasCarteTarget
{
    /// <summary>Colonne étroite : « 3 min », « 2 h », « 05/11 » (texte complet au survol).</summary>
    public string ShortTime => Time == "à l'instant" ? "< 1 min"
        : Time.StartsWith("il y a ") ? Time[7..]
        : Time.Length == 10 ? Time[..5] : Time;
    /// <summary>Position sans le nom de la sous-zone : « [12,11] Amakna ».</summary>
    public string ShortMap => System.Text.RegularExpressions.Regex.Replace(Map, @"\s*\(.*\)\s*$", "");
}

public sealed record FightRow(string Time, DateTime TimeSort, string Duration, string Monsters, long Xp, long Kamas,
    string Drops, double DropValue);

public sealed record ProjectionRow(string Span, double Xp, double Kamas, double Drops, double Total);

/// <summary>
/// Onglet Chasse : monstres de la map avec l'XP et les drops estimés d'après les combats déjà faits, alerte sonore et
/// historique des archi-monstres aperçus, et rentabilité de la session projetée sur 30, 60 et 120 minutes.
/// Les drops officiels viennent du fichier de langue « monsterdrops » ; l'XP (absente des données du jeu) vient de
/// monstres.json s'il est fourni, sinon elle est apprise des fins de combat du personnage, comme les drops observés.
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
        _tracker.CharacterSelected += _hunt.OnCharacterSelected;
        _sniffer.StreamClosed += stream => Ui(() => _hunt.OnStreamClosed(stream));
        _tracker.LevelUp += _hunt.OnLevelUp;
        _tracker.MapChanged += _hunt.OnMapChanged;
        _tracker.MapSprites += s => Safe(() => _hunt.OnSprites(s));
        _tracker.CharacterStats += s => Safe(() => _hunt.OnStats(s));
        _tracker.FightEnded += s => Safe(() => _hunt.OnFightEnd(s));
        _tracker.PartyChanged += s => Safe(() => _hunt.OnParty(s));
        _tracker.SpellList += s => Safe(() => _hunt.OnSpellList(s));
        _tracker.FightJoined += _hunt.OnFightJoined;
        _tracker.TurnStarted += _hunt.OnTurnStarted;
        _tracker.LifeRegen += _hunt.OnLifeRegen;
        _tracker.GameAction += s => Safe(() => _hunt.OnGameAction(s));
        _tracker.FightersUpdate += s => Safe(() => _hunt.OnFightersUpdate(s));
        _tracker.Challenge += s => Safe(() => _hunt.OnChallenge(s));
        // Onglet Switcheur : notifications
        _tracker.ExchangeRequested += target => Safe(() => _hunt.OnExchangeRequested(target));
        _tracker.PartyInvited += invited => Safe(() => _hunt.OnPartyInvited(invited));
        _hunt.Notice += OnGameNotice;
        _hunt.PartyJoined += c => Safe(() => OnPartyJoined(c));
        HuntSound.IsChecked = _hunt.SoundAlert;
        HuntVolume.Value = _hunt.SoundVolume;
        InitHuntAlerts();
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
        ShowArchiPopup(a);
        if (_data != null) HuntStatus.Text = $"⚠ Archi-monstre sur la map : {_data.GetMonster(a.MonsterId).Name} (niveau {a.Level})";
    }

    void PlayAlert()
    {
        try { ArchiSound.Play(_hunt?.SoundVolume ?? 70); }
        catch { SystemSounds.Exclamation.Play(); }
    }

    void HuntSound_Changed(object sender, RoutedEventArgs e)
    {
        if (_hunt != null) _hunt.SoundAlert = HuntSound.IsChecked == true;
    }

    void HuntVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Appelé dès le chargement de la fenêtre, avant la création du libellé
        if (HuntVolumeText != null) HuntVolumeText.Text = $"{HuntVolume.Value:0} %";
        if (_hunt != null) _hunt.SoundVolume = (int)Math.Round(HuntVolume.Value);
    }

    /// <summary>Curseur relâché : on fait entendre le nouveau volume.</summary>
    void HuntVolume_DragCompleted(object sender, DragCompletedEventArgs e) => PlayAlert();

    void HuntTestSound_Click(object sender, RoutedEventArgs e) => PlayAlert();

    void HuntNewSession_Click(object sender, RoutedEventArgs e) => _hunt?.NewSession();

    bool _huntCharactersUpdating;

    void HuntCharacter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_huntCharactersUpdating || _hunt == null) return;
        _hunt.Pinned = (HuntCharacterBox.SelectedItem as ComboBoxItem)?.Tag as string;
        RefreshHunt();
    }

    static string CharacterLabel(HuntCharacter c) => c.Label + (c.Level is { } l ? $" (niv. {l})" : "");

    /// <summary>Liste des personnages (une entrée par fenêtre de jeu), en gardant le choix courant.</summary>
    void UpdateHuntCharacters()
    {
        _huntCharactersUpdating = true;
        HuntCharacterBox.Items.Clear();
        HuntCharacterBox.Items.Add(new ComboBoxItem { Content = "Fenêtre active (auto)", Tag = null });
        foreach (var c in _hunt!.Characters.OrderBy(c => c.Label))
            HuntCharacterBox.Items.Add(new ComboBoxItem { Content = CharacterLabel(c), Tag = c.Stream });
        HuntCharacterBox.SelectedItem = HuntCharacterBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == _hunt.Pinned)
            ?? HuntCharacterBox.Items[0];
        _huntCharactersUpdating = false;
    }

    void HuntClearArchis_Click(object sender, RoutedEventArgs e) => _hunt?.ClearArchis();

    /// <summary>Ce que les combats passés apprennent sur un monstre : XP de base (hors sagesse) et objets obtenus.</summary>
    sealed class MonsterLearning
    {
        public double XpSum;
        public double LevelSum;
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
                l.LevelSum += Math.Max(1, m.Level);
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

    /// <summary>
    /// Drops officiels d'un groupe (fichier de langue « monsterdrops »). Pour chaque monstre, taux de son grade, puis
    /// chance = min(100, taux × prospection du groupe / 100 × (1 + étoiles / 100)). Un paquet de cartes a une chance fixe
    /// de 1 sur sr. Objet bloqué si la prospection du groupe est sous son minimum. Valeur par combat =
    /// min(maximum par combat, Σ chances / 100) × prix unitaire (HDV, sinon prix marchand TTG de la période, sinon le dernier).
    /// </summary>
    List<GroupDrop> OfficialDrops(MapGroup g, int prospection)
    {
        var all = new List<(MonsterDrop Drop, double Chance)>();
        foreach (var m in g.Monsters)
        {
            if (!_data!.MonsterDrops.TryGetValue(m.Id, out var drops)) continue;
            int grade = _data.GetMonster(m.Id).GradeOf(m.Level);
            foreach (var d in drops)
            {
                double rate = d.Rates.Count == 0 ? 0 : d.Rates[Math.Min(grade, d.Rates.Count - 1)];
                all.Add((d, d.CardOdds > 0 ? 100.0 / d.CardOdds
                    : Math.Min(100, rate * prospection / 100.0 * (1 + g.Bonus / 100.0))));
            }
        }
        return all.GroupBy(x => x.Drop.ItemId).Select(x =>
        {
            var d = x.First().Drop;
            bool blocked = d.CardOdds == 0 && prospection < d.MinProspection;
            double expected = x.Sum(y => y.Chance) / 100;
            if (d.MaxPerFight > 0) expected = Math.Min(d.MaxPerFight, expected);
            var price = DropPrice(d.ItemId);
            return new GroupDrop(d.ItemId, x.Max(y => y.Chance), x.Count(), blocked || d.Conditional ? 0 : expected * (price ?? 0),
                price, d.MinProspection, d.MaxPerFight, blocked, d.Conditional);
        }).OrderBy(d => d.Conditional).ThenBy(d => d.Blocked)
            .ThenByDescending(d => d.Value).ThenByDescending(d => d.Chance).ToList();
    }

    /// <summary>Groupes dont le tableau des drops est déplié (la grille est reconstruite à chaque rafraîchissement).</summary>
    readonly HashSet<long> _expandedGroups = new();

    void HuntDropsToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HuntGroupRow row) return;
        if (!_expandedGroups.Remove(row.Id)) _expandedGroups.Add(row.Id);
        RefreshHunt();
    }

    DropDetailRow DetailRow(GroupDrop d) => new(d.ItemId, _data!.GetItem(d.ItemId).Name,
        d.Conditional ? "—" : Percent(d.Chance), d.Count > 1 ? $"×{d.Count}" : "1",
        d.MinProspection > 0 ? d.MinProspection.ToString("N0") : "—",
        d.MaxPerFight > 0 ? d.MaxPerFight.ToString() : "∞",
        d.Price is { } p ? p.ToString("N0") : "?",
        d.Blocked || d.Conditional ? "—" : d.Price != null ? d.Value.ToString("N0") : "sans prix",
        d.Conditional ? "Sous condition" : d.Blocked ? $"🔒 PP {d.MinProspection:N0}" : "OK",
        d.Blocked || d.Conditional);

    /// <summary>Prix unitaire d'un drop : HDV, sinon prix marchand (cartes TTG) de la période, sinon le dernier relevé.</summary>
    double? DropPrice(int itemId)
    {
        if (UnitPrice(itemId) is { } hdv) return hdv;
        var m = _merchants?.Prices.GetValueOrDefault(itemId);
        return m?.Recent(TtgDays).Select(s => (double?)s.Price).Min() ?? m?.Last?.Price;
    }

    string DropLabel(GroupDrop d) =>
        $"{_data!.GetItem(d.ItemId).Name} {Percent(d.Chance)}" + (d.Count > 1 ? $" ×{d.Count}" : "");

    static string Percent(double chance) => chance >= 1 ? $"{chance:0.#} %" : $"{chance:0.###} %";

    /// <summary>Position et zone d'une map (identifiant reçu du jeu).</summary>
    string MapText(string mapId) => int.TryParse(mapId, out var id) ? _data!.MapLabel(id) : $"map {mapId}";

    void RefreshHunt()
    {
        if (!_ready || _data == null || _hunt == null) return;
        _hunt.Prune();
        var learned = Learn();
        var labels = _hunt.Characters.Select(CharacterLabel).Order().ToList();
        if (!HuntCharacterBox.Items.OfType<ComboBoxItem>().Skip(1).Select(i => i.Content as string).Order().SequenceEqual(labels)
            // Choix changé par le tracker (début de tour d'un autre personnage) : la liste suit
            || (HuntCharacterBox.SelectedItem as ComboBoxItem)?.Tag as string != _hunt.Pinned)
            UpdateHuntCharacters();

        var me = _hunt.Shown;
        // Mode auto : la liste montre le personnage suivi (celui qui joue ou qui vient de changer de map)
        if (HuntCharacterBox.Items.Count > 0 && HuntCharacterBox.Items[0] is ComboBoxItem auto)
            auto.Content = me != null && _hunt.Pinned == null ? $"Auto · {CharacterLabel(me)}" : "Fenêtre active (auto)";
        int wisdom = me?.Wisdom ?? 0;
        // Prospection inconnue (pas encore envoyée par le jeu) : celle de base d'un personnage
        int prospection = me?.GroupProspection ?? 100;
        HuntLevel.Text = me?.Level?.ToString() ?? "?";
        HuntGroupLevel.Text = me?.GroupLevel?.ToString() ?? "?";
        HuntGroupLevel.ToolTip = me?.Party.Count > 0
            ? $"Somme des {me.Party.Count} membres du groupe : " + string.Join(" + ", me.Party.Values.Select(p => p.Level))
            : "Pas de groupe : niveau du personnage seul";
        HuntWisdom.Text = me?.Wisdom?.ToString("N0") ?? "?";
        HuntProspection.Text = me?.Prospection?.ToString("N0") ?? "?";
        HuntGroupProspection.Text = me?.GroupProspection?.ToString("N0") ?? "?";
        HuntGroupProspection.ToolTip = me?.Party.Count > 0
            ? $"Somme des {me.Party.Count} membres du groupe : " + string.Join(" + ", me.Party.Values.Select(p => p.Prospection))
            : "Pas de groupe : prospection du personnage seul";

        // Groupes de la map. XP par niveau de monstre, en moyenne sur les monstres déjà combattus (repli pour les autres)
        var known = learned.Values.Where(l => l.Xp != null && l.LevelSum > 0).ToList();
        double? xpPerLevel = known.Count > 0 ? known.Sum(l => l.XpSum) / known.Sum(l => l.LevelSum) : null;
        var groups = (me?.Groups.Values ?? Enumerable.Empty<MapGroup>()).OrderByDescending(g => g.Monsters.Sum(m => m.Level)).Select(g =>
        {
            var names = string.Join(", ", g.Monsters.GroupBy(m => m.Id).Select(x =>
                (x.Count() > 1 ? $"{x.Count()}× " : "") + _data.GetMonster(x.Key).Name
                + " (" + string.Join("/", x.Select(m => m.Level)) + ")"));
            double factor = (1 + wisdom / 100.0) * (1 + g.Bonus / 100.0);
            // XP de base d'un monstre : monstres.json s'il est fourni, sinon apprise des combats ; à défaut,
            // extrapolée de l'XP par niveau des monstres déjà appris (« ≈ »)
            double? Known(GroupMonster m) => _data.BaseXp(m.Id, m.Level) ?? learned.GetValueOrDefault(m.Id)?.Xp;
            bool allKnown = g.Monsters.All(m => Known(m) != null);
            double? xp = allKnown || xpPerLevel != null
                ? g.Monsters.Sum(m => Known(m) ?? xpPerLevel!.Value * Math.Max(1, m.Level)) * factor : null;
            string xpText = xp is { } x ? (allKnown ? "" : "≈ ") + x.ToString("N0") : "à apprendre";

            // Drops : pour chaque objet, la plus forte fréquence parmi les monstres du groupe
            var drops = g.Monsters.Select(m => m.Id).Distinct()
                .Where(learned.ContainsKey)
                .SelectMany(id => learned[id].Items.Select(kv =>
                    (Item: kv.Key, Chance: (double)kv.Value.Fights / learned[id].Fights, Qty: (double)kv.Value.Quantity / kv.Value.Fights)))
                .GroupBy(d => d.Item).Select(x => x.MaxBy(d => d.Chance))
                .Select(d => (d.Item, d.Chance, d.Qty, Value: d.Chance * d.Qty * (UnitPrice(d.Item) ?? 0)))
                .OrderByDescending(d => d.Value).ThenByDescending(d => d.Chance).ToList();
            var dropText = drops.Count > 0 ? string.Join(", ", drops.Take(4).Select(d => $"{_data.GetItem(d.Item).Name} {d.Chance:0%}"))
                : g.Monsters.Any(m => learned.ContainsKey(m.Id)) ? "aucun drop vu" : "à apprendre";

            // Drops officiels (fichier de langue) : remplacent l'estimation apprise quand le groupe en a
            var official = OfficialDrops(g, prospection);
            var open = official.Where(d => !d.Blocked && !d.Conditional).ToList();
            int blockedCount = official.Count(d => d.Blocked && !d.Conditional), conditional = official.Count(d => d.Conditional);
            var officialText = official.Count == 0 ? "" : $"{open.Count} drop(s)"
                + (open.Count > 0 ? $" · {DropLabel(open[0])}" : "")
                + (blockedCount > 0 ? $" · {blockedCount} 🔒" : "") + (conditional > 0 ? $" · {conditional} sous condition" : "");
            double? dropValue = official.Count > 0 ? open.Sum(d => d.Value) : drops.Count > 0 ? drops.Sum(d => d.Value) : null;

            var archi = g.Monsters.FirstOrDefault(m => _data.GetMonster(m.Id).IsArchi);
            long? archiPrice = archi != null ? _pda?.Prices.GetValueOrDefault(PdaTracker.Key(archi.Id, PdaTracker.Archi))?.Price : null;
            return new HuntGroupRow(g.SpriteId, names, g.Monsters.Count, g.Monsters.Sum(m => m.Level),
                g.Bonus > 0 ? $"+{g.Bonus} %" : "", xp, xpText, dropText, officialText,
                official.Select(DetailRow).ToList(), _expandedGroups.Contains(g.SpriteId), dropValue,
                archi != null, archi != null ? _data.GetMonster(archi.Id).Name : "", archiPrice);
        }).ToList();
        SetRows(HuntGroupGrid, groups);
        HuntMap.Text = me?.MapId.Length > 0
            ? $"{me.Label} · {MapText(me.MapId)} · {groups.Count} groupe(s)"
            : "Map inconnue : changez de map en jeu";
        HuntEmpty.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!groups.Any(g => g.HasArchi)) HuntStatus.Text = "";

        // Archi-monstres aperçus, avec leur dernier prix de pierre relevé
        SetRows(HuntArchiGrid, _hunt.Archis.Select(a =>
        {
            var price = _pda?.Prices.GetValueOrDefault(PdaTracker.Key(a.MonsterId, PdaTracker.Archi));
            return new ArchiRow(Ago(a.Time), a.Time, _data.GetMonster(a.MonsterId).Name, a.Level, MapText(a.MapId),
                price?.Price, price != null ? Ago(price.SeenAt) : "", CarteTargetOf(a.MapId, _data.GetMonster(a.MonsterId).Name));
        }).ToList());

        // Combats et rentabilité de la session
        double Value(FightRecord f) => f.Drops.Sum(d => d.Quantity * (UnitPrice(d.ItemId) ?? 0));
        // Session du personnage affiché ; en multi-compte, le total de tous les personnages est rappelé
        var allSession = _hunt.Fights.Where(f => f.Time >= _hunt.SessionStart).ToList();
        var session = allSession.Where(f => f.Character == (me?.Name ?? "")).ToList();
        var fightRows = session.AsEnumerable().Reverse().Take(100).Select(f => new FightRow(
            f.Time.ToString("HH:mm"), f.Time, $"{f.DurationSeconds / 60}:{f.DurationSeconds % 60:00}",
            string.Join(", ", f.Monsters.GroupBy(m => m.Id).Select(x => (x.Count() > 1 ? $"{x.Count()}× " : "") + _data.GetMonster(x.Key).Name)),
            f.Xp, f.Kamas,
            string.Join(", ", f.Drops.Select(d => $"{d.Quantity}× {_data.GetItem(d.ItemId).Name}")), Value(f))).ToList();
        // Combat choisi (détail de ses drops) : gardé d'un rafraîchissement à l'autre
        _huntFightsUpdating = true;
        SetRows(HuntFightGrid, fightRows);
        HuntFightGrid.SelectedItem = fightRows.FirstOrDefault(r => r.TimeSort == _huntSelectedFight);
        _huntFightsUpdating = false;
        _huntSessionFights = session;
        ShowHuntDrops();

        var minutes = Math.Max(1, (DateTime.Now - _hunt.SessionStart).TotalMinutes);
        double xpTotal = session.Sum(f => f.Xp), kamasTotal = session.Sum(f => f.Kamas), dropsTotal = session.Sum(Value);
        int unpriced = session.SelectMany(f => f.Drops).Select(d => d.ItemId).Distinct().Count(id => UnitPrice(id) == null);
        HuntSession.Text = $"Session depuis {_hunt.SessionStart:HH:mm} ({minutes:0} min) · {session.Count} combat(s) · "
            + $"{xpTotal:N0} XP · {kamasTotal:N0} kamas · drops {Kamas(dropsTotal)}"
            + (unpriced > 0 ? $" ({unpriced} objet(s) sans prix HDV, comptés 0)" : "")
            + (allSession.Select(f => f.Character).Distinct().Count() > 1
                ? $"\nTous les personnages : {allSession.Count} combat(s) · {allSession.Sum(f => f.Kamas):N0} kamas · drops {Kamas(allSession.Sum(Value))}"
                : "");
        HuntProjection.ItemsSource = session.Count == 0 ? null : new[] { 30, 60, 120 }.Select(span =>
        {
            double k = span / minutes;
            return new ProjectionRow($"{span} min", xpTotal * k, kamasTotal * k, dropsTotal * k, (kamasTotal + dropsTotal) * k);
        }).ToList();
        HuntLearned.Text = $"Estimations apprises de {_hunt.Fights.Count(f => f.Monsters.Count > 0)} combat(s) "
            + $"({learned.Count} monstre(s) connus). Sagesse et étoiles du groupe appliquées à l'XP.";

        ShowEconome(me);
        UpdateOverlay();
    }

    /// <summary>
    /// Économe (challenge : chaque sort une seule fois par combat) : sorts encore disponibles et sorts déjà lancés.
    /// Simple affichage : c'est toujours le joueur qui choisit et lance ses sorts (README, règle 3).
    /// </summary>
    void ShowEconome(HuntCharacter? me)
    {
        if (me == null)
        {
            EconomeStatus.Text = "Aucun personnage suivi pour l'instant.";
            EconomeSpells.ItemsSource = null;
            return;
        }
        var used = me.UsedSpells.ToHashSet();
        var twice = me.UsedSpells.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        // L'arme (corps-à-corps) compte comme un sort : une seule attaque par combat
        var spells = _hunt!.SpellsOf(me).Prepend(HuntTracker.Weapon).Concat(me.UsedSpells).Distinct().ToList();
        EconomeSpells.ItemsSource = spells.Select(id => new EconomeSpell(id,
            id == HuntTracker.Weapon ? "Arme (corps-à-corps)"
            : _data!.Spells.GetValueOrDefault(id) is { Length: > 0 } n ? n : $"Sort {id}",
            twice.Contains(id) ? "Lancé 2 fois !" : used.Contains(id) ? "Déjà lancé" : "Disponible",
            twice.Contains(id) ? -1 : used.Contains(id) ? 0 : 1,
            _data!.SpellLooks.GetValueOrDefault(id))).ToList();

        int left = spells.Count(id => !used.Contains(id));
        EconomeStatus.Text = spells.Count == 0
            ? "Sorts inconnus : ils sont reçus à la connexion du personnage (lancez l'application avant)."
            : (me.InFight ? "En combat · " : "Dernier combat · ")
              + $"{used.Count} sort(s) lancé(s), {left} disponible(s)"
              + (twice.Count > 0 ? $" · ⚠ challenge raté ({twice.Count} sort(s) relancé(s))" : "");
    }

    void EconomeReset_Click(object sender, RoutedEventArgs e) => _hunt?.ResetEconome();
}

/// <summary>Sort de la partie Économe. State : 1 disponible, 0 déjà lancé, -1 lancé plusieurs fois (challenge raté).</summary>
public sealed record EconomeSpell(int Id, string Name, string Status, int State, SpellLook? Look)
{
    public System.Windows.Media.ImageSource? Icon => SpellIcons.Get(Id, Look);
    public string Tip => $"{Name} : {Status}";
}
