using System.IO;
using System.Text.RegularExpressions;

namespace DofusBankManager;

/// <summary>Une version du journal des changements : « ## 1.25.0 — 3 novembre 2026 » suivi de ses lignes « - … ».</summary>
public sealed record ChangelogEntry(string Version, string Date, IReadOnlyList<string> Items);

/// <summary>
/// Journal des changements (CHANGELOG.md à la racine du dépôt, intégré à l'exe). Format volontairement simple :
/// une section « ## version — date » par version, des puces « - », du gras « **…** ».
/// </summary>
public static class Changelog
{
    static IReadOnlyList<ChangelogEntry>? _entries;

    public static IReadOnlyList<ChangelogEntry> Entries => _entries ??= Load();

    static IReadOnlyList<ChangelogEntry> Load()
    {
        try
        {
            using var res = typeof(Changelog).Assembly.GetManifestResourceStream("CHANGELOG.md");
            return res == null ? [] : Parse(new StreamReader(res).ReadToEnd());
        }
        catch { return []; }
    }

    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        List<string>? items = null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var heading = Regex.Match(line, @"^##\s+(\d+\.\d+\.\d+)\s*(?:[—–-]\s*(.*))?$");
            if (heading.Success)
            {
                items = [];
                entries.Add(new ChangelogEntry(heading.Groups[1].Value, heading.Groups[2].Value.Trim(), items));
            }
            else if (line.StartsWith("#")) items = null; // autre titre : fin de la version
            else if (items == null) continue;
            else if (line.StartsWith("- ")) items.Add(line[2..].Trim());
            else if (line.StartsWith("  ") && line.Trim().Length > 0 && items.Count > 0) items[^1] += " " + line.Trim(); // suite d'une puce
        }
        return entries;
    }

    /// <summary>Découpe « texte **gras** texte » en morceaux (texte, gras ?).</summary>
    public static IEnumerable<(string Text, bool Bold)> Inline(string text)
    {
        var parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0) yield return (parts[i], i % 2 == 1);
    }
}
