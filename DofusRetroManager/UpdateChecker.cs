using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DofusRetroManager;

/// <summary>
/// Nouvelle version publiée. InstallerUrl : installateur joint à la Release (null s'il manque) ;
/// Sha256 : empreinte annoncée par GitHub, vérifiée après le téléchargement quand elle est fournie.
/// </summary>
public sealed record UpdateInfo(Version Latest, string PageUrl, string? InstallerUrl, string? Sha256, long Size);

/// <summary>
/// Vérifie si une version plus récente est publiée dans les Releases GitHub du projet, et télécharge son
/// installateur. Seules requêtes faites à GitHub : aucune donnée du joueur n'est envoyée (voir README, règle 4).
/// </summary>
public static class UpdateChecker
{
    const string Repo = "Recrox/DOFUS_RETRO_MANAGER";
    const string InstallerName = "DofusRetroManager-Setup.msi";
    public const string ReleasesPage = $"https://github.com/{Repo}/releases/latest";

    public static Version Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DofusRetroManager/{CurrentText}"); // exigé par l'API GitHub
        return http;
    }

    /// <summary>
    /// Vérification légère, faite toutes les 30 secondes : numéro de la dernière version publiée, lu dans la redirection
    /// de la page « latest » (requête HEAD, sans contenu). Elle ne passe pas par l'API GitHub, limitée à 60 requêtes
    /// par heure sans compte ; l'API (CheckAsync) n'est appelée que quand ce numéro dépasse la version installée.
    /// null : aucune version publiée.
    /// </summary>
    public static async Task<Version?> LatestVersionAsync()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DofusRetroManager/{CurrentText}");
        using var resp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, ReleasesPage));
        if ((int)resp.StatusCode is < 300 or >= 400) resp.EnsureSuccessStatusCode();
        // https://github.com/…/releases/tag/v1.28.0
        var tag = System.Text.RegularExpressions.Regex.Match(resp.Headers.Location?.ToString() ?? "", @"/tag/v?(\d+\.\d+\.\d+)$");
        return tag.Success ? Normalize(Version.Parse(tag.Groups[1].Value)) : null;
    }

    /// <summary>Retourne la nouvelle version si elle est plus récente, sinon null. Lève une exception si GitHub est injoignable.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var http = NewClient(TimeSpan.FromSeconds(8));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // aucune release publiée
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = Normalize(latest);
        if (latest <= Current) return null;

        var page = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;

        // Installateur joint à la Release : adresse, taille et empreinte (« sha256:… », fournie par GitHub)
        string? url = null, sha = null;
        long size = 0;
        if (doc.RootElement.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                if (a.GetProperty("name").GetString() != InstallerName) continue;
                url = a.GetProperty("browser_download_url").GetString();
                size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:"))
                    sha = digest["sha256:".Length..];
            }
        return new UpdateInfo(latest, page, url, sha, size);
    }

    /// <summary>
    /// Télécharge l'installateur dans le dossier temporaire et vérifie sa taille et son empreinte.
    /// Retourne le chemin du fichier. progress : 0 à 1.
    /// </summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken cancel)
    {
        if (update.InstallerUrl == null) throw new InvalidOperationException("Aucun installateur n'est joint à cette version.");
        var dir = Path.Combine(Path.GetTempPath(), "DofusRetroManager");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"DofusRetroManager-Setup-{update.Latest.ToString(3)}.msi");
        var part = path + ".part";

        // Déjà téléchargé lors d'un lancement précédent : réutilisé s'il est toujours intact
        if (File.Exists(path) && update.Sha256 != null)
        {
            try
            {
                await VerifyAsync(path, update, cancel);
                progress.Report(1);
                return path;
            }
            catch (InvalidDataException) { }
        }

        using (var http = NewClient(TimeSpan.FromMinutes(10)))
        using (var resp = await http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? update.Size;
            await using var input = await resp.Content.ReadAsStreamAsync(cancel);
            await using var output = File.Create(part);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancel)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                if (total > 0) progress.Report((double)done / total);
            }
        }

        await VerifyAsync(part, update, cancel);
        File.Move(part, path, overwrite: true);
        return path;
    }

    /// <summary>Fichier incomplet ou altéré : on ne l'installe pas (taille, puis empreinte si GitHub la fournit).</summary>
    static async Task VerifyAsync(string file, UpdateInfo update, CancellationToken cancel)
    {
        var length = new FileInfo(file).Length;
        if (update.Size > 0 && length != update.Size)
            throw new InvalidDataException($"Téléchargement incomplet ({length:N0} octets sur {update.Size:N0}).");
        if (update.Sha256 == null) return;
        await using var stream = File.OpenRead(file);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel));
        if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L'empreinte du fichier téléchargé ne correspond pas à celle publiée sur GitHub.");
    }

    /// <summary>
    /// Lance l'installateur téléchargé (Windows Installer, avec son interface habituelle).
    /// L'application doit se fermer ensuite pour que ses fichiers puissent être remplacés.
    /// </summary>
    public static void StartInstaller(string msiPath) =>
        Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\"") { UseShellExecute = false });

    /// <summary>Application installée par l'installateur (il pose LISEZMOI.md à côté de l'exe) : la mise à jour peut se faire sans assistant.</summary>
    public static bool CanUpdateSilently => File.Exists(Path.Combine(AppContext.BaseDirectory, "LISEZMOI.md"));

    /// <summary>Journal de Windows Installer de la dernière mise à jour automatique (diagnostic, sur ce PC).</summary>
    public static string UpdateLogPath => Path.Combine(Path.GetTempPath(), "DofusRetroManager", "update.log");

    /// <summary>Argument passé à l'application relancée après une mise à jour automatique : code de retour de msiexec.</summary>
    public const string UpdateResultArg = "--update-result=";

    /// <summary>
    /// Mise à jour sans assistant : un petit script attend la fermeture de l'application, installe la nouvelle version
    /// (msiexec /passive : barre de progression seule, rien à accepter) dans le dossier actuel, puis relance l'application
    /// avec le code de retour. L'application doit se fermer juste après l'appel.
    /// </summary>
    public static void StartSilentUpdate(string msiPath)
    {
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'"; // chaîne PowerShell littérale
        var exe = Environment.ProcessPath!;
        var msiArgs = $"/i \"{msiPath}\" /passive /norestart INSTALLFOLDER=\"{Path.GetDirectoryName(exe)}\" /l*v \"{UpdateLogPath}\"";
        var script = $"""
            # Mise à jour de Dofus Retro Manager : attendre sa fermeture, installer la nouvelle version, la relancer.
            Wait-Process -Id {Environment.ProcessId} -Timeout 60 -ErrorAction SilentlyContinue
            $p = Start-Process msiexec.exe -ArgumentList {Quote(msiArgs)} -Wait -PassThru
            Start-Process {Quote(exe)} -ArgumentList ('{UpdateResultArg}' + $p.ExitCode)
            """;
        var scriptPath = Path.Combine(Path.GetDirectoryName(msiPath)!, "update.ps1");
        // Avec BOM : Windows PowerShell lirait sinon les chemins accentués en ANSI
        File.WriteAllText(scriptPath, script, new System.Text.UTF8Encoding(true));
        // Sans shell, le script hérite des droits de l'application (administrateur) : pas de nouvelle demande
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"") { UseShellExecute = false, CreateNoWindow = true });
    }

    /// <summary>Supprime les installateurs téléchargés, une fois la mise à jour faite.</summary>
    public static void CleanDownloads()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(Path.GetTempPath(), "DofusRetroManager"), "DofusRetroManager-Setup-*"))
                File.Delete(f);
        }
        catch { }
        // Téléchargements de l'ancienne version (« Dofus Bank Manager », jusqu'à la 1.32)
        try { Directory.Delete(Path.Combine(Path.GetTempPath(), "DofusBankManager"), recursive: true); }
        catch { }
    }

    /// <summary>
    /// Ouvre une page dans le navigateur. L'application tourne en administrateur :
    /// passer par l'explorateur évite d'ouvrir le navigateur avec les droits élevés.
    /// </summary>
    public static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = false });

    static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
