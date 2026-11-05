using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DofusRetroManager.Multi;

/// <summary>Fenêtre du client Dofus Retro : son personnage est lu dans le titre (« Recrox - Dofus Retro v1.49.9 »).</summary>
public sealed record GameWindow(IntPtr Handle, string Name);

/// <summary>
/// Fenêtres du jeu : détection par leur titre, mise au premier plan et clignotement dans la barre des tâches.
/// L'application ne lit que le titre des fenêtres et ne fait que les afficher : elle n'envoie jamais de touche ni de
/// clic au jeu (README, règles 2 et 3).
/// </summary>
public static partial class GameWindows
{
    [GeneratedRegex(@"^(?:(?<name>.+?) - )?Dofus Retro v[\d.]+$")]
    private static partial Regex TitleRegex();

    public static List<GameWindow> Find()
    {
        var found = new List<GameWindow>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            int length = GetWindowTextLength(h);
            if (length == 0 || length > 200) return true;
            var sb = new StringBuilder(length + 1);
            GetWindowText(h, sb, sb.Capacity);
            var m = TitleRegex().Match(sb.ToString());
            if (m.Success && IsDofusProcess(h)) found.Add(new GameWindow(h, m.Groups["name"].Value));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Le titre seul ne suffit pas (un onglet de navigateur pourrait l'imiter) : le processus doit être le client.</summary>
    static bool IsDofusProcess(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid);
        try { return Process.GetProcessById((int)pid).ProcessName.Contains("dofus", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static IntPtr Foreground => GetForegroundWindow();

    /// <summary>Processus d'une fenêtre.</summary>
    public static uint ProcessOf(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid);
        return pid;
    }

    /// <summary>
    /// Met la fenêtre au premier plan (comme Alt+Tab), à la demande du joueur (raccourci ou clic).
    /// Windows n'autorise ce changement que depuis l'application active : on se rattache brièvement au fil de la fenêtre
    /// active, sans envoyer aucune entrée clavier.
    /// </summary>
    public static void Focus(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        if (SetForegroundWindow(h)) return;
        uint target = GetWindowThreadProcessId(GetForegroundWindow(), out _), self = GetCurrentThreadId();
        if (target == self) return;
        AttachThreadInput(self, target, true);
        try { BringWindowToTop(h); SetForegroundWindow(h); }
        finally { AttachThreadInput(self, target, false); }
        // Dernier recours (Windows refuse parfois quand l'application n'est pas active) : bascule comme Alt+Tab
        if (GetForegroundWindow() != h) SwitchToThisWindow(h, true);
    }

    /// <summary>
    /// Agrandit la fenêtre (comme son bouton Agrandir ou Windows+↑), si le joueur a activé l'option. Ce n'est que de la
    /// gestion de fenêtre : aucune touche ni aucun clic n'est envoyé au jeu.
    /// </summary>
    public static void Maximize(IntPtr h)
    {
        if (!IsZoomed(h)) ShowWindow(h, SW_MAXIMIZE);
    }

    /// <summary>Fait clignoter la fenêtre dans la barre des tâches jusqu'à ce qu'elle soit affichée.</summary>
    public static void Flash(IntPtr h)
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = h, dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG, uCount = 5,
        };
        FlashWindowEx(ref info);
    }

    const int SW_RESTORE = 9, SW_MAXIMIZE = 3;
    const uint FLASHW_TRAY = 2, FLASHW_TIMERNOFG = 12;

    [StructLayout(LayoutKind.Sequential)]
    struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO info);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
