using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DofusRetroManager.Data;
using DofusRetroManager.Net;

namespace DofusRetroManager;

public sealed record RecipeRow(Recipe Recipe, string Name, int Level, string Type, string Jobs,
    int Craftable, int MissingKinds, string MissingText, bool UsesOwned)
{
    public int Index { get; init; }
    /// <summary>Nombre de cases de la recette (ingrédients différents).</summary>
    public int Slots => Recipe.Ingredients.Count;

    /// <summary>Coût de fabrication d'après les prix HDV (null si un ingrédient n'a pas de coût connu).</summary>
    public double? Cost { get; init; }
    /// <summary>Prix de vente unitaire de l'objet à l'HDV.</summary>
    public double? Sell { get; init; }
    public double? Profit => Sell - Cost;
    /// <summary>Bénéfice rapporté au coût, en fraction (0,25 = +25 %).</summary>
    public double? Margin => Cost > 0 ? Profit / Cost : null;
    /// <summary>1 si rentable, -1 si à perte, 0 si inconnu : couleur de la colonne Bénéfice.</summary>
    public int ProfitSign => Profit is { } p ? Math.Sign(p) : 0;
}

/// <summary>Ingrédient d'une recette. Children : ingrédients de sa propre recette (sous-recette), quantités totales.</summary>
public sealed record IngredientRow(int ItemId, string Name, string Ratio, Brush Brush, string Note, string Price,
    IReadOnlyList<IngredientRow> Children)
{
    public bool HasChildren => Children.Count > 0;
}

public sealed record FmItemOption(long Uid, string Label)
{
    public override string ToString() => Label; // texte affiché dans la liste déroulante
}

public sealed record FmHistoryRow(int Index, string Time, int RuneId, string Rune, string Outcome, Brush OutcomeBrush,
    string Gains, string Losses, string Weight, string Reliquat);

/// <summary>Ligne de l'écran « Poids des stats » (onglet FM) : rune, stat, poids par point et valeur par défaut.</summary>
public sealed record FmWeightRow(int Stat, int RuneId, string Rune, string Label, string Weight, string Default, bool Modified);

public sealed record HdvRow(int Index, int ItemId, string Name, int Level, string Type,
    long? Lot1, long? Lot10, long? Lot100, double? Unit, long? Average, string Seen, DateTime SeenAt);

public sealed record HdvHistoryRow(string When, string Lot1, string Lot10, string Lot100);

