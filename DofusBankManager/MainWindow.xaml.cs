using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DofusBankManager.Data;
using DofusBankManager.Net;

namespace DofusBankManager;

public sealed record RecipeRow(Recipe Recipe, string Name, int Level, string Type, string Jobs,
    int Craftable, int MissingKinds, string MissingText, bool UsesOwned)
{
    public int Index { get; init; }
}

public sealed record IngredientRow(int ItemId, string Name, string Ratio, Brush Brush, string Note);

public sealed record FmItemOption(long Uid, string Label)
{
    public override string ToString() => Label; // texte affiché dans la liste déroulante
}

public sealed record FmHistoryRow(int Index, string Time, int RuneId, string Rune, string Outcome, Brush OutcomeBrush,
    string Gains, string Losses, string Weight, string Reliquat);

public sealed record HdvRow(int Index, int ItemId, string Name, int Level, string Type,
    long? Lot1, long? Lot10, long? Lot100, double? Unit, long? Average, string Seen, DateTime SeenAt);

public sealed record HdvHistoryRow(string When, string Prices);

public sealed record BankRow(int ItemId, string Name, int Quantity, int Level, string Type, int UsedIn)
{
    public int Index { get; init; }
    /// <summary>Valeur estimée à l'HDV (quantité × meilleur prix unitaire relevé).</summary>
    public long? Value { get; init; }
}

public partial class MainWindow : Window
{
    const string AllJobs = "Tous les métiers";
    static readonly string PacketLogPath = Path.Combine(GameData.AppDir, "packets.log");

