using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DofusRetroManager.Multi;

/// <summary>
/// Bulle de notification en bas à droite de l'écran. Elle ne prend pas le focus (le jeu reste actif) et disparaît
/// seule ; un clic affiche la fenêtre de jeu concernée. Plusieurs bulles s'empilent.
/// </summary>
public sealed class NoticeToast : Window
{
    static readonly List<NoticeToast> Open = new();
    const double Gap = 10, Width_ = 320;

    NoticeToast(string title, string text, Action? onClick)
    {
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

        MouseLeftButtonUp += (_, _) => { onClick?.Invoke(); Close(); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => { timer.Stop(); FadeOut(); };
        Loaded += (_, _) =>
        {
            foreach (var t in Open) t.Place(); // la nouvelle bulle se place en bas, les autres remontent
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            timer.Start();
        };
        Closed += (_, _) => { Open.Remove(this); foreach (var t in Open) t.Place(); };
    }

    public static void Show(string title, string text, Action? onClick)
    {
        // Pas plus de 4 bulles à la fois : la plus ancienne laisse sa place
        if (Open.Count >= 4) Open[0].Close();
        var toast = new NoticeToast(title, text, onClick);
        Open.Add(toast);
        toast.Show();
    }

    /// <summary>Empile les bulles au-dessus de la barre des tâches, la plus récente en bas.</summary>
    void Place()
    {
        var area = SystemParameters.WorkArea;
        double bottom = area.Bottom;
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var t = Open[i];
            bottom -= t.ActualHeight;
            if (t == this) break;
            bottom += 24 - Gap; // 12 de marge transparente (pour l'ombre) sous et sur chaque carte : écart visible = Gap
        }
        Left = area.Right - Width - 4;
        Top = bottom;
    }

    void FadeOut()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
