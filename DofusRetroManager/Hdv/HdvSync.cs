using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hdv;

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
/// Partage des prix entre amis via le serveur du projet (dossier server/) : prix HDV des objets, prix des
/// cartes TTG chez les marchands, prix des pierres d'âme par monstre. Désactivé par défaut. N'envoie que
/// l'objet (ou le monstre), ses prix et la date du relevé, jamais le joueur ni le marchand (README, règle 4).
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
    readonly Ttg.MerchantTracker _merchants;
    readonly PdaTracker _pda;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly Dictionary<int, HdvPrice> _outbox = new();
    readonly List<(int Tpl, Ttg.MerchantSnapshot Snap)> _merchantOutbox = new();
    readonly Dictionary<string, PdaPrice> _pdaOutbox = new();
    DateTime _lastPullAt = DateTime.MinValue;
    bool _busy;

    public SyncSettings Settings { get; private set; } = new();
    public string Status { get; private set; } = "Partage désactivé.";

    public event Action? StatusChanged;
    public event Action<string>? Log;

    public HdvSync(HdvTracker hdv, Ttg.MerchantTracker merchants, PdaTracker pda)
    {
        _hdv = hdv;
        _merchants = merchants;
        _pda = pda;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"DofusRetroManager/{UpdateChecker.CurrentText}");
        Load();
        _hdv.Observed += p => Queue(() => _outbox[p.TemplateId] = p);
        _merchants.Observed += (tpl, s) => Queue(() => _merchantOutbox.Add((tpl, s)));
        _pda.Observed += p => Queue(() => _pdaOutbox[p.Key] = p);
        if (Settings.Enabled) SetStatus("Partage activé.");
    }

    bool OutboxEmpty => _outbox.Count == 0 && _merchantOutbox.Count == 0 && _pdaOutbox.Count == 0;

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
        {
            var since = DateTime.Now.AddDays(-30);
            foreach (var p in _hdv.Prices.Values.Where(p => !p.Shared && p.SeenAt > since))
                _outbox[p.TemplateId] = p;
            foreach (var m in _merchants.Prices.Values)
                foreach (var s in m.History.Where(s => !s.Shared && s.Time > since))
                    _merchantOutbox.Add((m.TemplateId, s));
            foreach (var p in _pda.Prices.Values.Where(p => !p.Shared && p.SeenAt > since))
                _pdaOutbox[p.Key] = p;
        }
        _lastPullAt = DateTime.MinValue;
        _unsupported = false; // autre serveur : on réessaie TTG et PDA
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
    async void Queue(Action add)
    {
        if (!Settings.Enabled) return;
        bool first = OutboxEmpty;
        add();
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
            if (!OutboxEmpty) await PushAsync();
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
        int sent = 0;

        if (_outbox.Count > 0)
        {
            var batch = _outbox.Values.Take(MaxBatch).ToList();
            var payload = batch.Select(p => new
            {
                tpl = p.TemplateId, lot1 = p.Lot1, lot10 = p.Lot10, lot100 = p.Lot100, avg = p.Average,
                seenAt = Ms(p.SeenAt),
            });
            if (await PostAsync("/prices", payload) != true) return;
            foreach (var p in batch) _outbox.Remove(p.TemplateId);
            sent += batch.Count;
        }

        if (_merchantOutbox.Count > 0)
        {
            var batch = _merchantOutbox.Take(MaxBatch).ToList();
            var payload = batch.Select(x => new { tpl = x.Tpl, price = x.Snap.Price, qty = x.Snap.Quantity, seenAt = Ms(x.Snap.Time) });
            var ok = await PostAsync("/merchant", payload);
            if (ok == false) return;
            _merchantOutbox.RemoveRange(0, batch.Count);
            if (ok == true) sent += batch.Count;
        }

        if (_pdaOutbox.Count > 0)
        {
            var batch = _pdaOutbox.Values.Take(MaxBatch).ToList();
            var payload = batch.Select(p => new
            {
                monster = p.MonsterId, category = PdaTracker.CategoryCode(p.Category), price = p.Price,
                offers = p.Offers, avg = p.Average, seenAt = Ms(p.SeenAt),
            });
            var ok = await PostAsync("/pda", payload);
            if (ok == false) return;
            foreach (var p in batch) _pdaOutbox.Remove(p.Key);
            if (ok == true) sent += batch.Count;
        }

        if (_unsupported) SetStatus("Partage actif pour l'HDV. Le serveur du groupe doit être mis à jour pour partager aussi les prix TTG et PDA (voir server/README.md).");
        else SetStatus($"Partage actif · {sent} prix envoyé(s) à {DateTime.Now:HH:mm}.");
    }

    /// <summary>Vrai si le serveur du groupe ne connaît pas encore les prix TTG et PDA (ancienne version).</summary>
    bool _unsupported;

    /// <summary>
    /// Envoie un lot. true : accepté ; false : refusé, le lot est gardé pour un prochain essai ;
    /// null : route inconnue d'un serveur pas encore mis à jour, le lot est abandonné.
    /// </summary>
    async Task<bool?> PostAsync(string path, object payload)
    {
        using var req = Request(HttpMethod.Post, path);
        req.Content = JsonContent.Create(payload);
        using var resp = await _http.SendAsync(req);
        if (resp.IsSuccessStatusCode) return true;
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) { _unsupported = true; return null; }
        SetStatus(ErrorText(resp));
        return false;
    }

    static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();

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

        // Absents d'un serveur pas encore mis à jour : rien à fusionner
        if (doc.RootElement.TryGetProperty("merchant", out var merchant))
            updated += _merchants.MergeShared(merchant.EnumerateArray().Select(e => (
                Tpl: e.GetProperty("tpl").GetInt32(), Price: e.GetProperty("price").GetInt64(), Qty: e.GetProperty("qty").GetInt32(),
                SeenAt: Date(e))).ToList());
        if (doc.RootElement.TryGetProperty("pda", out var pda))
            updated += _pda.MergeShared(pda.EnumerateArray().Select(e => (
                Monster: e.GetProperty("monster").GetInt32(), Category: e.GetProperty("category").GetInt32(),
                Price: Long(e, "price"), Offers: e.GetProperty("offers").GetInt32(), Average: Long(e, "avg"),
                SeenAt: Date(e))).ToList());

        Settings.LastPull = doc.RootElement.GetProperty("now").GetInt64();
        Save();
        if (updated > 0) Log?.Invoke($"Partage : {updated} prix reçus du groupe.");
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

    static DateTime Date(JsonElement e) => DateTimeOffset.FromUnixTimeMilliseconds(e.GetProperty("seenAt").GetInt64()).LocalDateTime;

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
