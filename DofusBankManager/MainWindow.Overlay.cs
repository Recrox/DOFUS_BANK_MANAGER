using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DofusBankManager.Multi;

namespace DofusBankManager;

/// <summary>
/// Superposition en jeu (onglet Fenêtres) : personnage, Économe, groupes de la map et ordre de jeu, dans une petite
/// fenêtre au-dessus du jeu. Elle n'apparaît que quand une fenêtre de jeu est au premier plan et laisse passer les clics :
/// c'est une fenêtre de l'application, rien n'est injecté ni dessiné dans le client (README, règles 2 et 3).
/// </summary>
public partial class MainWindow
{
    OverlayWindow? _overlay;
    readonly DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    void InitOverlay()
    {
        _organizerLoading = true;
        OverlayEnabled.IsChecked = _organizer.OverlayEnabled;
        OverlayEconome.IsChecked = _organizer.OverlayEconome;
        OverlayGroups.IsChecked = _organizer.OverlayGroups;
        OverlayRotation.IsChecked = _organizer.OverlayRotation;
        OverlayOpacity.Value = _organizer.OverlayOpacity * 100;
        _organizerLoading = false;
        _overlayTimer.Tick += (_, _) => UpdateOverlayVisibility();
        _overlayTimer.Start();
        Closed += (_, _) => _overlay?.Close();
    }

    void OverlayOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_organizerLoading) return;
        _organizer.OverlayEnabled = OverlayEnabled.IsChecked == true;
        _organizer.OverlayEconome = OverlayEconome.IsChecked == true;
        _organizer.OverlayGroups = OverlayGroups.IsChecked == true;
        _organizer.OverlayRotation = OverlayRotation.IsChecked == true;
        _organizer.Save();
        // Superposition désactivée : on ressort du mode déplacement
        if (!_organizer.OverlayEnabled && _overlay is { Locked: false }) OverlayMove_Click(sender, e);
        UpdateOverlay();
        UpdateOverlayVisibility();
    }

    void OverlayOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OverlayOpacityText != null) OverlayOpacityText.Text = $"{OverlayOpacity.Value:0} %";
        if (_organizerLoading) return;
        _organizer.OverlayOpacity = OverlayOpacity.Value / 100;
        _organizer.Save();
        if (_overlay != null) _overlay.Opacity = _organizer.OverlayOpacity;
    }

    /// <summary>Déplacer : la superposition s'affiche et se fait glisser ; Verrouiller : sa position est retenue.</summary>
    void OverlayMove_Click(object sender, RoutedEventArgs e)
    {
        var overlay = EnsureOverlay();
        overlay.Locked = !overlay.Locked;
        OverlayMove.Content = overlay.Locked ? "Déplacer" : "Verrouiller";
        if (overlay.Locked)
        {
            _organizer.OverlayLeft = overlay.Left;
            _organizer.OverlayTop = overlay.Top;
            _organizer.Save();
        }
        UpdateOverlay();
        UpdateOverlayVisibility();
    }

    OverlayWindow EnsureOverlay()
    {
        if (_overlay != null) return _overlay;
        var area = SystemParameters.WorkArea;
        _overlay = new OverlayWindow { Opacity = _organizer.OverlayOpacity };
        // Position retenue, si elle est encore sur un écran ; sinon en haut à droite
        bool onScreen = _organizer.OverlayLeft is { } l && _organizer.OverlayTop is { } t
            && l >= SystemParameters.VirtualScreenLeft - 50 && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
            && t >= SystemParameters.VirtualScreenTop - 20 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;
        _overlay.Left = onScreen ? _organizer.OverlayLeft!.Value : area.Right - 330;
        _overlay.Top = onScreen ? _organizer.OverlayTop!.Value : area.Top + 90;
        return _overlay;
    }

    /// <summary>Visible si activée et qu'une fenêtre de jeu est au premier plan (toujours visible pendant le déplacement).</summary>
    void UpdateOverlayVisibility()
    {
        bool moving = _overlay is { Locked: false };
        bool want = moving || (_organizer.OverlayEnabled && IsGameForeground());
        if (want && _overlay is not { IsVisible: true })
        {
            UpdateOverlay();
            EnsureOverlay().Show();
        }
        else if (!want && _overlay is { IsVisible: true }) _overlay.Hide();
    }

    bool IsGameForeground()
    {
        var fg = GameWindows.Foreground;
        return _windows.Any(w => w.Handle == fg)
            || (_overlay != null && new WindowInteropHelper(_overlay).Handle == fg);
    }

    void UpdateOverlay()
    {
        if (_overlay == null && !_organizer.OverlayEnabled) return;
        var me = _hunt?.Shown;
        var spells = (EconomeSpells.ItemsSource as IEnumerable<EconomeSpell>)?.ToList() ?? [];
        var allGroups = (HuntGroupGrid.ItemsSource as IEnumerable<HuntGroupRow>)?.ToList() ?? [];
        var groups = allGroups.Take(3).Select(g => new OverlayGroup(g.Monsters,
            $"Niv. {g.TotalLevel} · XP {g.XpText}" + (g.DropValue is { } v ? $" · drops ≈ {v:N0} k" : "")
            + (g.HasArchi ? $" · ARCHI : {g.Archi}" : ""), g.HasArchi)).ToList();
        var fg = GameWindows.Foreground;
        var rotation = Rotation().Select((w, i) => new OverlayTurn(i + 1, w.Name.Length > 0 ? w.Name : "(sélection du personnage)",
            CharacterOf(w)?.Initiative?.ToString("N0") ?? "?", w.Handle == fg)).ToList();

        EnsureOverlay().Show(new OverlayModel(
            me?.Label ?? "Aucun personnage suivi",
            me == null ? "Changez de map en jeu"
                : $"Niv. {me.Level?.ToString() ?? "?"} · PP {me.GroupProspection?.ToString("N0") ?? "?"} · Sagesse {me.Wisdom?.ToString("N0") ?? "?"}",
            "Économe · " + EconomeStatus.Text, spells,
            allGroups.Count == 0 ? "Map · aucun groupe" : $"Map · {allGroups.Count} groupe(s)" + (allGroups.Count > 3 ? " (3 plus forts)" : ""),
            groups, rotation,
            _organizer.OverlayEconome && spells.Count > 0, _organizer.OverlayGroups, _organizer.OverlayRotation && rotation.Count > 1));
    }
}
