using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using DofusBankManager.Data;

namespace DofusBankManager.Hdv;

public sealed class SyncSettings
{
    /// <summary>Serveur du groupe (voir server/README.md). Public : seul le code de groupe protège l'accès.</summary>
    public const string DefaultServerUrl = "https://dofus-bank-manager-hdv.recrox.workers.dev";

    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = DefaultServerUrl;
    public string GroupKey { get; set; } = "";
    /// <summary>Horloge du serveur lors de la dernière lecture : on ne redemande que les nouveautés.</summary>
    public long LastPull { get; set; }
}

/// <summary>
/// Partage des prix HDV entre amis via le serveur du projet (dossier server/).
/// Désactivé par défaut. N'envoie que l'objet, ses prix et la date du relevé (README, règle 4).
/// </summary>
public sealed class HdvSync
{
    /// <summary>Fréquence de lecture des relevés du groupe (~240 requêtes/h par application ouverte).</summary>
    static readonly TimeSpan PullInterval = TimeSpan.FromSeconds(15);
    /// <summary>Délai avant l'envoi d'un relevé, pour regrouper les consultations rapprochées.</summary>
    static readonly TimeSpan PushDelay = TimeSpan.FromSeconds(1);
    const int MaxBatch = 1000;

    static string SettingsPath => Path.Combine(GameData.AppDir, "partage.json");

    readonly HdvTracker _hdv;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly Dictionary<int, HdvPrice> _outbox = new();
    DateTime _lastPullAt = DateTime.MinValue;
    bool _busy;

    public SyncSettings Settings { get; private set; } = new();
    public string Status { get; private set; } = "Partage désactivé.";

    public event Action? StatusChanged;
    public event Action<string>? Log;

    public HdvSync(HdvTracker hdv)
    {
        _hdv = hdv;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"DofusBankManager/{UpdateChecker.CurrentText}");
        Load();
        _hdv.Observed += OnObserved;
        if (Settings.Enabled) SetStatus("Partage activé.");
    }

    public void Configure(bool enabled, string serverUrl, string groupKey)
    {
        serverUrl = serverUrl.Trim().TrimEnd('/');
        groupKey = groupKey.Trim();
        if (serverUrl != Settings.ServerUrl || groupKey != Settings.GroupKey) Settings.LastPull = 0; // nouveau groupe : tout relire
        bool justEnabled = enabled && !Settings.Enabled;
        Settings.Enabled = enabled;
        Settings.ServerUrl = serverUrl;
        Settings.GroupKey = groupKey;
        Save();

        if (justEnabled) // on partage aussi les relevés faits avant l'activation
            foreach (var p in _hdv.Prices.Values.Where(p => !p.Shared && p.SeenAt > DateTime.Now.AddDays(-30)))
                _outbox[p.TemplateId] = p;
        _lastPullAt = DateTime.MinValue;
        SetStatus(enabled ? "Partage activé." : "Partage désactivé.");
    }

    /// <summary>Vérifie l'adresse et le code de groupe.</summary>
    public async Task<string> TestAsync(string serverUrl, string groupKey)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{serverUrl.Trim().TrimEnd('/')}/health");
            req.Headers.Add("X-Group-Key", groupKey.Trim());
            using var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode ? "Connexion réussie."
                : resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Code de groupe refusé."
                : $"Le serveur a répondu {(int)resp.StatusCode}.";
        }
        catch (Exception ex) { return "Serveur injoignable : " + ex.Message; }
    }

    /// <summary>Nouveau relevé du joueur : envoyé au groupe presque immédiatement.</summary>
    async void OnObserved(HdvPrice price)
    {
        if (!Settings.Enabled) return;
        bool first = _outbox.Count == 0;
        _outbox[price.TemplateId] = price;
        if (!first) return; // un envoi est déjà programmé
        await Task.Delay(PushDelay);
        await TickAsync();
    }

    /// <summary>Synchronisation immédiate (bouton « Synchroniser »).</summary>
    public Task SyncNowAsync()
    {
        _lastPullAt = DateTime.MinValue;
        return TickAsync();
    }

    /// <summary>Appelé régulièrement : envoie les nouveaux relevés, et récupère ceux du groupe toutes les 15 secondes.</summary>
    public async Task TickAsync()
    {
        if (_busy || !Settings.Enabled || Settings.ServerUrl.Length == 0 || Settings.GroupKey.Length == 0) return;
        _busy = true;
        try
        {
            if (_outbox.Count > 0) await PushAsync();
            if (DateTime.Now - _lastPullAt > PullInterval) await PullAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Partage : serveur injoignable (" + ex.Message + ").");
        }
        finally { _busy = false; }
    }

    async Task PushAsync()
    {
        var batch = _outbox.Values.Take(MaxBatch).ToList();
        var payload = batch.Select(p => new
        {
            tpl = p.TemplateId, lot1 = p.Lot1, lot10 = p.Lot10, lot100 = p.Lot100, avg = p.Average,
            seenAt = new DateTimeOffset(p.SeenAt).ToUnixTimeMilliseconds(),
        });
        using var req = Request(HttpMethod.Post, "/prices");
        req.Content = JsonContent.Create(payload);
        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) { SetStatus(ErrorText(resp)); return; }
        foreach (var p in batch) _outbox.Remove(p.TemplateId);
        SetStatus($"Partage actif · {batch.Count} prix envoyé(s) à {DateTime.Now:HH:mm}.");
    }

    async Task PullAsync()
    {
        using var req = Request(HttpMethod.Get, $"/prices?since={Settings.LastPull}");
        using var resp = await _http.SendAsync(req);
        _lastPullAt = DateTime.Now;
        if (!resp.IsSuccessStatusCode) { SetStatus(ErrorText(resp)); return; }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var prices = doc.RootElement.GetProperty("prices").EnumerateArray().Select(e => (
            Tpl: e.GetProperty("tpl").GetInt32(),
            Lot1: Long(e, "lot1"), Lot10: Long(e, "lot10"), Lot100: Long(e, "lot100"), Average: Long(e, "avg"),
            SeenAt: DateTimeOffset.FromUnixTimeMilliseconds(e.GetProperty("seenAt").GetInt64()).LocalDateTime)).ToList();

        int updated = _hdv.MergeShared(prices);
        Settings.LastPull = doc.RootElement.GetProperty("now").GetInt64();
        Save();
        if (updated > 0) Log?.Invoke($"Partage HDV : {updated} prix reçus du groupe.");
        SetStatus($"Partage actif · synchronisé à {DateTime.Now:HH:mm}" + (updated > 0 ? $" ({updated} prix reçus)." : "."));
    }

    HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, Settings.ServerUrl + path);
        req.Headers.Add("X-Group-Key", Settings.GroupKey);
        return req;
    }

    static string ErrorText(HttpResponseMessage resp) =>
        resp.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? "Partage : code de groupe refusé."
            : $"Partage : le serveur a répondu {(int)resp.StatusCode}.";

    static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

    void SetStatus(string status)
    {
        if (status == Status) return;
        Status = status;
        StatusChanged?.Invoke();
    }

    void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Settings = JsonSerializer.Deserialize<SyncSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { Settings = new(); }
        if (string.IsNullOrWhiteSpace(Settings.ServerUrl)) Settings.ServerUrl = SyncSettings.DefaultServerUrl;
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings));
        }
        catch { }
    }
}
