using System.IO;
using System.Text.Json;
using DofusBankManager.Data;

namespace DofusBankManager.Hdv;

/// <summary>Relevé de prix d'un objet à un instant donné (prix du lot le moins cher, en kamas).</summary>
public sealed class HdvSnapshot
{
    public DateTime Time { get; set; }
    public long? Lot1 { get; set; }
    public long? Lot10 { get; set; }
    public long? Lot100 { get; set; }
    public int Offers { get; set; }
}

public sealed class HdvPrice
{
    public int TemplateId { get; set; }
    public long? Lot1 { get; set; }
    public long? Lot10 { get; set; }
    public long? Lot100 { get; set; }
    /// <summary>Prix moyen indiqué par l'HDV (par unité).</summary>
    public long? Average { get; set; }
    public int Offers { get; set; }
    public DateTime SeenAt { get; set; }
    public List<HdvSnapshot> History { get; set; } = new();

    /// <summary>Meilleur prix par unité parmi les lots disponibles.</summary>
    public double? UnitPrice => new double?[] { Lot1, Lot10 / 10.0, Lot100 / 100.0 }.Where(p => p > 0).Min();
}

/// <summary>
/// Prix de l'hôtel de vente, relevés passivement quand le joueur consulte un HDV en jeu.
/// Aucune requête n'est envoyée au serveur (README, règles 1 et 3) : seuls les objets consultés ont un prix.
///
///   EHl{tpl}|{ligne}|{ligne}…   offres d'un objet, ligne = "id;effets;prix×1;prix×10;prix×100" (prix vide si aucun lot)
///   EHP{tpl}|{prix}              prix moyen d'un objet
/// </summary>
public sealed class HdvTracker
{
    const int MaxHistory = 50;

    static string HdvPath => Path.Combine(GameData.AppDir, "hdv.json");

    readonly GameData _data;

    public Dictionary<int, HdvPrice> Prices { get; private set; } = new();

    public event Action? Changed;
    public event Action<string>? Log;

    public HdvTracker(GameData data)
    {
        _data = data;
        Load();
    }

    /// <summary>Offres d'un objet (message EHl sans le préfixe).</summary>
    public void OnOffers(string body)
    {
        var parts = body.Split('|');
        if (!int.TryParse(parts[0], out var tpl)) return;

        long? min1 = null, min10 = null, min100 = null;
        int offers = 0;
        foreach (var line in parts.Skip(1))
        {
            // "id;effets;prix×1;prix×10;prix×100" : les effets utilisent ',' et '#', jamais ';'
            var f = line.Split(';');
            if (f.Length < 5 || !long.TryParse(f[0], out _)) continue;
            var p1 = ParsePrice(f[2]); var p10 = ParsePrice(f[3]); var p100 = ParsePrice(f[4]);
            min1 = Min(min1, p1); min10 = Min(min10, p10); min100 = Min(min100, p100);
            offers += (p1 != null ? 1 : 0) + (p10 != null ? 1 : 0) + (p100 != null ? 1 : 0);
        }

        var price = Get(tpl);
        price.Lot1 = min1; price.Lot10 = min10; price.Lot100 = min100;
        price.Offers = offers;
        price.SeenAt = DateTime.Now;

        // Un relevé par consultation ; on remplace le précédent s'il date de moins d'une minute
        var last = price.History.LastOrDefault();
        var snap = new HdvSnapshot { Time = DateTime.Now, Lot1 = min1, Lot10 = min10, Lot100 = min100, Offers = offers };
        if (last != null && DateTime.Now - last.Time < TimeSpan.FromMinutes(1)) price.History[^1] = snap;
        else price.History.Add(snap);
        if (price.History.Count > MaxHistory) price.History.RemoveRange(0, price.History.Count - MaxHistory);

        Log?.Invoke($"HDV {_data.GetItem(tpl).Name} : ×1 {Fmt(min1)}, ×10 {Fmt(min10)}, ×100 {Fmt(min100)}.");
        Save();
        Changed?.Invoke();
    }

    /// <summary>Prix moyen d'un objet (message EHP sans le préfixe).</summary>
    public void OnAverage(string body)
    {
        var parts = body.Split('|');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var tpl) || ParsePrice(parts[1]) is not { } avg) return;
        var price = Get(tpl);
        price.Average = avg;
        if (price.SeenAt == default) price.SeenAt = DateTime.Now;
        Save();
        Changed?.Invoke();
    }

    public void Forget(int templateId)
    {
        if (!Prices.Remove(templateId)) return;
        Save();
        Changed?.Invoke();
    }

    HdvPrice Get(int tpl)
    {
        if (!Prices.TryGetValue(tpl, out var p)) Prices[tpl] = p = new HdvPrice { TemplateId = tpl };
        return p;
    }

    static long? ParsePrice(string s) => long.TryParse(s, out var v) && v > 0 ? v : null;
    static long? Min(long? a, long? b) => a == null ? b : b == null ? a : Math.Min(a.Value, b.Value);
    static string Fmt(long? v) => v?.ToString("N0") ?? "—";

    void Load()
    {
        try
        {
            if (File.Exists(HdvPath))
                Prices = JsonSerializer.Deserialize<Dictionary<int, HdvPrice>>(File.ReadAllText(HdvPath)) ?? new();
        }
        catch { Prices = new(); }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(HdvPath, JsonSerializer.Serialize(Prices));
        }
        catch (Exception ex) { Log?.Invoke("Sauvegarde HDV impossible : " + ex.Message); }
    }
}
