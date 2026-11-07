using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DofusRetroManager.Data;
using DofusRetroManager.Net;

namespace DofusRetroManager;

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
        WireControls();
        InitOrganizer();
        InitOverlay();
        _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        _sniffer.Log += m => Ui(() => AddLog(m));
        _sniffer.MessageReceived += OnMessage;
        _sniffer.StreamClassified += OnStreamClassified;
        _sniffer.StreamClosed += stream => Ui(() => _tracker.OnStreamClosed(stream));
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
        VersionText.Text = "v" + UpdateChecker.CurrentText;
    }


    // --- Thème ---

    void OnThemeChanged()
    {
        RefreshFm();
        // Les couleurs des ingrédients sont calculées à l'affichage : on les recalcule
        RecipeGrid_SelectionChanged(RecipeGrid, null!);
    }

    async Task InitAsync()
    {
        UpdateCaptureUi();
        // Interface HTML : la seule interface de l'application
        _ = ShowWebUiAsync();
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
        InitArene();
        InitHdv(_data);
        InitTrades(_data);
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
#if DEBUG
        _ = ReplayAsync();
#endif
    }

#if DEBUG
    /// <summary>
    /// Tests : rejoue un fichier de messages (une ligne par message, « connexion&lt;TAB&gt;message », ou « #wait ms ») comme
    /// s'ils venaient du jeu. Version de développement seulement, avec DRM_REPLAY (et DRM_APPDIR, pour ne jamais toucher
    /// aux données du joueur).
    /// </summary>
    async Task ReplayAsync()
    {
        if (Environment.GetEnvironmentVariable("DRM_REPLAY") is not { Length: > 0 } path || !File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("#wait")) { await Task.Delay(int.Parse(line[5..].Trim())); continue; }
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            OnMessage(line[..tab], line[(tab + 1)..]);
            await Task.Delay(30);
        }
        AddLog("Relecture terminée.");
    }
#endif


    // --- Formats (kamas, ancienneté d'un relevé) ---

    static string Kamas(double? v) => v is { } k ? $"{k:N0} k" : "—";

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "à l'instant"
            : d.TotalHours < 1 ? $"il y a {(int)d.TotalMinutes} min"
            : d.TotalDays < 1 ? $"il y a {(int)d.TotalHours} h"
            : t.ToString("dd/MM/yyyy");
    }

    // --- Utilitaires ---

    static bool Contains(string text, string search) =>
        System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, search,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;

    /// <summary>
    /// Remplace les lignes d'une grille en conservant le tri choisi par l'utilisateur (clic sur un en-tête) :
    /// WPF l'efface à chaque changement d'ItemsSource, par exemple à chaque nouveau prix HDV.
    /// </summary>
    /// <summary>Lignes d'un tableau : le tri et la pagination sont faits par la page.</summary>
    static void SetRows(WebList grid, System.Collections.IEnumerable rows) => grid.ItemsSource = rows;

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