    readonly PacketSniffer _sniffer = new();
    readonly DofusTracker _tracker = new();
    GameData? _data;
    List<RecipeRow> _rows = new();
    Dictionary<int, int> _usage = new();
    readonly bool _isAdmin;
    bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        _sniffer.Log += m => Ui(() => AddLog(m));
        _sniffer.MessageReceived += OnMessage;
        _sniffer.StreamClassified += OnStreamClassified;
        _tracker.Log += m => Ui(() => AddLog(m));
        _tracker.BankChanged += () => Ui(Refresh);
        _tracker.InventoryChanged += () => Ui(() => { if (IncludeInventory.IsChecked == true) Refresh(); });

        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) => { _sniffer.Dispose(); ThemeManager.Changed -= OnThemeChanged; };
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        ThemeManager.Changed += OnThemeChanged;
        UpdateThemeUi();
        VersionText.Text = "v" + UpdateChecker.CurrentText;
    }

    // --- Mise à jour ---

    UpdateInfo? _update;

    async Task CheckUpdatesAsync()
    {
        try
        {
            _update = await UpdateChecker.CheckAsync();
            if (_update == null)
            {
                AddLog($"Version {UpdateChecker.CurrentText} : à jour.");
                return;
            }
            UpdateTitle.Text = $"Nouvelle version {_update.Latest} disponible (vous avez la {UpdateChecker.CurrentText})";
            UpdateBanner.Visibility = Visibility.Visible;
            AddLog($"Mise à jour disponible : {_update.Latest} ({_update.PageUrl}).");
        }
        catch (Exception ex)
        {
            AddLog($"Vérification de mise à jour impossible : {ex.Message}");
        }
    }

    void UpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        try { UpdateChecker.OpenInBrowser(_update?.PageUrl ?? UpdateChecker.ReleasesPage); }
        catch (Exception ex) { AddLog("Ouverture du navigateur impossible : " + ex.Message); }
    }

    void UpdateLater_Click(object sender, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;

    // --- Forgemagie ---

    Fm.FmTracker? _fm;
    bool _fmUpdating;

    void InitFm(GameData data)
    {
        _fm = new Fm.FmTracker(data);
        _fm.Log += AddLog;
        _fm.Changed += RefreshFm;
        _tracker.CraftOpened += _fm.OnCraftOpened;
        _tracker.CraftItemSeen += _fm.OnCraftItemSeen;
        _tracker.CraftInfo += _fm.OnCraftInfo;
        _tracker.CraftItemConsumed += (uid, tpl, count) => _fm.OnItemConsumed(uid, tpl, count);

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => _fm.Tick();
        timer.Start();
        RefreshFm();
    }

    void RefreshFm()
    {
        if (_fm == null || _data == null) return;
        _fmUpdating = true;
        var options = _fm.Items.Values.OrderByDescending(i => i.LastUsed)
            .Select(i => new FmItemOption(i.Uid, $"{_data.GetItem(i.TemplateId).Name}  ·  {i.History.Count} rune(s)"))
            .ToList();
        FmItemCombo.ItemsSource = options;
        FmItemCombo.SelectedItem = options.FirstOrDefault(o => o.Uid == _fm.CurrentUid);
        _fmUpdating = false;

        var item = _fm.CurrentUid is { } uid ? _fm.Items.GetValueOrDefault(uid) : null;
        FmEmpty.Visibility = item == null || item.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (item == null)
        {
            FmItemName.Text = "Aucun objet";
            FmItemIcon.Source = null;
            FmReliquat.Text = "0";
            FmStats.ItemsSource = null;
            FmHistoryGrid.ItemsSource = null;
            return;
        }

        FmItemName.Text = _data.GetItem(item.TemplateId).Name;
        FmItemIcon.Source = ItemIcons.Get(item.TemplateId);
        FmReliquat.Text = item.Reliquat.ToString("0.##");
        FmStats.ItemsSource = _fm.ParseStats(item.Effects)
            .Where(kv => kv.Value != 0)
            .Select(kv => _fm.Describe(kv.Key, kv.Value))
            .ToList();

        string Changes(IEnumerable<Fm.FmStatChange> changes) =>
            string.Join(", ", changes.Select(c => _fm.Describe(c.Stat, c.Delta)));

        FmHistoryGrid.ItemsSource = item.History
            .Select((ev, i) => new FmHistoryRow(
                i + 1,
                ev.Time.ToString("HH:mm:ss"),
                ev.RuneTemplateId ?? 0,
                ev.RuneTemplateId is { } r ? _data.GetItem(r).Name : "?",
                ev.Outcome,
                (Brush)FindResource(ev.Outcome switch
                {
                    Fm.FmTracker.SuccesCritique => "Ok",
                    Fm.FmTracker.SuccesNeutre => "Warning",
                    _ => "Missing",
                }),
                Changes(ev.Changes.Where(c => c.Delta > 0)),
                Changes(ev.Changes.Where(c => c.Delta < 0)),
                $"+{ev.WeightGained:0.##} / -{ev.WeightLost:0.##}",
                ev.ReliquatAfter.ToString("0.##")))
            .Reverse() // le plus récent en haut
            .ToList();
    }

    void FmItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fmUpdating || _fm == null || FmItemCombo.SelectedItem is not FmItemOption o) return;
        _fm.Select(o.Uid);
        RefreshFm();
    }

    void FmSetReliquat_Click(object sender, RoutedEventArgs e)
    {
        if (_fm?.CurrentUid is not { } uid) return;
        var text = FmReliquatInput.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            AddLog("Reliquat invalide : saisissez un nombre.");
            return;
        }
        _fm.SetReliquat(uid, value);
        FmReliquatInput.Text = "";
    }

    void FmClear_Click(object sender, RoutedEventArgs e)
    {
        if (_fm?.CurrentUid is not { } uid) return;
        if (MessageBox.Show(this, "Effacer l'historique de cet objet et remettre son reliquat à 0 ?", "Forgemagie",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _fm.ClearHistory(uid);
    }

    void FmForget_Click(object sender, RoutedEventArgs e)
    {
        if (_fm?.CurrentUid is not { } uid) return;
        if (MessageBox.Show(this, "Retirer cet objet et tout son historique de la liste ?", "Forgemagie",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _fm.Forget(uid);
    }

    void FmWeights_Click(object sender, RoutedEventArgs e)
    {
        try { UpdateChecker.OpenInBrowser(Fm.FmTracker.WeightsPath); }
        catch (Exception ex) { AddLog("Ouverture impossible : " + ex.Message); }
    }

    // --- Thème ---

    void ThemeButton_Click(object sender, RoutedEventArgs e) => ThemePopup.IsOpen = !ThemePopup.IsOpen;

    void ThemeOption_Click(object sender, RoutedEventArgs e)
    {
        ThemePopup.IsOpen = false;
        ThemeManager.Apply(sender == ThemeJour ? ThemeManager.Jour
            : sender == ThemeNuit ? ThemeManager.Nuit : ThemeManager.Crepuscule);
    }

    void OnThemeChanged()
    {
        UpdateThemeUi();
        RefreshFm();
        // Les couleurs des ingrédients sont calculées à l'affichage : on les recalcule
        RecipeGrid_SelectionChanged(RecipeGrid, null!);
    }

    void UpdateThemeUi()
    {
        var current = ThemeManager.Current;
        ThemeJour.Tag = current == ThemeManager.Jour ? "selected" : "";
        ThemeCrepuscule.Tag = current == ThemeManager.Crepuscule ? "selected" : "";
        ThemeNuit.Tag = current == ThemeManager.Nuit ? "selected" : "";
        ThemeButtonIcon.Data = (System.Windows.Media.Geometry)FindResource("Icon" + current);
    }

    async Task InitAsync()
    {
        UpdateCaptureUi();
        _ = CheckUpdatesAsync();
        try
        {
            AddLog("Chargement des données du jeu…");
            _data = await Task.Run(() => GameData.LoadAsync(m => Ui(() => AddLog(m))));
        }
        catch (Exception ex)
        {
            AddLog("ERREUR : " + ex.Message);
            MessageBox.Show(this, "Impossible de charger les données du jeu :\n" + ex.Message, "Dofus Bank Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ItemIcons.Data = _data;
        InitFm(_data);
        InitHdv(_data);
        _usage = _data.Recipes.SelectMany(r => r.Ingredients.Select(i => i.ItemId).Distinct())
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        JobFilter.Items.Add(AllJobs);
        foreach (var j in _data.JobNames) JobFilter.Items.Add(j);
        JobFilter.SelectedIndex = 0;

        _ready = true;
        _tracker.LoadLatest();
        Refresh();

        if (_isAdmin) StartCapture();
    }

    // --- Capture réseau ---

    void OnMessage(string stream, string msg)
    {
        // Seuls les messages de banque/inventaire sont journalisés (voir README, règle 4)
        if (LogPacketsEnabled && DofusTracker.IsLoggable(msg))
        {
            try { File.AppendAllText(PacketLogPath, $"{DateTime.Now:HH:mm:ss.fff} {(msg.Length > 2000 ? msg[..2000] + "…" : msg)}\n"); }
            catch { }
        }
        // Le suivi de l'état se fait sur le thread de l'interface pour éviter tout accès concurrent
        Ui(() =>
        {
            if (_streamsWithMessages.Add(stream))
                AddLog($"Messages texte reçus sur {stream}.");
            try { _tracker.Handle(msg); }
            catch (Exception ex) { AddLog($"Paquet ignoré ({ex.Message}) : {msg[..Math.Min(80, msg.Length)]}"); }
        });
    }

    /// <summary>Diagnostic : indique si les connexions du client Dofus sont lisibles (texte) ou chiffrées.</summary>
    void OnStreamClassified(string server, bool isText, byte firstByte)
    {
        if (!DofusConnections.RemoteEndpoints().Contains(server)) return;
        Ui(() => AddLog(isText
            ? $"Connexion Dofus {server} : flux texte lisible."
            : $"Connexion Dofus {server} : flux illisible (premier octet 0x{firstByte:X2}{(firstByte == 0x16 || firstByte == 0x17 ? ", chiffrement TLS" : "")})."));
    }

    void LogDofusConnections()
    {
        var eps = DofusConnections.RemoteEndpoints();
        AddLog(eps.Count == 0
            ? "Client Dofus Retro introuvable (aucune connexion)."
            : "Connexions du client Dofus Retro : " + string.Join(", ", eps));
    }

    readonly HashSet<string> _streamsWithMessages = new();

    volatile bool _logPackets;
    bool LogPacketsEnabled => _logPackets;

    void StartCapture()
    {
        try
        {
            AddLog(FirewallRule.Ensure());
            _sniffer.Start();
            AddLog("Capture démarrée. Ouvrez votre banque en jeu pour la synchroniser.");
            LogDofusConnections();
        }
        catch (Exception ex)
        {
            AddLog("Capture impossible : " + ex.Message);
        }
        UpdateCaptureUi();
    }

    void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sniffer.IsRunning) { _sniffer.Stop(); AddLog("Capture arrêtée."); UpdateCaptureUi(); }
        else StartCapture();
    }

    void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Exception ex) { AddLog("Élévation annulée : " + ex.Message); }
    }

    void UpdateCaptureUi()
    {
        bool running = _sniffer.IsRunning;
        CaptureDot.Fill = running ? Brushes.LimeGreen : (_isAdmin ? Brushes.Gray : Brushes.OrangeRed);
        CaptureStatus.Text = running ? "Capture active — en écoute du trafic Dofus"
            : _isAdmin ? "Capture arrêtée" : "Droits administrateur requis pour lire le trafic réseau";
        CaptureButton.Content = running ? "Arrêter la capture" : "Démarrer la capture";
        CaptureButton.IsEnabled = _isAdmin;
        AdminButton.Visibility = _isAdmin ? Visibility.Collapsed : Visibility.Visible;
    }

    void LogPacketsBox_Changed(object sender, RoutedEventArgs e)
    {
        _logPackets = LogPacketsBox.IsChecked == true;
        if (_logPackets) AddLog($"Journalisation des paquets dans {PacketLogPath}");
    }

    // --- Calcul des recettes ---

    Dictionary<int, int> OwnedQuantities()
    {
        var s = _tracker.State;
        IEnumerable<OwnedItem> items = s.Bank.Values;
        if (IncludeInventory.IsChecked == true) items = items.Concat(s.Inventory.Values);
        return items.GroupBy(i => i.TemplateId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
    }

    void Refresh()
    {
        if (!_ready || _data == null) return;
        var s = _tracker.State;
        BankStatus.Text = s.Bank.Count == 0
            ? "Aucune banque reçue — ouvrez votre banque en jeu."
            : $"{(s.Character.Length > 0 ? s.Character + " — " : "")}Banque : {s.Bank.Count} objets ({s.Bank.Values.Sum(i => (long)i.Quantity):N0} unités), "
              + $"{s.BankKamas:N0} kamas — mise à jour le {s.BankUpdatedAt:dd/MM/yyyy HH:mm:ss}";

        var owned = OwnedQuantities();
        _rows = _data.Recipes.Select(r =>
        {
            var it = _data.GetItem(r.ResultId);
            int craftable = r.Ingredients.Min(i => owned.GetValueOrDefault(i.ItemId) / i.Quantity);
            var missing = r.Ingredients.Where(i => owned.GetValueOrDefault(i.ItemId) < i.Quantity).ToList();
            var missingText = string.Join(", ", missing.Select(i =>
                $"{i.Quantity - owned.GetValueOrDefault(i.ItemId)}× {_data.GetItem(i.ItemId).Name}"));
            return new RecipeRow(r, it.Name, it.Level, it.TypeName, string.Join(", ", r.Jobs),
                craftable, missing.Count, missingText, r.Ingredients.Any(i => owned.ContainsKey(i.ItemId)));
        }).ToList();

        ApplyRecipeFilters();
        ApplyBankFilter();
    }

    void ApplyRecipeFilters()
    {
        if (!_ready) return;
        var search = RecipeSearch.Text.Trim();
        var job = JobFilter.SelectedItem as string;
        int min = int.TryParse(LevelMin.Text, out var a) ? a : 0;
        int max = int.TryParse(LevelMax.Text, out var b) ? b : int.MaxValue;

        IEnumerable<RecipeRow> q = _rows;
        q = ModeFilter.SelectedIndex switch
        {
            0 => q.Where(r => r.Craftable > 0),
            1 => q.Where(r => r.MissingKinds <= 1),
            2 => q.Where(r => r.MissingKinds <= 2),
            3 => q.Where(r => r.UsesOwned),
            _ => q
        };
        if (job != null && job != AllJobs) q = q.Where(r => r.Recipe.Jobs.Contains(job));
        q = q.Where(r => r.Level >= min && r.Level <= max);
        if (search.Length > 0)
            q = q.Where(r => Contains(r.Name, search) || Contains(r.Type, search)
                || r.Recipe.Ingredients.Any(i => Contains(_data!.GetItem(i.ItemId).Name, search)));

        var list = q.OrderByDescending(r => r.Craftable > 0).ThenBy(r => r.MissingKinds)
            .ThenByDescending(r => r.Level).ThenBy(r => r.Name)
            .Select((r, i) => r with { Index = i + 1 }).ToList();
        RecipeGrid.ItemsSource = list;
        RecipeCount.Text = $"{list.Count} recette(s)";
    }

    void ApplyBankFilter()
    {
        if (!_ready || _data == null) return;
        var search = BankSearch.Text.Trim();
        var rows = _tracker.State.Bank.Values
            .GroupBy(i => i.TemplateId)
            .Select(g =>
            {
                var it = _data.GetItem(g.Key);
                int qty = g.Sum(i => i.Quantity);
                var unit = _hdv?.Prices.GetValueOrDefault(g.Key)?.UnitPrice;
                return new BankRow(g.Key, it.Name, qty, it.Level, it.TypeName, _usage.GetValueOrDefault(g.Key))
                    { Value = unit is { } u ? (long)Math.Round(u * qty) : null };
            })
            .Where(r => search.Length == 0 || Contains(r.Name, search) || Contains(r.Type, search))
            .OrderBy(r => r.Type).ThenBy(r => r.Name)
            .Select((r, i) => r with { Index = i + 1 })
            .ToList();
        BankGrid.ItemsSource = rows;
        BankCount.Text = $"{rows.Count} objet(s) distinct(s)";

        int priced = rows.Count(r => r.Value != null);
        BankValue.Text = priced == 0 ? ""
            : $"Valeur estimée : {rows.Sum(r => r.Value ?? 0):N0} kamas ({priced}/{rows.Count} objets avec un prix)";
    }

    // --- Hôtel de vente ---

    const string AllTypes = "Tous les types";
    Hdv.HdvTracker? _hdv;

    void InitHdv(GameData data)
    {
        _hdv = new Hdv.HdvTracker(data);
        _hdv.Log += AddLog;
        _hdv.Changed += () => { ApplyHdvFilters(); ApplyBankFilter(); };
        _tracker.HdvOffers += _hdv.OnOffers;
        _tracker.HdvAverage += _hdv.OnAverage;
        _tracker.HdvOfferMove += _hdv.OnOfferMove;

        _sync = new Hdv.HdvSync(_hdv);
        _sync.Log += AddLog;
        _sync.StatusChanged += () => ShareStatus.Text = _sync.Status;
        ShareEnabled.IsChecked = _sync.Settings.Enabled;
        ShareUrl.Text = _sync.Settings.ServerUrl;
        ShareKey.Text = _sync.Settings.GroupKey;
        ShareStatus.Text = _sync.Status;
        var syncTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        syncTimer.Tick += async (_, _) => await _sync.TickAsync();
        syncTimer.Start();
        _ = _sync.TickAsync();

        HdvTypeFilter.Items.Add(AllTypes);
        foreach (var t in data.Items.Values.Select(i => i.TypeName).Distinct().Order()) HdvTypeFilter.Items.Add(t);
        HdvTypeFilter.SelectedIndex = 0;
        ApplyHdvFilters();
    }

    void ApplyHdvFilters()
    {
        if (_hdv == null || _data == null || HdvTypeFilter.SelectedItem == null) return;
        var search = HdvSearch.Text.Trim();
        var type = HdvTypeFilter.SelectedItem as string;
        var selected = (HdvGrid.SelectedItem as HdvRow)?.ItemId;

        IEnumerable<int> ids = HdvShowAll.IsChecked == true ? _data.Items.Keys : _hdv.Prices.Keys;
        var rows = ids
            .Select(id => (Item: _data.GetItem(id), Price: _hdv.Prices.GetValueOrDefault(id)))
            .Where(x => type == AllTypes || x.Item.TypeName == type)
            .Where(x => search.Length == 0 || Contains(x.Item.Name, search))
            .OrderByDescending(x => x.Price?.SeenAt ?? DateTime.MinValue).ThenBy(x => x.Item.Name)
            .Select((x, i) => new HdvRow(i + 1, x.Item.Id, x.Item.Name, x.Item.Level, x.Item.TypeName,
                x.Price?.Lot1, x.Price?.Lot10, x.Price?.Lot100, x.Price?.UnitPrice, x.Price?.Average,
                x.Price is { } p ? Ago(p.SeenAt) + (p.Shared ? " · groupe" : "") : "", x.Price?.SeenAt ?? DateTime.MinValue))
            .ToList();

        HdvGrid.ItemsSource = rows;
        HdvGrid.SelectedItem = rows.FirstOrDefault(r => r.ItemId == selected);
        HdvCount.Text = $"{rows.Count} objet(s) · {_hdv.Prices.Count} prix relevé(s)";
        HdvEmpty.Visibility = _hdv.Prices.Count == 0 && HdvShowAll.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
    }

    void HdvFilters_Changed(object sender, RoutedEventArgs e) => ApplyHdvFilters();

    Hdv.HdvSync? _sync;

    void ShareToggle_Click(object sender, RoutedEventArgs e) =>
        SharePanel.Visibility = SharePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    async void ShareSync_Click(object sender, RoutedEventArgs e)
    {
        if (_sync == null) return;
        if (!_sync.Settings.Enabled) { ShareStatus.Text = "Activez et enregistrez le partage d'abord."; return; }
        await _sync.SyncNowAsync();
    }

    async void ShareTest_Click(object sender, RoutedEventArgs e)
    {
        if (_sync == null) return;
        ShareStatus.Text = "Test en cours…";
        ShareStatus.Text = await _sync.TestAsync(ShareUrl.Text, ShareKey.Text);
    }

    async void ShareSave_Click(object sender, RoutedEventArgs e)
    {
        if (_sync == null) return;
        if (ShareEnabled.IsChecked == true && (ShareUrl.Text.Trim().Length == 0 || ShareKey.Text.Trim().Length == 0))
        {
            ShareStatus.Text = "Renseignez l'adresse du serveur et le code de groupe.";
            return;
        }
        _sync.Configure(ShareEnabled.IsChecked == true, ShareUrl.Text, ShareKey.Text);
        await _sync.TickAsync();
    }

    void HdvGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_hdv == null || _data == null || HdvGrid.SelectedItem is not HdvRow row) return;
        var price = _hdv.Prices.GetValueOrDefault(row.ItemId);
        HdvDetailTitle.Text = row.Name;
        HdvDetailIcon.Source = ItemIcons.Get(row.ItemId);
        HdvDetailSub.Text = price == null
            ? "Pas encore de prix relevé : consultez cet objet dans un hôtel de vente en jeu."
            : $"Prix unitaire : {Kamas(price.UnitPrice)} · moyen : {Kamas(price.Average)}\n{price.Offers} lot(s) en vente au dernier relevé ({Ago(price.SeenAt)})";
        HdvHistory.ItemsSource = price?.History.AsEnumerable().Reverse()
            .Select(s => new HdvHistoryRow(s.Time.ToString("dd/MM/yyyy HH:mm"),
                $"×1 {Kamas(s.Lot1)} · ×10 {Kamas(s.Lot10)} · ×100 {Kamas(s.Lot100)}"))
            .ToList();
    }

    void HdvForget_Click(object sender, RoutedEventArgs e)
    {
        if (HdvGrid.SelectedItem is HdvRow row) _hdv?.Forget(row.ItemId);
    }

    static string Kamas(double? v) => v is { } k ? $"{k:N0} k" : "—";

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "à l'instant"
            : d.TotalHours < 1 ? $"il y a {(int)d.TotalMinutes} min"
            : d.TotalDays < 1 ? $"il y a {(int)d.TotalHours} h"
            : t.ToString("dd/MM/yyyy");
    }

    void RecipeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecipeGrid.SelectedItem is not RecipeRow row || _data == null) return;
        var owned = OwnedQuantities();
        var recipesByResult = _rows.ToLookup(r => r.Recipe.ResultId);

        DetailTitle.Text = row.Name;
        DetailSub.Text = $"Niveau {row.Level} · {row.Type} · {row.Jobs}"
            + (row.Craftable > 0 ? $"\nFaisable {row.Craftable} fois" : $"\nIl manque {row.MissingKinds} ingrédient(s)");
        DetailIngredients.ItemsSource = row.Recipe.Ingredients.Select(i =>
        {
            int have = owned.GetValueOrDefault(i.ItemId);
            bool ok = have >= i.Quantity;
            var note = "";
            if (!ok)
            {
                var sub = recipesByResult[i.ItemId].FirstOrDefault();
                if (sub != null)
                    note = sub.Craftable > 0 ? $"Fabricable avec vos ressources (×{sub.Craftable})" : "Fabricable (ressources manquantes)";
            }
            return new IngredientRow(i.ItemId, $"{i.Quantity}× {_data.GetItem(i.ItemId).Name}", $"{have} / {i.Quantity}",
                ok ? (Brush)FindResource("Ok") : (Brush)FindResource("Missing"), note);
        }).ToList();
    }

    void Filters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender == IncludeInventory) Refresh();
        else ApplyRecipeFilters();
    }

    void BankSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyBankFilter();

    // --- Utilitaires ---

    static bool Contains(string text, string search) =>
        System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, search,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;

    void AddLog(string msg)
    {
        LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\n");
        LogBox.ScrollToEnd();
    }

    void Ui(Action a)
    {
        if (Dispatcher.CheckAccess()) a();
        else Dispatcher.BeginInvoke(a);
    }
}
