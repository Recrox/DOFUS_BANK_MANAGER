using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DofusBankManager.Hunt;
using DofusBankManager.Multi;

namespace DofusBankManager;

/// <summary>Fenêtre de jeu listée dans l'onglet Fenêtres. Order : place dans la rotation (0 = exclu).</summary>
public sealed record OrganizerRow(long Handle, int Order, string Name, string Level, int? Initiative, string State,
    bool Excluded, bool Active)
{
    public string OrderText => Excluded ? "—" : Order.ToString();
    public string InitiativeText => Initiative?.ToString("N0") ?? "?";
}

/// <summary>
/// Onglet Fenêtres : fenêtres du jeu détectées, ordre de jeu par initiative, raccourcis personnage suivant / précédent,
/// personnages exclus (compte métier) et notifications (début de combat, de tour, échange, invitation de groupe).
/// L'application ne fait que mettre une fenêtre au premier plan quand le joueur le demande : aucune touche ni aucun clic
/// n'est envoyé au jeu (README, règle 3).
/// </summary>
public partial class MainWindow
{
    readonly OrganizerSettings _organizer = OrganizerSettings.Load();
    GlobalHotkeys? _hotkeys;
    List<GameWindow> _windows = new();
    readonly DispatcherTimer _windowTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _organizerLoading;
    DateTime _lastNoticeSound;