public sealed record BankRow(int ItemId, string Name, int Quantity, int Level, string Type, int UsedIn)
{
    public int Index { get; init; }
    /// <summary>Valeur estimée à l'HDV (quantité × meilleur prix unitaire relevé).</summary>
    public long? Value { get; init; }
    /// <summary>Meilleur prix unitaire relevé à l'HDV (×1, ×10 ÷ 10, ×100 ÷ 100).</summary>
    public double? UnitPrice { get; init; }
    /// <summary>Prix moyen affiché par l'HDV.</summary>
    public long? AveragePrice { get; init; }
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
        KeepColumnWidths(this);
        InitOrganizer();
        InitOverlay();
        _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        _sniffer.Log += m => Ui(() => AddLog(m));
        _sniffer.MessageReceived += OnMessage;
        _sniffer.StreamClassified += OnStreamClassified;
        _tracker.Log += m => Ui(() => AddLog(m));
        _tracker.BankChanged += () => Ui(Refresh);
        _tracker.InventoryChanged += () => Ui(() =>
        {
            UpdateInventoryHint();
            if (IncludeInventory.IsChecked == true) Refresh();
        });

        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) => { _sniffer.Dispose(); ThemeManager.Changed -= OnThemeChanged; };
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        ThemeManager.Changed += OnThemeChanged;
        UpdateThemeUi();
        VersionText.Text = "v" + UpdateChecker.CurrentText;
    }

    // --- Mise à jour ---

    UpdateInfo? _update;
    /// <summary>Version repoussée avec « Plus tard » : la vérification périodique ne la signale plus (une plus récente, si).</summary>
    Version? _updateDismissed;

    /// <summary>Après un échec (GitHub injoignable ou limite de requêtes atteinte), pause des vérifications périodiques.</summary>
    DateTime _updateRetryAt;
    bool _updateChecking;

    /// <summary>
    /// Vérification au lancement, puis toutes les 30 secondes tant que l'application reste ouverte : une version publiée
    /// pendant la session s'affiche sans relancer. Les vérifications périodiques ne lisent que le numéro de la dernière
    /// version (UpdateChecker.LatestVersionAsync, hors API GitHub) ; l'API n'est appelée que pour une version nouvelle.
    /// En cas d'échec, pause de 15 minutes.
    /// </summary>
    void StartUpdateChecks()
    {
        _ = CheckUpdatesAsync();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => { if (DateTime.Now >= _updateRetryAt) _ = CheckUpdatesAsync(periodic: true); };
        timer.Start();
    }

    async Task CheckUpdatesAsync(bool periodic = false)
    {
        // Téléchargement ou installation en cours : le bandeau ne change pas ; une seule vérification à la fois
        if (_download != null || _updateChecking) return;
        _updateChecking = true;
        try
        {
            if (periodic)
            {
                // Rien de nouveau (ou version déjà signalée, ou repoussée) : pas d'appel à l'API
                var latest = await UpdateChecker.LatestVersionAsync();
                if (latest == null || latest <= UpdateChecker.Current || latest <= _update?.Latest || latest <= _updateDismissed) return;
            }
            var update = await UpdateChecker.CheckAsync();
            // Release tout juste créée, installateur pas encore joint : on la reprendra à la vérification suivante
            if (periodic && update is { InstallerUrl: null }) return;
            if (update == null)
            {
                if (!periodic) AddLog($"Version {UpdateChecker.CurrentText} : à jour.");
                return;
            }
            // Déjà signalée, ou repoussée avec « Plus tard » : rien de nouveau
            if (_download != null || update.Latest <= _update?.Latest || update.Latest <= _updateDismissed) return;

            // Nouvelle version (éventuellement plus récente que celle déjà signalée) : bandeau remis à zéro
            _update = update;
            _installerPath = null;
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateText.Text = "L'application télécharge l'installateur puis le lance : il remplace cette version en gardant vos banques et vos prix.";
            UpdateActionButton.Content = "Télécharger et installer";
            UpdateTitle.Text = $"Nouvelle version {_update.Latest} disponible (vous avez la {UpdateChecker.CurrentText})";
            if (_update.InstallerUrl == null)
            {
                // Release sans installateur joint : il ne reste que la page GitHub
                UpdateText.Text = "L'installateur n'est pas joint à cette version : téléchargez-le depuis la page GitHub.";
                UpdateActionButton.Content = "Ouvrir la page";
            }
            else if (UpdateChecker.CanUpdateSilently)
            {
                UpdateText.Text = "L'application télécharge la mise à jour, se ferme le temps de l'installer, puis se rouvre d'elle-même. "
                    + "Rien à accepter ; vos banques et vos prix sont conservés.";
                UpdateActionButton.Content = "Mettre à jour";
            }
            UpdateBanner.Visibility = Visibility.Visible;
            AddLog($"Mise à jour disponible : {_update.Latest} ({_update.PageUrl}).");
        }
        catch (Exception ex)
        {
            // Une ligne par échec, pas toutes les 30 secondes : on réessaie dans 15 minutes
            AddLog($"Vérification de mise à jour impossible : {ex.Message}");
            _updateRetryAt = DateTime.Now.AddMinutes(15);
        }
        finally { _updateChecking = false; }
    }

    /// <summary>Installateur téléchargé et vérifié, prêt à être lancé.</summary>
    string? _installerPath;
    CancellationTokenSource? _download;

    async void UpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null) return;
        if (_update.InstallerUrl == null) { UpdateNotes_Click(sender, e); return; }

        if (_installerPath != null) { InstallUpdate(); return; }

        _download = new CancellationTokenSource();
        UpdateActionButton.IsEnabled = false;
        UpdateLaterButton.Content = "Annuler";
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateText.Text = "Téléchargement de l'installateur…";
        var progress = new Progress<double>(p =>
        {
            UpdateProgressFill.Width = UpdateProgress.ActualWidth * p;
            UpdateText.Text = $"Téléchargement de l'installateur… {p:P0}";
        });
        try
        {
            _installerPath = await UpdateChecker.DownloadAsync(_update, progress, _download.Token);
            UpdateProgressFill.Width = UpdateProgress.ActualWidth;
            AddLog($"Installateur {_update.Latest} téléchargé : {_installerPath}");
            if (UpdateChecker.CanUpdateSilently) { InstallUpdate(); return; }
            // Application lancée hors installation (ex. dossier de développement) : l'assistant demande où installer
            UpdateText.Text = "Téléchargement terminé et vérifié. L'application se fermera pendant l'installation ; "
                + "à la fin, laissez cochée « Lancer Dofus Retro Manager » pour la rouvrir.";
            UpdateActionButton.Content = "Installer maintenant";
        }
        catch (OperationCanceledException)
        {
            UpdateText.Text = "Téléchargement annulé.";
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            UpdateText.Text = "Téléchargement impossible : " + ex.Message + " Réessayez, ou passez par « Nouveautés » pour le télécharger depuis GitHub.";
            UpdateProgress.Visibility = Visibility.Collapsed;
            AddLog("Téléchargement de la mise à jour impossible : " + ex.Message);
        }
        finally
        {
            _download = null;
            UpdateActionButton.IsEnabled = true;
            UpdateLaterButton.Content = "Plus tard";
        }
    }

    void UpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        try { UpdateChecker.OpenInBrowser(_update?.PageUrl ?? UpdateChecker.ReleasesPage); }
        catch (Exception ex) { AddLog("Ouverture du navigateur impossible : " + ex.Message); }
    }

    /// <summary>
    /// Installe l'installateur téléchargé. Application installée : sans assistant, puis relance automatique
    /// (UpdateChecker.StartSilentUpdate) ; sinon, l'assistant habituel. Dans les deux cas l'application se ferme.
    /// </summary>
    void InstallUpdate()
    {
        try
        {
            if (UpdateChecker.CanUpdateSilently)
            {
                UpdateText.Text = "Installation de la mise à jour… L'application va se fermer puis se rouvrir d'elle-même.";
                UpdateChecker.StartSilentUpdate(_installerPath!);
            }
            else UpdateChecker.StartInstaller(_installerPath!);
            AddLog($"Installation de la version {_update!.Latest} lancée.");
            Application.Current.Shutdown();
        }
        catch (Exception ex) { UpdateText.Text = "Impossible de lancer l'installation : " + ex.Message; }
    }

    /// <summary>Relancée après une mise à jour automatique : nouveautés si elle a réussi, explication sinon.</summary>
    void ShowUpdateResult()
    {
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(UpdateChecker.UpdateResultArg));
        if (arg == null) return;
        int.TryParse(arg[UpdateChecker.UpdateResultArg.Length..], out var code);
        if (arg.Length > UpdateChecker.UpdateResultArg.Length && code is 0 or 3010)
        {
            AddLog($"Mise à jour installée : version {UpdateChecker.CurrentText}.");
            UpdateChecker.CleanDownloads();
            ShowChangelog();
            return;
        }
        var reason = code switch
        {
            1602 => "La mise à jour a été annulée.",
            1618 => "Une autre installation Windows était en cours : la mise à jour n'a pas pu se faire.",
            _ => $"La mise à jour a échoué (code {code}). Détails : {UpdateChecker.UpdateLogPath}",
        };
        AddLog(reason);
        MessageBox.Show(this, reason + "\n\nVous gardez la version " + UpdateChecker.CurrentText
            + ". Réessayez depuis le bandeau de mise à jour, ou installez-la depuis la page GitHub.",
            "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    void Changelog_Click(object sender, RoutedEventArgs e) => ShowChangelog();

    void ShowChangelog()
    {
        var open = OwnedWindows.OfType<ChangelogWindow>().FirstOrDefault();
        if (open != null) { open.Activate(); return; }
        new ChangelogWindow { Owner = this }.Show();
    }

    void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        if (_download != null) { _download.Cancel(); return; } // « Annuler » pendant le téléchargement
        UpdateBanner.Visibility = Visibility.Collapsed;
        _updateDismissed = _update?.Latest;
    }

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
        RefreshFmWeights();
        _fmUpdating = true;
        // Plusieurs exemplaires du même objet : ses caractéristiques et la dernière rune les distinguent
        var options = _fm.Items.Values.OrderByDescending(i => i.LastUsed)
            .Select(i =>
            {
                var stats = string.Join(", ", _fm.ParseStats(i.Effects).Where(kv => kv.Value != 0).Take(3)
                    .Select(kv => _fm.Describe(kv.Key, kv.Value)));
                return new FmItemOption(i.Uid, $"{_data.GetItem(i.TemplateId).Name}  ·  {i.History.Count} rune(s)"
                    + (stats.Length > 0 ? $"  ·  {stats}" : "") + $"  ·  {i.LastUsed:dd/MM HH:mm}");
            })
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

        SetRows(FmHistoryGrid, item.History
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
            .ToList());
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
        if (_webConfirmed || MessageBox.Show(this, "Effacer l'historique de cet objet et remettre son reliquat à 0 ?", "Forgemagie",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _fm.ClearHistory(uid);
    }

    void FmForget_Click(object sender, RoutedEventArgs e)
    {
        if (_fm?.CurrentUid is not { } uid) return;
        if (_webConfirmed || MessageBox.Show(this, "Retirer cet objet et tout son historique de la liste ?", "Forgemagie",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _fm.Forget(uid);
    }

    // --- Poids des stats : affichés à la place de l'historique, modifiables, pris en compte tout de suite ---

    void FmWeights_Click(object sender, RoutedEventArgs e)
    {
        bool show = FmWeightsPanel.Visibility != Visibility.Visible;
        FmWeightsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        FmWeights.Content = show ? "Retour à l'historique" : "Poids des stats";
    }

    static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);

    void RefreshFmWeights()
    {
        if (_fm == null || _data == null) return;
        SetRows(FmWeightsGrid, _fm.WeightList().Select(w => new FmWeightRow(w.Stat, w.RuneId,
            w.Rune.Length > 0 ? w.Rune : "—", _data.FormatEffect(w.Stat, 1), Num(w.Weight),
            w.Default is { } d ? Num(d) : "—", w.Default != w.Weight)).ToList());
    }

    /// <summary>Saisie d'un poids : enregistrée en quittant le champ ou avec Entrée ; vide ou invalide : valeur par défaut.</summary>
    void SetFmWeight(int stat, string text)
    {
        if (_fm == null) return;
        text = text.Trim().Replace(',', '.');
        if (text.Length == 0) { _fm.SetWeight(stat, null); return; }
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0)
            _fm.SetWeight(stat, v);
        else
        {
            AddLog("Poids invalide : saisissez un nombre positif (ex. 0,25).");
            RefreshFmWeights(); // la valeur précédente revient
        }
    }

    void FmWeightBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FmWeightRow row } box && box.Text != row.Weight) SetFmWeight(row.Stat, box.Text);
    }

    void FmWeightBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || sender is not TextBox { DataContext: FmWeightRow row } box) return;
        e.Handled = true;
        if (box.Text != row.Weight) SetFmWeight(row.Stat, box.Text);
    }

    void FmWeightReset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FmWeightRow row) _fm?.SetWeight(row.Stat, null);
    }

    void FmWeightsReset_Click(object sender, RoutedEventArgs e) => _fm?.ResetWeights();

    // --- Thème ---

    void ThemeButton_Click(object sender, RoutedEventArgs e) => ThemePopup.IsOpen = !ThemePopup.IsOpen;

    void StyleButton_Click(object sender, RoutedEventArgs e) =>
        ThemeManager.ApplyStyle(ThemeManager.Style == ThemeManager.Moderne ? ThemeManager.Classique : ThemeManager.Moderne);

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
        // Le bouton propose l'autre style
        StyleButtonText.Text = ThemeManager.Style == ThemeManager.Moderne ? "Classique" : "Moderne";
    }

    async Task InitAsync()
    {
        UpdateCaptureUi();
        // Interface HTML choisie au lancement précédent
        if (ThemeManager.HtmlUi) _ = ShowWebUiAsync();
        StartUpdateChecks();
        _ = Dispatcher.BeginInvoke(ShowUpdateResult, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        try
        {
            AddLog("Chargement des données du jeu…");
            _data = await Task.Run(() => GameData.LoadAsync(m => Ui(() => AddLog(m))));
        }
        catch (Exception ex)
        {
            AddLog("ERREUR : " + ex.Message);
            MessageBox.Show(this, "Impossible de charger les données du jeu :\n" + ex.Message, "Dofus Retro Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ItemIcons.Data = _data;
        InitFm(_data);
        // TTG et PDA avant l'HDV : la synchronisation avec le groupe partage aussi leurs prix
        InitTtg(_data);
        InitPda(_data);
        InitHunt(_data);
        InitHarvest(_data);
        InitOcre(_data);
        InitCarte(_data);
        InitArene();
        InitHdv(_data);
        _usage = _data.Recipes.SelectMany(r => r.Ingredients.Select(i => i.ItemId).Distinct())
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        _recipeByResult = _data.Recipes.GroupBy(r => r.ResultId).ToDictionary(g => g.Key, g => g.First());
        InitBrisage(_data);

        JobFilter.Items.Add(AllJobs);
        foreach (var j in _data.JobNames) JobFilter.Items.Add(j);
        JobFilter.SelectedIndex = 0;

        // Un bouton par nombre de cases existant dans le jeu (choix multiple)
        foreach (var n in _data.Recipes.Select(r => r.Ingredients.Count).Distinct().Order())
        {
            var toggle = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = n.ToString(), Tag = n, Style = (Style)FindResource("PillToggle"),
                ToolTip = $"Recettes à {n} case(s)",
            };
            toggle.Checked += Filters_Changed;
            toggle.Unchecked += Filters_Changed;
            SlotFilters.Children.Add(toggle);
        }

        _ready = true;
        _tracker.LoadLatest();
        Refresh();
        UpdateInventoryHint();

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
            // Multi-compte : chasse et marchands sont suivis par fenêtre de jeu
            if (_hunt != null) _hunt.Stream = stream;
            if (_merchants != null) _merchants.Stream = stream;
            if (_harvest != null) _harvest.Stream = stream;
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

    void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Sans shell, le nouveau processus hérite des droits actuels (pas de nouvelle demande administrateur)
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
            Application.Current.Shutdown();
        }
        catch (Exception ex) { AddLog("Relance impossible : " + ex.Message); }
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
                craftable, missing.Count, missingText, r.Ingredients.Any(i => owned.ContainsKey(i.ItemId)))
                { Cost = RecipeCost(r), Sell = UnitPrice(r.ResultId) };
        }).ToList();

        ApplyRecipeFilters();
        ApplyBankFilter();
        ApplyBrisageFilters();
        ApplyTtgFilters();
        ApplyPdaFilters();
        RefreshHunt();
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
            5 or 7 => q.Where(r => r.Profit > 0),
            6 => q.Where(r => r.Profit != null),
            _ => q
        };
        var profitMode = ModeFilter.SelectedIndex >= 5;
        SellMinPanel.Visibility = profitMode ? Visibility.Visible : Visibility.Collapsed;
        if (profitMode && long.TryParse(new string(SellMin.Text.Where(char.IsDigit).ToArray()), out var sellMin) && sellMin > 0)
            q = q.Where(r => r.Sell >= sellMin);
        if (job != null && job != AllJobs) q = q.Where(r => r.Recipe.Jobs.Contains(job));
        q = q.Where(r => r.Level >= min && r.Level <= max);
        var slots = SlotFilters.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()
            .Where(t => t.IsChecked == true).Select(t => (int)t.Tag).ToHashSet();
        if (slots.Count > 0) q = q.Where(r => slots.Contains(r.Slots));
        if (_ingredientFilter is { } ingredient) q = q.Where(r => r.Recipe.Ingredients.Any(i => i.ItemId == ingredient));
        if (search.Length > 0)
            q = q.Where(r => Contains(r.Name, search) || Contains(r.Type, search)
                || r.Recipe.Ingredients.Any(i => Contains(_data!.GetItem(i.ItemId).Name, search)));

        // Modes « rentabilité » : les plus gros bénéfices (ou marges) en haut ; sinon les faisables d'abord
        var ordered = ModeFilter.SelectedIndex == 7 ? q.OrderByDescending(r => r.Margin).ThenByDescending(r => r.Profit).ThenBy(r => r.Name)
            : profitMode ? q.OrderByDescending(r => r.Profit).ThenBy(r => r.Name)
            : q.OrderByDescending(r => r.Craftable > 0).ThenBy(r => r.MissingKinds).ThenByDescending(r => r.Level).ThenBy(r => r.Name);
        var list = ordered.Select((r, i) => r with { Index = i + 1 }).ToList();
        var selected = (RecipeGrid.SelectedItem as RecipeRow)?.Recipe;
        SetRows(RecipeGrid, list);
        RecipeGrid.SelectedItem = list.FirstOrDefault(r => r.Recipe == selected);
        RecipeCount.Text = $"{list.Count} recette(s) · bénéfice calculable pour {_rows.Count(r => r.Profit != null)}";
    }

    bool _bankTypesUpdating;

    void ApplyBankFilter()
    {
        if (!_ready || _data == null || _bankTypesUpdating) return;
        var search = BankSearch.Text.Trim();

        // Personnage : celui en jeu (1re entrée, suit les changements de personnage) ou la dernière banque connue d'un autre
        var current = _tracker.State;
        var snapshots = _tracker.AllSnapshots();
        var chars = snapshots.Skip(1).Select(x => x.Character).Distinct().Order()
            .Prepend("En jeu" + (current.Character.Length > 0 ? $" ({current.Character})" : "")).ToList();
        var chosen = BankCharacter.SelectedIndex > 0 ? BankCharacter.SelectedItem as string : null;
        if (!chars.SequenceEqual(BankCharacter.Items.Cast<string>()))
        {
            _bankTypesUpdating = true;
            BankCharacter.ItemsSource = chars;
            BankCharacter.SelectedIndex = chosen != null && chars.IndexOf(chosen) is > 0 and var i ? i : 0;
            _bankTypesUpdating = false;
        }
        var shown = BankCharacter.SelectedIndex > 0
            ? snapshots.Skip(1).FirstOrDefault(x => x.Character == (string)BankCharacter.SelectedItem) ?? current
            : current;

        var all = shown.Bank.Values
            .GroupBy(i => i.TemplateId)
            .Select(g =>
            {
                var it = _data.GetItem(g.Key);
                int qty = g.Sum(i => i.Quantity);
                var unit = UnitPrice(g.Key);
                return new BankRow(g.Key, it.Name, qty, it.Level, it.TypeName, _usage.GetValueOrDefault(g.Key))
                {
                    Value = unit is { } u ? (long)Math.Round(u * qty) : null,
                    UnitPrice = unit,
                    AveragePrice = _hdv?.Prices.GetValueOrDefault(g.Key)?.Average,
                };
            })
            .ToList();

        // Liste déroulante des types : uniquement ceux présents en banque, sélection conservée
        var types = all.Select(r => r.Type).Distinct().Order().Prepend(AllTypes).ToList();
        var type = BankTypeFilter.SelectedItem as string ?? AllTypes;
        if (!types.SequenceEqual(BankTypeFilter.Items.Cast<string>()))
        {
            _bankTypesUpdating = true;
            BankTypeFilter.ItemsSource = types;
            if (!types.Contains(type)) type = AllTypes;
            BankTypeFilter.SelectedItem = type;
            _bankTypesUpdating = false;
        }

        var rows = all
            .Where(r => type == AllTypes || r.Type == type)
            .Where(r => search.Length == 0 || Contains(r.Name, search) || Contains(r.Type, search))
            .OrderBy(r => r.Type).ThenBy(r => r.Name)
            .Select((r, i) => r with { Index = i + 1 })
            .ToList();
        SetRows(BankGrid, rows);
        var owner = shown.Character;
        BankOwner.Text = (owner.Length > 0 ? $"Banque de {owner}" : "Banque")
            + (shown != current && shown.BankUpdatedAt > DateTime.MinValue ? $" · relevée le {shown.BankUpdatedAt:dd/MM/yyyy HH:mm}" : "");
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
        _hdv.Changed += () =>
        {
            ApplyHdvFilters();
            Refresh(); // coût, vente et bénéfice des recettes, puis banque, brisage, TTG…
            ShowTtgDetail();
            RecipeGrid_SelectionChanged(RecipeGrid, null!); // coûts du détail de la recette
        };
        _tracker.HdvOffers += _hdv.OnOffers;
        _tracker.HdvAverage += _hdv.OnAverage;
        _tracker.HdvOfferMove += _hdv.OnOfferMove;

        _sync = new Hdv.HdvSync(_hdv, _merchants!, _pda!);
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

        SetRows(HdvGrid, rows);
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
        HdvChart.Points = price?.History
            .Where(s => s.UnitPrice != null).Select(s => (s.Time, s.UnitPrice!.Value)).ToList() ?? [];
        HdvHistory.ItemsSource = price?.History.AsEnumerable().Reverse()
            .Select(s => new HdvHistoryRow(s.Time.ToString("dd/MM/yyyy HH:mm"), Kamas(s.Lot1), Kamas(s.Lot10), Kamas(s.Lot100)))
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

        DetailTitle.Text = row.Name;
        DetailIcon.Source = ItemIcons.Get(row.Recipe.ResultId);
        DetailSub.Text = $"Niveau {row.Level} · {row.Type} · {row.Jobs}"
            + (row.Craftable > 0 ? $"\nFaisable {row.Craftable} fois" : $"\nIl manque {row.MissingKinds} ingrédient(s)");

        var stats = StatLines(_data.GetItem(row.Recipe.ResultId).MaxStats);
        DetailStats.ItemsSource = stats;
        DetailStatsPanel.Visibility = stats.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Nouvelle recette : autant de fabrications que les ressources le permettent (au moins 1)
        if (_detailRecipe != row.Recipe)
        {
            _detailRecipe = row.Recipe;
            DetailCraftCount.Text = Math.Max(1, row.Craftable).ToString();
        }
        DetailCraftMax.IsEnabled = row.Craftable > 0;
        ShowDetailCost();
    }

    Recipe? _detailRecipe;

    void DetailCraftCount_Changed(object sender, TextChangedEventArgs e) => ShowDetailCost();

    void DetailCraftMax_Click(object sender, RoutedEventArgs e)
    {
        if (RecipeGrid.SelectedItem is RecipeRow { Craftable: > 0 } row) DetailCraftCount.Text = row.Craftable.ToString();
    }

    /// <summary>
    /// Coût et rentabilité de N fabrications : prix HDV de chaque ingrédient (ou coût de sa sous-recette s'il n'a pas de prix),
    /// bénéfice en comptant tous les ingrédients, et bénéfice en ne comptant que ce qu'il faut acheter.
    /// </summary>
    void ShowDetailCost()
    {
        if (!_ready || RecipeGrid?.SelectedItem is not RecipeRow row || _data == null) return;
        int n = int.TryParse(DetailCraftCount.Text, out var c) && c > 0 ? Math.Min(c, 100_000) : 1;
        var owned = OwnedQuantities();

        // Ingrédients (et sous-recettes) pour les N fabrications
        var recipesByResult = _rows.ToLookup(r => r.Recipe.ResultId);
        DetailIngredients.ItemsSource = row.Recipe.Ingredients
            .Select(i => BuildIngredient(i.ItemId, i.Quantity * n, owned, recipesByResult, 1))
            .ToList();

        double total = 0, toBuy = 0;
        int unpriced = 0;
        foreach (var i in row.Recipe.Ingredients)
        {
            if (UnitCost(i.ItemId) is not { } unit) { unpriced++; continue; }
            total += unit * i.Quantity * n;
            toBuy += unit * Math.Max(0, i.Quantity * n - owned.GetValueOrDefault(i.ItemId));
        }
        int count = row.Recipe.Ingredients.Count;
        DetailCostPanel.Visibility = Visibility.Visible;
        DetailCostTitle.Text = n > 1 ? $"Coût de {n} fabrications" : "Coût de la recette";
        DetailCost.Text = unpriced == count ? "Coût inconnu" : Kamas(total) + (unpriced > 0 ? " (partiel)" : "");

        var lines = new List<string>();
        if (unpriced > 0)
            lines.Add($"{unpriced} ingrédient(s) sur {count} sans prix relevé : consultez-les à l'HDV.");
        if (unpriced < count) lines.Add($"À acheter (manquants) : {Kamas(toBuy)}");
        var sell = UnitPrice(row.Recipe.ResultId) * n;
        lines.Add(sell is { } s ? $"Vente HDV{(n > 1 ? $" ({n} × {Kamas(s / n)})" : "")} : {Kamas(s)}" : "Prix HDV de l'objet inconnu");
        DetailCostNote.Text = string.Join("\n", lines);

        // Bénéfices : seulement si tout est chiffré (sinon le résultat serait faux)
        bool known = sell != null && unpriced == 0;
        SetProfit(DetailProfit, "Bénéfice", known ? sell - total : null, total);
        SetProfit(DetailProfitOwned, "Hors ressources possédées", known && toBuy < total ? sell - toBuy : null, toBuy);
    }

    void SetProfit(TextBlock target, string label, double? profit, double cost)
    {
        target.Text = profit is { } p
            ? $"{label} : {(p >= 0 ? "+" : "")}{Kamas(p)}" + (cost > 0 ? $" ({p / cost:+0%;-0%;0%})" : "")
            : "";
        target.SetResourceReference(TextBlock.ForegroundProperty, profit >= 0 ? "Ok" : "Missing");
    }

    /// <summary>Profondeur maximale des sous-recettes affichées et des coûts de fabrication (garde-fou contre les cycles).</summary>
    const int MaxRecipeDepth = 4;
    Dictionary<int, Recipe> _recipeByResult = new();

    IngredientRow BuildIngredient(int itemId, int needed, Dictionary<int, int> owned, ILookup<int, RecipeRow> recipesByResult, int depth)
    {
        int have = owned.GetValueOrDefault(itemId);
        bool ok = have >= needed;
        var sub = recipesByResult[itemId].FirstOrDefault();
        var note = "";
        if (!ok && sub != null)
            note = sub.Craftable > 0 ? $"Fabricable avec vos ressources (×{sub.Craftable})" : "Fabricable (ressources manquantes)";

        // Quantités de la sous-recette pour fabriquer les « needed » exemplaires demandés
        var children = sub != null && depth < MaxRecipeDepth
            ? sub.Recipe.Ingredients.Select(i => BuildIngredient(i.ItemId, i.Quantity * needed, owned, recipesByResult, depth + 1)).ToList()
            : new List<IngredientRow>();

        var price = UnitPrice(itemId) is { } u
            ? Kamas(u * needed) + (needed > 1 ? $"  ({Kamas(u)} / u)" : "")
            : CraftCost(itemId) is { } c ? $"Fabrication ≈ {Kamas(c * needed)}" : "Prix inconnu";

        return new IngredientRow(itemId, $"{needed}× {_data!.GetItem(itemId).Name}", $"{have} / {needed}",
            ok ? (Brush)FindResource("Ok") : (Brush)FindResource("Missing"), note, price, children);
    }

    /// <summary>Meilleur prix unitaire relevé à l'HDV.</summary>
    double? UnitPrice(int itemId) => _hdv?.Prices.GetValueOrDefault(itemId)?.UnitPrice;

    /// <summary>Coût d'une unité : prix HDV, sinon coût de fabrication si tous ses ingrédients ont un coût connu.</summary>
    double? UnitCost(int itemId, int depth = 0) =>
        UnitPrice(itemId) ?? (depth < MaxRecipeDepth ? CraftCost(itemId, depth + 1) : null);

    /// <summary>Coût de fabrication d'une unité ; null si l'objet n'a pas de recette ou si un ingrédient n'a pas de coût connu.</summary>
    double? CraftCost(int itemId, int depth = 0) =>
        _recipeByResult.TryGetValue(itemId, out var recipe) ? RecipeCost(recipe, depth) : null;

    /// <summary>Coût d'une recette ; null si un ingrédient n'a pas de coût connu (prix HDV ou sous-recette).</summary>
    double? RecipeCost(Recipe recipe, int depth = 0)
    {
        double total = 0;
        foreach (var i in recipe.Ingredients)
        {
            if (UnitCost(i.ItemId, depth) is not { } c) return null;
            total += c * i.Quantity;
        }
        return total;
    }

    /// <summary>Caractéristiques triées et formatées, ex. « +200 Vitalité ».</summary>
    static List<string> StatLines(IReadOnlyDictionary<int, int> stats) =>
        stats.OrderBy(kv => ItemStats.Order(kv.Key)).Select(kv => ItemStats.Format(kv.Key, kv.Value)).ToList();

    void Filters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender == IncludeInventory) { UpdateInventoryHint(); Refresh(); }
        else ApplyRecipeFilters();
    }

    /// <summary>Indique à côté de « Inclure l'inventaire » si l'inventaire est réellement connu.</summary>
    void UpdateInventoryHint()
    {
        int count = _tracker.State.Inventory.Count;
        if (_tracker.InventoryComplete)
        {
            InventoryHint.Text = $"({count} objet(s) en inventaire)";
            InventoryHint.Foreground = (Brush)FindResource("TextMuted");
        }
        else
        {
            InventoryHint.Text = IncludeInventory.IsChecked == true
                ? "Inventaire inconnu : l'application doit être lancée avant de vous connecter à votre personnage. Reconnectez-vous (ou changez de personnage et revenez)."
                : "";
            InventoryHint.Foreground = (Brush)FindResource("Warning");
        }
    }

    void BankFilter_Changed(object sender, RoutedEventArgs e) => ApplyBankFilter();

    // --- Banque -> recettes utilisant un objet ---

    /// <summary>Objet dont on affiche les recettes qui l'utilisent (posé depuis l'onglet Banque).</summary>
    int? _ingredientFilter;

    void BankRecipes_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BankRow row) ShowRecipesUsing(row.ItemId);
    }

    void BankGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Double-clic sur une ligne seulement (pas sur un en-tête de colonne)
        if (ItemsControl.ContainerFromElement(BankGrid, e.OriginalSource as DependencyObject) is DataGridRow { Item: BankRow { UsedIn: > 0 } row })
            ShowRecipesUsing(row.ItemId);
    }

    /// <summary>Ouvre l'onglet Recettes sur toutes les recettes qui utilisent cet objet, les autres filtres remis à zéro.</summary>
    void ShowRecipesUsing(int itemId)
    {
        if (_data == null) return;
        _ready = false; // une seule mise à jour à la fin
        _ingredientFilter = itemId;
        RecipeSearch.Text = "";
        JobFilter.SelectedIndex = 0;
        ModeFilter.SelectedIndex = 4; // Toutes les recettes (les faisables restent en haut)
        LevelMin.Text = "1";
        LevelMax.Text = "200";
        foreach (var t in SlotFilters.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()) t.IsChecked = false;
        _ready = true;

        IngredientChipText.Text = $"Utilisant : {_data.GetItem(itemId).Name}";
        IngredientChip.Visibility = Visibility.Visible;
        ApplyRecipeFilters();
        RecipesTab.IsSelected = true;
    }

    void IngredientChipClear_Click(object sender, RoutedEventArgs e)
    {
        _ingredientFilter = null;
        IngredientChip.Visibility = Visibility.Collapsed;
        ApplyRecipeFilters();
    }

    // --- Utilitaires ---

    static bool Contains(string text, string search) =>
        System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, search,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;

    /// <summary>
    /// Remplace les lignes d'une grille en conservant le tri choisi par l'utilisateur (clic sur un en-tête) :
    /// WPF l'efface à chaque changement d'ItemsSource, par exemple à chaque nouveau prix HDV.
    /// </summary>
    static void SetRows(DataGrid grid, System.Collections.IEnumerable rows)
    {
        var sorts = grid.Items.SortDescriptions.ToList();
        var directions = grid.Columns.Select(c => c.SortDirection).ToList();
        grid.ItemsSource = rows;
        foreach (var s in sorts) grid.Items.SortDescriptions.Add(s);
        for (int i = 0; i < directions.Count; i++) grid.Columns[i].SortDirection = directions[i];
    }

    /// <summary>
    /// Quand la place manque, une grille avec une colonne étoile rétrécit aussi les colonnes à largeur fixe jusqu'à
    /// leur MinWidth (20 px), ce qui coupe les prix. Leur largeur devient donc un minimum : la grille défile à la place.
    /// </summary>
    static void KeepColumnWidths(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is DataGrid grid)
                foreach (var c in grid.Columns.Where(c => c.Width.IsAbsolute && c.MinWidth < c.Width.Value))
                    c.MinWidth = c.Width.Value;
            KeepColumnWidths(child);
        }
    }

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
