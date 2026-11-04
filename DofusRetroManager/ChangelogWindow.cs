using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DofusRetroManager;

/// <summary>Fenêtre « Nouveautés » : le journal des changements intégré à l'exe, version par version, aux couleurs du thème.</summary>
public sealed class ChangelogWindow : Window
{
    public ChangelogWindow()
    {
        Title = "Nouveautés — Dofus Retro Manager";
        Width = 640;
        Height = 720;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(ForegroundProperty, "Text");
        SetResourceReference(FontFamilyProperty, "AppFont");
        FontSize = 13;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var list = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };
        var title = new TextBlock { Text = "Nouveautés", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AccentSolid");
        list.Children.Add(title);
        list.Children.Add(Muted($"Vous utilisez la version {UpdateChecker.CurrentText}.", new Thickness(0, 0, 0, 14)));

        var entries = Changelog.Entries;
        if (entries.Count == 0) list.Children.Add(Muted("Le journal des changements est introuvable.", default));
        foreach (var entry in entries) list.Children.Add(Card(entry));

        Content = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    static Border Card(ChangelogEntry entry)
    {
        bool current = entry.Version == UpdateChecker.CurrentText;
        var panel = new StackPanel();

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var version = new TextBlock { Text = "Version " + entry.Version, FontSize = 16, FontWeight = FontWeights.Bold };
        version.SetResourceReference(TextBlock.ForegroundProperty, "AccentSolid");
        head.Children.Add(version);
        if (current)
        {
            var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = "installée", FontSize = 11, FontWeight = FontWeights.SemiBold } };
            chip.SetResourceReference(Border.BorderBrushProperty, "AccentBorder");
            head.Children.Add(chip);
        }
        var date = Muted(entry.Date, default);
        date.HorizontalAlignment = HorizontalAlignment.Right;
        date.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(date);
        panel.Children.Add(head);

        foreach (var item in entry.Items)
        {
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            line.Inlines.Add(new Run("•  "));
            foreach (var (text, bold) in Changelog.Inline(item))
                line.Inlines.Add(new Run(text) { FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal });
            panel.Children.Add(line);
        }

        var card = new Border { Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(current ? 2 : 1), Child = panel };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.SetResourceReference(Border.BorderBrushProperty, current ? "AccentBorder" : "Border");
        card.SetResourceReference(Border.CornerRadiusProperty, "RadiusLg");
        return card;
    }

    static TextBlock Muted(string text, Thickness margin)
    {
        var t = new TextBlock { Text = text, Margin = margin, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return t;
    }
}