    void InitOrganizer()
    {
        _organizerLoading = true;
        NotifyFight.IsChecked = _organizer.NotifyFight;
        NotifyTurn.IsChecked = _organizer.NotifyTurn;
        NotifyExchange.IsChecked = _organizer.NotifyExchange;
        NotifyParty.IsChecked = _organizer.NotifyParty;
        NotifyToast.IsChecked = _organizer.ShowToast;
        NotifyFlash.IsChecked = _organizer.FlashTaskbar;
        NotifySound.IsChecked = _organizer.PlaySound;
        HotkeyNext.Text = _organizer.NextKey;
        HotkeyPrevious.Text = _organizer.PreviousKey;
        _organizerLoading = false;

        SourceInitialized += (_, _) =>
        {
            _hotkeys = new GlobalHotkeys(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle));
            RegisterHotkeys();
        };
        Closed += (_, _) => _hotkeys?.Dispose();
        _windowTimer.Tick += (_, _) => RefreshWindows();
        _windowTimer.Start();
    }

    void RegisterHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.Clear();
        var failed = new List<string>();
        foreach (var (gesture, step) in new[] { (_organizer.NextKey, 1), (_organizer.PreviousKey, -1) })
            if (gesture.Length > 0 && !_hotkeys.Register(gesture, () => Cycle(step))) failed.Add(gesture);
        HotkeyStatus.Text = failed.Count == 0 ? ""
            : $"Raccourci indisponible (déjà pris par une autre application ?) : {string.Join(", ", failed)}";
    }

    /// <summary>Personnage d'une fenêtre : celui suivi sur une connexion au jeu, retrouvé par son nom.</summary>
    HuntCharacter? CharacterOf(GameWindow w) => w.Name.Length == 0 ? null
        : _hunt?.Characters.FirstOrDefault(c => string.Equals(c.Name, w.Name, StringComparison.OrdinalIgnoreCase));

    bool IsExcluded(GameWindow w) => w.Name.Length > 0 && _organizer.Excluded.Contains(w.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Ordre de jeu : initiative décroissante, initiative inconnue en dernier ; les personnages exclus à part.</summary>
    List<GameWindow> Rotation() => _windows.Where(w => !IsExcluded(w))
        .OrderByDescending(w => CharacterOf(w)?.Initiative ?? int.MinValue).ThenBy(w => w.Name).ToList();

    /// <summary>Raccourci : met au premier plan le personnage suivant (ou précédent) dans l'ordre de jeu.</summary>
    void Cycle(int step)
    {
        _windows = GameWindows.Find();
        var list = Rotation();
        if (list.Count == 0) return;
        int i = list.FindIndex(w => w.Handle == GameWindows.Foreground);
        int next = i < 0 ? (step > 0 ? 0 : list.Count - 1) : (i + step + list.Count) % list.Count;
        GameWindows.Focus(list[next].Handle);
        RefreshWindows();
    }

    void RefreshWindows()
    {
        _windows = GameWindows.Find();
        var rotation = Rotation();
        var foreground = GameWindows.Foreground;
        var rows = rotation.Concat(_windows.Where(IsExcluded)).Select(w =>
        {
            var c = CharacterOf(w);
            string state = w.Name.Length == 0 ? "Pas connecté"
                : c == null ? "Pas encore vu (lancez l'appli avant la connexion ou changez de map)"
                : c.InFight ? "En combat" : "Hors combat";
            return new OrganizerRow(w.Handle.ToInt64(), rotation.IndexOf(w) + 1, w.Name.Length > 0 ? w.Name : "(sélection du personnage)",
                c?.Level?.ToString() ?? "?", c?.Initiative, state, IsExcluded(w), w.Handle == foreground);
        }).ToList();

        // La grille n'est remplacée que si quelque chose a changé (sinon un clic en cours serait perdu)
        if (OrganizerGrid.ItemsSource is IEnumerable<OrganizerRow> old && old.SequenceEqual(rows)) return;
        SetRows(OrganizerGrid, rows);
        OrganizerEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OrganizerCount.Text = rows.Count == 0 ? "" : $"{rotation.Count} personnage(s) dans la rotation"
            + (rows.Count > rotation.Count ? $", {rows.Count - rotation.Count} exclu(s)" : "");
    }

    void OrganizerShow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OrganizerRow row) GameWindows.Focus(new IntPtr(row.Handle));
    }

    void OrganizerExclude_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OrganizerRow row) return;
        var name = _windows.FirstOrDefault(w => w.Handle.ToInt64() == row.Handle)?.Name;
        if (string.IsNullOrEmpty(name)) return; // fenêtre sans personnage : rien à exclure
        if (_organizer.Excluded.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) == 0)
            _organizer.Excluded.Add(name);
        _organizer.Save();
        RefreshWindows();
    }

    void OrganizerOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_organizerLoading) return;
        _organizer.NotifyFight = NotifyFight.IsChecked == true;
        _organizer.NotifyTurn = NotifyTurn.IsChecked == true;
        _organizer.NotifyExchange = NotifyExchange.IsChecked == true;
        _organizer.NotifyParty = NotifyParty.IsChecked == true;
        _organizer.ShowToast = NotifyToast.IsChecked == true;
        _organizer.FlashTaskbar = NotifyFlash.IsChecked == true;
        _organizer.PlaySound = NotifySound.IsChecked == true;
        _organizer.Save();
    }

    /// <summary>Saisie d'un raccourci : la combinaison pressée remplace le texte ; Suppr ou Retour arrière l'efface.</summary>
    void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Delete or Key.Back) { box.Text = ""; return; }
        var text = GlobalHotkeys.Format(Keyboard.Modifiers, key);
        if (text.Length > 0) box.Text = text;
    }

    void Hotkey_Changed(object sender, TextChangedEventArgs e)
    {
        if (_organizerLoading) return;
        _organizer.NextKey = HotkeyNext.Text;
        _organizer.PreviousKey = HotkeyPrevious.Text;
        _organizer.Save();
        RegisterHotkeys();
    }

    /// <summary>Notification d'un évènement de jeu, sauf si sa fenêtre est déjà au premier plan.</summary>
    void OnGameNotice(HuntCharacter c, GameNotice notice)
    {
        if (!_organizer.Notify(notice)) return;
        var window = (_windows.Count > 0 ? _windows : GameWindows.Find())
            .FirstOrDefault(w => string.Equals(w.Name, c.Name, StringComparison.OrdinalIgnoreCase));
        if (window != null && window.Handle == GameWindows.Foreground) return;

        var text = notice switch
        {
            GameNotice.Fight => "Combat lancé",
            GameNotice.Turn => "C'est à vous de jouer",
            GameNotice.Exchange => "Demande d'échange",
            _ => "Invitation de groupe",
        };
        if (_organizer.ShowToast)
            NoticeToast.Show(c.Label, text, window != null ? () => GameWindows.Focus(window.Handle) : null);
        if (_organizer.FlashTaskbar && window != null) GameWindows.Flash(window.Handle);
        // Plusieurs fenêtres entrent souvent en combat ensemble : un seul son
        if (_organizer.PlaySound && DateTime.Now - _lastNoticeSound > TimeSpan.FromSeconds(1.5))
        {
            _lastNoticeSound = DateTime.Now;
            try { ArchiSound.PlayNotice(_hunt?.SoundVolume ?? 70); } catch { }
        }
    }
}
