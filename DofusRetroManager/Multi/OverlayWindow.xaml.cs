using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DofusRetroManager.Multi;

/// <summary>Contenu de la superposition, recalculé avec l'onglet Combat et la liste des fenêtres.</summary>
public sealed record OverlayModel(string Title, string Stats, string Econome, IReadOnlyList<EconomeSpell> Spells,
    string GroupsTitle, IReadOnlyList<OverlayGroup> Groups, IReadOnlyList<OverlayTurn> Rotation,
    bool ShowEconome, bool ShowGroups, bool ShowRotation, System.Windows.Media.ImageSource? Icon = null,
    string HarvestTitle = "", IReadOnlyList<string>? Harvest = null, bool ShowHarvest = false)
{
    /// <summary>Résumé comparable : la fenêtre n'est redessinée que si quelque chose a changé.</summary>
    public string Key => string.Join("|", Title, Stats, Econome, string.Join(",", Spells.Select(s => $"{s.Id}:{s.State}")),
        GroupsTitle, string.Join(",", Groups), string.Join(",", Rotation), ShowEconome, ShowGroups, ShowRotation, Icon,
        HarvestTitle, string.Join(",", Harvest ?? []), ShowHarvest);
}

public sealed record OverlayGroup(string Monsters, string Info, bool Archi);

/// <summary>
/// Ligne de l'ordre de jeu. Life : points de vie en pourcentage (null : inconnus). Icon : tête du personnage (classe et sexe).
/// Handle : fenêtre du personnage (clic : elle passe au premier plan). Timer : temps du tour qui s'écoule (« 12 / 30 s »).
/// Blink : c'est son tour (mode split : la ligne clignote au lieu de basculer).
/// </summary>
public sealed record OverlayTurn(int Order, string Name, string Initiative, bool Active, int? Life, System.Windows.Media.ImageSource? Icon = null,
    long Handle = 0, string Timer = "", bool Blink = false)
{
    public bool HasLife => Life != null;
    public double LifeValue => Life ?? 0;
    public string LifeText => Life is { } l ? $"{l} %" : "";
}

/// <summary>
/// Superposition en jeu. Verrouillée, elle laisse passer la souris (les clics vont au jeu) et ne prend jamais le focus ;
/// déverrouillée, on la fait glisser pour la placer.
/// </summary>
public partial class OverlayWindow : Window
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;

    bool _locked = true;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyStyle();
        MouseLeftButtonDown += (_, e) => { if (!_locked && e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    /// <summary>Verrouillée : clics transmis au jeu. Déverrouillée : déplaçable à la souris.</summary>
    public bool Locked
    {
        get => _locked;
        set
        {
            _locked = value;
            MoveHint.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            Cursor = value ? null : Cursors.SizeAll;
            ApplyStyle();
        }
    }

    public void Show(OverlayModel model)
    {
        if ((DataContext as OverlayModel)?.Key != model.Key) DataContext = model;
    }

    /// <summary>Clic sur un personnage de l'ordre de jeu : sa fenêtre (handle).</summary>
    public event Action<long>? TurnClicked;

    /// <summary>Verrouillée, la souris est sur l'ordre de jeu : seule cette zone reçoit les clics.</summary>
    bool _overRotation;

    void ApplyStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
        style = _locked && !_overRotation ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    /// <summary>
    /// Appelé régulièrement : la superposition laisse passer les clics vers le jeu, sauf sur la liste de l'ordre de jeu,
    /// où un clic sur un personnage affiche sa fenêtre. Elle ne prend jamais le focus.
    /// </summary>
    public void UpdateHitTest()
    {
        if (!IsVisible || !GetCursorPos(out var p)) return;
        bool over = false;
        if (_locked && RotationList.IsVisible && RotationList.ActualHeight > 0)
        {
            var local = RotationList.PointFromScreen(new Point(p.X, p.Y));
            over = local.X >= 0 && local.Y >= 0 && local.X <= RotationList.ActualWidth && local.Y <= RotationList.ActualHeight;
        }
        if (over == _overRotation) return;
        _overRotation = over;
        ApplyStyle();
    }

    void Turn_Click(object sender, MouseButtonEventArgs e)
    {
        if (_locked && (sender as FrameworkElement)?.DataContext is OverlayTurn { Handle: not 0 } t) TurnClicked?.Invoke(t.Handle);
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
}
