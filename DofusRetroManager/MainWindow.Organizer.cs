using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DofusRetroManager.Data;
using DofusRetroManager.Hunt;
using DofusRetroManager.Multi;

namespace DofusRetroManager;

/// <summary>Fenêtre de jeu listée dans l'onglet Switcheur. Order : place dans la rotation (0 = exclu).</summary>
public sealed record OrganizerRow(long Handle, int Order, string Name, string Level, int? Initiative, string State,
    bool Excluded, bool Active, bool Main, string Class = "", int Gfx = 0, System.Windows.Media.ImageSource? Icon = null)
{
    public string OrderText => Excluded ? "—" : Order.ToString();
    public string InitiativeText => Initiative?.ToString("N0") ?? "?";
    /// <summary>Étoile pleine pour le personnage principal, vide pour les autres (cliquable).</summary>
    public string Star => Main ? "★" : "☆";
}

/// <summary>
/// Onglet Switcheur : fenêtres du jeu détectées, ordre de jeu par initiative, raccourcis personnage suivant / précédent,
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
        NotifyAutoSwitch.IsChecked = _organizer.AutoSwitch;
        NotifyAutoSwitchTurn.IsChecked = _organizer.AutoSwitchTurn;
        OrganizerEnabled.IsChecked = _organizer.Enabled;
        OrganizerLockOrder.IsChecked = _organizer.LockOrderOnParty;
        HotkeyDebounce.Text = _organizer.HotkeyDebounceMs.ToString();
        HotkeyNext.Text = _organizer.NextKey;
        HotkeyPrevious.Text = _organizer.PreviousKey;
        HotkeyMain.Text = _organizer.MainKey;
        OrganizerOptions.IsEnabled = _organizer.Enabled;
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
        // Switcheur désactivé : les touches sont rendues aux autres applications (et au jeu)
        if (!_organizer.Enabled)
        {
            HotkeyStatus.Text = "";
            return;
        }
        var failed = new List<string>();
        foreach (var (gesture, step) in new[] { (_organizer.NextKey, 1), (_organizer.PreviousKey, -1) })
            if (gesture.Length > 0 && !_hotkeys.Register(gesture, () => Debounced(() => Cycle(step)))) failed.Add(gesture);
        if (_organizer.MainKey.Length > 0 && !_hotkeys.Register(_organizer.MainKey, () => Debounced(FocusMain))) failed.Add(_organizer.MainKey);
        HotkeyStatus.Text = failed.Count == 0 ? ""
            : $"Raccourci indisponible (déjà pris par une autre application ?) : {string.Join(", ", failed)}";
    }

    DateTime _lastHotkey;

    /// <summary>Anti-rebond : une pression trop proche de la précédente (touche qui rebondit, double appui) est ignorée.</summary>
    void Debounced(Action action)
    {
        var now = DateTime.Now;
        if ((now - _lastHotkey).TotalMilliseconds < _organizer.HotkeyDebounceMs) return;
        _lastHotkey = now;
        action();
    }

    void HotkeyDebounce_Changed(object sender, TextChangedEventArgs e)
    {
        if (_organizerLoading) return;
        if (int.TryParse(HotkeyDebounce.Text, out var ms) && ms is >= 0 and <= 5000)
        {
            _organizer.HotkeyDebounceMs = ms;
            _organizer.Save();
        }
    }

    /// <summary>
    /// Entrée en groupe : l'ordre de jeu actuel est enregistré, les personnages qui n'y étaient pas encore sont ajoutés à la
    /// suite, et il ne bouge plus (l'initiative change en combat, l'ordre ne doit pas la suivre).
    /// </summary>
    void OnPartyJoined(HuntCharacter c)
    {
        if (!_organizer.LockOrderOnParty) return;
        _windows = GameWindows.Find();
        var current = Rotation().Select(w => w.Name).Where(n => n.Length > 0).ToList();
        var saved = _pendingOrder ?? _organizer.Order;
        var missing = current.Where(n => !saved.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count == 0 && _pendingOrder == null && saved.Count > 0) return;
        _organizer.Order = saved.Concat(missing).ToList();
        _pendingOrder = null;
        _organizer.Save();
        AddLog($"Groupe formé : ordre de jeu enregistré ({string.Join(", ", _organizer.Order)}).");
        RefreshWindows();
    }

    /// <summary>Classe et sexe du personnage d'une fenêtre (icône), vus à sa sélection, même lors d'un lancement précédent.</summary>
    CharacterLook? LookOf(GameWindow w) => _hunt?.LookOf(w.Name);

    /// <summary>Personnage d'une fenêtre : celui suivi sur une connexion au jeu, retrouvé par son nom.</summary>
    HuntCharacter? CharacterOf(GameWindow w) => w.Name.Length == 0 ? null
        : _hunt?.Characters.FirstOrDefault(c => string.Equals(c.Name, w.Name, StringComparison.OrdinalIgnoreCase));

    bool IsMain(GameWindow w) => w.Name.Length > 0 && string.Equals(w.Name, _organizer.MainCharacter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Raccourci « Principal » : met au premier plan la fenêtre du personnage principal (étoile).</summary>
    void FocusMain()
    {
        _windows = GameWindows.Find();
        if (_windows.FirstOrDefault(IsMain) is { } main)
        {
            GameWindows.Focus(main.Handle);
            RefreshWindows();
        }
    }

    bool IsExcluded(GameWindow w) => w.Name.Length > 0 && _organizer.Excluded.Contains(w.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Ordre déplacé par glisser-déposer, pas encore enregistré (il s'applique déjà).</summary>
    List<string>? _pendingOrder;
    bool _dragging;
    Point _dragStart;
    OrganizerRow? _dragRow;

    /// <summary>
    /// Ordre de jeu : celui choisi par glisser-déposer s'il y en a un (les personnages qui n'y sont pas suivent, par
    /// initiative), sinon initiative décroissante, initiative inconnue en dernier. Les personnages exclus sont à part.
    /// </summary>
    List<GameWindow> Rotation()
    {
        var byInitiative = _windows.Where(w => !IsExcluded(w))
            .OrderByDescending(w => CharacterOf(w)?.Initiative ?? int.MinValue).ThenBy(w => w.Name);
        var order = _pendingOrder ?? (_organizer.Order.Count > 0 ? _organizer.Order : null);
        if (order == null) return byInitiative.ToList();
        return byInitiative.OrderBy(w =>
        {
            int i = order.FindIndex(n => string.Equals(n, w.Name, StringComparison.OrdinalIgnoreCase));
            return i < 0 || w.Name.Length == 0 ? int.MaxValue : i;
        }).ToList();
    }

    // --- Glisser-déposer : réorganiser l'ordre de jeu ---

    void OrganizerGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragRow = null;
        // Pas depuis un bouton ou une case (étoile, Exclure, Afficher)
        for (var d = e.OriginalSource as DependencyObject; d != null && d != OrganizerGrid; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return;
        if (ItemsControl.ContainerFromElement(OrganizerGrid, e.OriginalSource as DependencyObject) is DataGridRow { Item: OrganizerRow row }
            && !row.Excluded && WindowName(row).Length > 0)
        {
            _dragRow = row;
            _dragStart = e.GetPosition(OrganizerGrid);
        }
    }

    void OrganizerGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragRow == null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(OrganizerGrid);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _dragRow;
        _dragRow = null;
        _dragging = true; // la liste n'est pas rafraîchie pendant le déplacement
        try { DragDrop.DoDragDrop(OrganizerGrid, row, DragDropEffects.Move); }
        finally { _dragging = false; }
        RefreshWindows();
    }

    void OrganizerGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(OrganizerRow)) is OrganizerRow source
            && ItemsControl.ContainerFromElement(OrganizerGrid, e.OriginalSource as DependencyObject) is DataGridRow { Item: OrganizerRow target })
            MoveInOrder(source, target);
    }

    /// <summary>Place le personnage de la ligne « source » à la place de celui de « target » (ordre en attente).</summary>
    void MoveInOrder(OrganizerRow source, OrganizerRow target)
    {
        string from = WindowName(source), to = WindowName(target);
        if (from.Length == 0 || target.Excluded || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return;
        var names = Rotation().Select(w => w.Name).Where(n => n.Length > 0).ToList();
        names.RemoveAll(n => string.Equals(n, from, StringComparison.OrdinalIgnoreCase));
        int i = names.FindIndex(n => string.Equals(n, to, StringComparison.OrdinalIgnoreCase));
        names.Insert(i < 0 ? names.Count : (target.Order > source.Order ? i + 1 : i), from);
        _pendingOrder = names;
        RefreshWindows();
    }

    string WindowName(OrganizerRow row) => _windows.FirstOrDefault(w => w.Handle.ToInt64() == row.Handle)?.Name ?? "";

    void OrganizerSaveOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingOrder == null) return;
        _organizer.Order = _pendingOrder;
        _pendingOrder = null;
        _organizer.Save();
        RefreshWindows();
    }

    /// <summary>Retour à l'ordre automatique (initiative) : l'ordre enregistré est oublié.</summary>
    void OrganizerResetOrder_Click(object sender, RoutedEventArgs e)
    {
        _pendingOrder = null;
        _organizer.Order = new();
        _organizer.Save();
        RefreshWindows();
    }

    void UpdateOrderUi()
    {
        bool saved = _organizer.Order.Count > 0;
        OrganizerSaveOrder.IsEnabled = _pendingOrder != null;
        OrganizerResetOrder.IsEnabled = saved || _pendingOrder != null;
        OrganizerOrderStatus.Text = _pendingOrder != null ? "Ordre modifié : enregistrez-le pour le garder"
            : saved ? "Ordre personnalisé (enregistré)" : "Ordre par initiative";
        OrganizerOrderStatus.SetResourceReference(TextBlock.ForegroundProperty, _pendingOrder != null ? "Warning" : "TextMuted");
    }

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
        if (_dragging) return;
        _windows = GameWindows.Find();
        UpdateOrderUi();
        var rotation = Rotation();
        var foreground = GameWindows.Foreground;
        var rows = rotation.Concat(_windows.Where(IsExcluded)).Select(w =>
        {
            var c = CharacterOf(w);
            var look = LookOf(w);
            string state = w.Name.Length == 0 ? "Pas connecté"
                : c == null ? "Pas encore vu (lancez l'appli avant la connexion ou changez de map)"
                : c.InFight ? "En combat" : "Hors combat";
            return new OrganizerRow(w.Handle.ToInt64(), rotation.IndexOf(w) + 1, w.Name.Length > 0 ? w.Name : "(sélection du personnage)",
                c?.Level?.ToString() ?? "?", c?.Initiative, state, IsExcluded(w), w.Handle == foreground, IsMain(w),
                look?.Text ?? "", look?.Gfx ?? 0, CharacterIcons.Get(look));
        }).ToList();

        UpdateOverlay(); // ordre de jeu et fenêtre au premier plan
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

    /// <summary>Étoile : ce personnage devient le principal (un seul) ; un nouveau clic sur l'étoile pleine l'enlève.</summary>
    void OrganizerMain_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OrganizerRow row) return;
        var name = _windows.FirstOrDefault(w => w.Handle.ToInt64() == row.Handle)?.Name;
        if (string.IsNullOrEmpty(name)) return;
        _organizer.MainCharacter = string.Equals(_organizer.MainCharacter, name, StringComparison.OrdinalIgnoreCase) ? null : name;
        _organizer.Save();
        RefreshWindows();
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
        _organizer.AutoSwitch = NotifyAutoSwitch.IsChecked == true;
        _organizer.AutoSwitchTurn = NotifyAutoSwitchTurn.IsChecked == true;
        _organizer.LockOrderOnParty = OrganizerLockOrder.IsChecked == true;
        bool enabledChanged = _organizer.Enabled != (OrganizerEnabled.IsChecked == true);
        _organizer.Enabled = OrganizerEnabled.IsChecked == true;
        _organizer.Save();
        if (enabledChanged) RegisterHotkeys();
        OrganizerOptions.IsEnabled = _organizer.Enabled;
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
        _organizer.MainKey = HotkeyMain.Text;
        _organizer.Save();
        RegisterHotkeys();
    }

    /// <summary>Notification d'un évènement de jeu, sauf si sa fenêtre est déjà au premier plan.</summary>
    /// <summary>Dernière notification (fenêtre, type, heure) : une invitation vue par deux de vos fenêtres n'est notifiée qu'une fois.</summary>
    (IntPtr Window, GameNotice Notice, DateTime Time) _lastNotice;

    void OnGameNotice(HuntCharacter c, GameNotice notice, string? target)
    {
        if (!_organizer.Enabled) return;
        // Personnage concerné : celui que nomme le message (l'invité), sinon celui de la connexion
        var name = target ?? c.Name;
        var windows = _windows.Count > 0 ? _windows : GameWindows.Find();
        var window = windows.FirstOrDefault(w => name.Length > 0 && string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        // Invitation lancée par un de vos comptes vers un joueur qui n'est pas à vous : rien à signaler
        if (window == null && target != null && !string.Equals(c.Name, target, StringComparison.OrdinalIgnoreCase)) return;
        if (window != null && _lastNotice.Window == window.Handle && _lastNotice.Notice == notice
            && DateTime.Now - _lastNotice.Time < TimeSpan.FromSeconds(3)) return;
        if (window != null) _lastNotice = (window.Handle, notice, DateTime.Now);

        // Invitation de groupe ou d'échange : la fenêtre de l'invité passe au premier plan (il n'a plus qu'à accepter) ;
        // début de tour : la fenêtre du personnage qui doit jouer (il joue toujours lui-même)
        bool autoSwitch = notice switch
        {
            GameNotice.Party or GameNotice.Exchange => _organizer.AutoSwitch,
            GameNotice.Turn => _organizer.AutoSwitchTurn,
            _ => false,
        };
        if (autoSwitch && window != null && !IsExcluded(window) && window.Handle != GameWindows.Foreground)
        {
            GameWindows.Focus(window.Handle);
            RefreshWindows();
        }

        if (!_organizer.Notify(notice)) return;
        if (window != null && window.Handle == GameWindows.Foreground)
        {
            // Déjà sous les yeux (ou affichée à l'instant) : seulement le son, pour attirer l'attention
            if (notice is GameNotice.Party or GameNotice.Exchange) PlayNoticeSound();
            return;
        }

        var text = notice switch
        {
            GameNotice.Fight => "Combat lancé",
            GameNotice.Turn => "C'est à vous de jouer",
            GameNotice.Exchange => "Demande d'échange",
            _ => "Invitation de groupe",
        };
        if (_organizer.ShowToast)
            NoticeToast.Show(window?.Name is { Length: > 0 } n ? n : c.Label, text,
                window != null ? () => GameWindows.Focus(window.Handle) : null);
        if (_organizer.FlashTaskbar && window != null) GameWindows.Flash(window.Handle);
        PlayNoticeSound();
    }

    /// <summary>Plusieurs fenêtres entrent souvent en combat ensemble : un seul son.</summary>
    void PlayNoticeSound()
    {
        if (!_organizer.PlaySound || DateTime.Now - _lastNoticeSound < TimeSpan.FromSeconds(1.5)) return;
        _lastNoticeSound = DateTime.Now;
        try { ArchiSound.PlayNotice(_hunt?.SoundVolume ?? 70); } catch { }
    }
}
