using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DofusBankManager.Multi;

/// <summary>Contenu de la superposition, recalculé avec l'onglet Combat et la liste des fenêtres.</summary>
public sealed record OverlayModel(string Title, string Stats, string Econome, IReadOnlyList<EconomeSpell> Spells,
    string GroupsTitle, IReadOnlyList<OverlayGroup> Groups, IReadOnlyList<OverlayTurn> Rotation,
    bool ShowEconome, bool ShowGroups, bool ShowRotation)
{
    /// <summary>Résumé comparable : la fenêtre n'est redessinée que si quelque chose a changé.</summary>
    public string Key => string.Join("|", Title, Stats, Econome, string.Join(",", Spells.Select(s => $"{s.Id}:{s.State}")),
        GroupsTitle, string.Join(",", Groups), string.Join(",", Rotation), ShowEconome, ShowGroups, ShowRotation);
}

public sealed record OverlayGroup(string Monsters, string Info, bool Archi);

public sealed record OverlayTurn(int Order, string Name, string Initiative, bool Active);

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

    void ApplyStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
        style = _locked ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
}
