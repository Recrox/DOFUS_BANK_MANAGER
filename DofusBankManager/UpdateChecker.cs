using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DofusBankManager;

public sealed record UpdateInfo(Version Latest, string PageUrl);

/// <summary>
/// Vérifie si une version plus récente est publiée dans les Releases GitHub du projet.
/// Seule requête faite à GitHub : aucune donnée du joueur n'est envoyée (voir README, règle 4).
/// </summary>
public static class UpdateChecker
{
    const string Repo = "Recrox/DOFUS_BANK_MANAGER";
    public const string ReleasesPage = $"https://github.com/{Repo}/releases/latest";

    public static Version Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    /// <summary>Retourne la nouvelle version si elle est plus récente, sinon null. Lève une exception si GitHub est injoignable.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DofusBankManager/{CurrentText}"); // exigé par l'API GitHub
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // aucune release publiée
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = Normalize(latest);

        var page = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
        return latest > Current ? new UpdateInfo(latest, page) : null;
    }

    /// <summary>
    /// Ouvre une page dans le navigateur. L'application tourne en administrateur :
    /// passer par l'explorateur évite d'ouvrir le navigateur avec les droits élevés.
    /// </summary>
    public static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = false });

    static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
