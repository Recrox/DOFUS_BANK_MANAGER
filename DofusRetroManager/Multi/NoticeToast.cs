using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DofusRetroManager.Multi;

/// <summary>
/// Bulle de notification en bas à droite de l'écran. Elle ne prend pas le focus (le jeu reste actif) et disparaît
/// seule ; un clic affiche la fenêtre de jeu concernée. Plusieurs bulles s'empilent, sur l'écran de la fenêtre de jeu
/// concernée (sinon de la fenêtre au premier plan) : avec le jeu sur un second écran, la bulle reste visible.
/// </summary>
public sealed class NoticeToast : Window
{
    static readonly List<NoticeToast> Open = new();
    const double Gap = 10, Width_ = 320;
    /// <summary>Fenêtre dont l'écran reçoit la pile de bulles (la dernière demandée).</summary>
    static IntPtr _screenOf;

    /// <summary>Bulle permanente (archi-monstre) : elle ne disparaît qu'avec sa croix.</summary>
    readonly bool _sticky;

    NoticeToast(string title, string text, Action? onClick, bool sticky, ImageSource? icon)
    {
        _sticky = sticky;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = Width_;
        Cursor = System.Windows.Input.Cursors.Hand;

        var accent = new Border { Width = 5, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 12, 0) };
        accent.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var titleBlock = new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "AccentSolid");
        var textBlock = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var stack = new StackPanel();
        stack.Children.Add(titleBlock);
        stack.Children.Add(textBlock);
        var row = new DockPanel();
        DockPanel.SetDock(accent, Dock.Left);
        row.Children.Add(accent);
        // Croix de fermeture (bulle permanente) : son clic ferme la bulle sans afficher la fenêtre du jeu
        var close = new TextBlock { Text = "✕", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(10, -2, 0, 0),
            VerticalAlignment = VerticalAlignment.Top, ToolTip = "Fermer" };
        close.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        // Illustration du monstre (archi-monstre, alerte monstre), à gauche du texte
        if (icon != null)
        {
            var image = new Image { Source = icon, Width = 46, Height = 46, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(image, Dock.Left);
            row.Children.Add(image);
        }
        row.Children.Add(stack);

        var card = new Border
        {
            Child = row, Padding = new Thickness(12), BorderThickness = new Thickness(1), Margin = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface2");
        card.SetResourceReference(Border.BorderBrushProperty, "AccentBorder");
        card.SetResourceReference(Border.CornerRadiusProperty, "RadiusLg");
        Content = card;
        SetResourceReference(FontFamilyProperty, "AppFont");

        // Clic sur la bulle : la fenêtre du jeu s'affiche ; une bulle permanente reste jusqu'à sa croix
        MouseLeftButtonUp += (_, _) => { onClick?.Invoke(); if (!_sticky) Close(); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => { timer.Stop(); FadeOut(); };
        Loaded += (_, _) =>
        {
            PlaceAll(); // la nouvelle bulle se place en bas, les autres remontent
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            if (!_sticky) timer.Start();
        };
        // Écran d'une autre résolution (DPI) : la hauteur change, la pile est recalculée
        SizeChanged += (_, _) => { if (IsLoaded) PlaceAll(); };
        Closed += (_, _) => { Open.Remove(this); PlaceAll(); };
    }

    /// <param name="screenOf">Fenêtre de jeu concernée : la bulle s'affiche sur son écran (sinon celui de la fenêtre au premier plan).</param>
    /// <param name="sticky">Bulle permanente : pas de disparition automatique, seulement sa croix.</param>
    /// <param name="icon">Image à gauche du texte (ex. illustration du monstre), facultative.</param>
    public static void Show(string title, string text, Action? onClick, IntPtr screenOf = default, bool sticky = false, ImageSource? icon = null)
    {
        _screenOf = screenOf != IntPtr.Zero ? screenOf : GetForegroundWindow();
        // Pas plus de 4 bulles passagères à la fois : la plus ancienne laisse sa place (les permanentes restent)
        if (Open.Where(t => !t._sticky).ToList() is { Count: >= 4 } passing) passing[0].Close();
        var toast = new NoticeToast(title, text, onClick, sticky, icon);
        Open.Add(toast);
        toast.Show();
    }

    /// <summary>
    /// Empile les bulles au-dessus de la barre des tâches de l'écran choisi, la plus récente en bas. Positions en pixels
    /// (écrans de résolutions différentes) ; chaque bulle est remise au premier plan, au-dessus d'un jeu en plein écran.
    /// </summary>
    static void PlaceAll()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromWindow(_screenOf, MonitorDefaultToPrimary);
        if (!GetMonitorInfo(monitor, ref info)) return;
        var area = info.Work;
        int bottom = area.Bottom;
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var t = Open[i];
            var hwnd = new WindowInteropHelper(t).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) continue;
            double scale = (r.Bottom - r.Top) / Math.Max(1, t.ActualHeight);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            // 12 de marge transparente (pour l'ombre) sous et sur chaque carte : écart visible = Gap
            int top = bottom - h;
            SetWindowPos(hwnd, HwndTopmost, area.Right - w - 4, top, 0, 0, SwpNoSize | SwpNoActivate);
            bottom = top + (int)Math.Round((24 - Gap) * scale);
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    const uint MonitorDefaultToPrimary = 1, SwpNoSize = 0x0001, SwpNoActivate = 0x0010;
    static readonly IntPtr HwndTopmost = new(-1);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    void FadeOut()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
