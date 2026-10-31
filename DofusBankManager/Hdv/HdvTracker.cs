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
    /// <summary>Vrai si le dernier relevé vient d'un autre membre du groupe (partage des prix).</summary>
    public bool Shared { get; set; }
    public List<HdvSnapshot> History { get; set; } = new();

    /// <summary>Meilleur prix par unité parmi les lots disponibles.</summary>
    public double? UnitPrice => new double?[] { Lot1, Lot10 / 10.0, Lot100 / 100.0 }.Where(p => p > 0).Min();
}

/// <summary>
/// Prix de l'hôtel de vente, relevés passivement quand le joueur consulte un HDV en jeu.
/// Aucune requête n'est envoyée au serveur (README, règles 1 et 3) : seuls les objets consultés ont un prix.
///
/// Formats observés sur Retro (prix écrits "547,0") :
///   EHl{tpl}|{offre}|{offre}…           offres d'un objet, offre = "id;effets;×1;×10;×100;tpl"
///   EHm+{id}|{tpl}|{effets}|{×1}|{×10}|{×100}   offre ajoutée ou modifiée pendant la consultation
///   EHm-{id}                            offre retirée (vendue, annulée)
///   EHP{tpl}|{prix}                     prix moyen d'un objet (par unité)
/// </summary>
public sealed class HdvTracker
{
    const int MaxHistory = 50;

    static string HdvPath => Path.Combine(GameData.AppDir, "hdv.json");

    readonly GameData _data;

    /// <summary>Offres actuellement affichées en jeu, par objet puis par identifiant d'offre.</summary>
    readonly Dictionary<int, Dictionary<long, (long? Lot1, long? Lot10, long? Lot100)>> _offers = new();
    readonly Dictionary<long, int> _templateOfOffer = new();

    public Dictionary<int, HdvPrice> Prices { get; private set; } = new();

    public event Action? Changed;
    public event Action<string>? Log;
    /// <summary>Prix relevé par le joueur lui-même (à partager avec le groupe, si le partage est activé).</summary>
    public event Action<HdvPrice>? Observed;

    public HdvTracker(GameData data)
    {
        _data = data;
        Load();
    }

    /// <summary>Offres d'un objet (message EHl sans le préfixe) : remplacent les offres connues.</summary>
    public void OnOffers(string body)
    {
        var parts = body.Split('|');
        if (!int.TryParse(parts[0], out var tpl)) return;

        var lines = new Dictionary<long, (long?, long?, long?)>();
        foreach (var line in parts.Skip(1))
        {
            // "id;effets;×1;×10;×100;tpl" : les effets utilisent ',' et '#', jamais ';'
            var f = line.Split(';');
            if (f.Length < 5 || !long.TryParse(f[0], out var id)) continue;
            lines[id] = (ParsePrice(f[2]), ParsePrice(f[3]), ParsePrice(f[4]));
        }
        foreach (var id in _offers.GetValueOrDefault(tpl)?.Keys ?? Enumerable.Empty<long>()) _templateOfOffer.Remove(id);
        foreach (var id in lines.Keys) _templateOfOffer[id] = tpl;
        _offers[tpl] = lines;

        if (lines.Count == 0 && parts.Length > 1)
            Log?.Invoke($"HDV : offres non reconnues pour {_data.GetItem(tpl).Name} : {body[..Math.Min(body.Length, 160)]}");
        Record(tpl);
    }

    /// <summary>Offre ajoutée, modifiée ou retirée pendant la consultation (message EHm sans le préfixe).</summary>
    public void OnOfferMove(string body)
    {
        if (body.Length < 2) return;
        var f = body[1..].Split('|');
        if (!long.TryParse(f[0], out var id)) return;

        if (body[0] == '+' && f.Length >= 6 && int.TryParse(f[1], out var tpl))
        {
            if (!_offers.TryGetValue(tpl, out var lines)) _offers[tpl] = lines = new();
            lines[id] = (ParsePrice(f[3]), ParsePrice(f[4]), ParsePrice(f[5]));
            _templateOfOffer[id] = tpl;
            Record(tpl);
        }
        else if (body[0] == '-' && _templateOfOffer.Remove(id, out var removedTpl))
        {
            _offers[removedTpl].Remove(id);
            Record(removedTpl);
        }
    }

    /// <summary>Enregistre le lot le moins cher de chaque taille, d'après les offres connues de l'objet.</summary>
    void Record(int tpl)
    {
        var lines = _offers.GetValueOrDefault(tpl)?.Values.ToList() ?? new();
        long? min1 = lines.Min(l => l.Lot1), min10 = lines.Min(l => l.Lot10), min100 = lines.Min(l => l.Lot100);
        int offers = lines.Sum(l => (l.Lot1 != null ? 1 : 0) + (l.Lot10 != null ? 1 : 0) + (l.Lot100 != null ? 1 : 0));

        var price = Get(tpl);
        price.Lot1 = min1; price.Lot10 = min10; price.Lot100 = min100;
        price.Offers = offers;
        price.SeenAt = DateTime.Now;
        price.Shared = false;
        AddSnapshot(price, offers);

        Log?.Invoke($"HDV {_data.GetItem(tpl).Name} : ×1 {Fmt(min1)}, ×10 {Fmt(min10)}, ×100 {Fmt(min100)}.");
        Save();
        Observed?.Invoke(price);
        Changed?.Invoke();
    }

    /// <summary>Un relevé par consultation : on remplace le précédent s'il date de moins d'une minute.</summary>
    static void AddSnapshot(HdvPrice price, int offers)
    {
        var last = price.History.LastOrDefault();
        var snap = new HdvSnapshot { Time = price.SeenAt, Lot1 = price.Lot1, Lot10 = price.Lot10, Lot100 = price.Lot100, Offers = offers };
        if (last != null && (price.SeenAt - last.Time).Duration() < TimeSpan.FromMinutes(1)) price.History[^1] = snap;
        else price.History.Add(snap);
        if (price.History.Count > MaxHistory) price.History.RemoveRange(0, price.History.Count - MaxHistory);
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
        Observed?.Invoke(price);
        Changed?.Invoke();
    }

    /// <summary>
    /// Fusionne les prix reçus du groupe : un relevé distant ne remplace le local que s'il est plus récent.
    /// Retourne le nombre d'objets mis à jour.
    /// </summary>
    public int MergeShared(IEnumerable<(int Tpl, long? Lot1, long? Lot10, long? Lot100, long? Average, DateTime SeenAt)> prices)
    {
        int updated = 0;
        foreach (var r in prices)
        {
            if (!_data.Items.ContainsKey(r.Tpl)) continue;
            var price = Get(r.Tpl);
            if (r.SeenAt <= price.SeenAt) continue;
            price.Lot1 = r.Lot1; price.Lot10 = r.Lot10; price.Lot100 = r.Lot100;
            if (r.Average != null) price.Average = r.Average;
            price.SeenAt = r.SeenAt;
            price.Offers = 0;
            price.Shared = true;
            AddSnapshot(price, 0);
            updated++;
        }
        if (updated > 0)
        {
            Save();
            Changed?.Invoke();
        }
        return updated;
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

    /// <summary>Prix au format "547,0" (ou "547") ; vide ou 0 = aucun lot.</summary>
    static long? ParsePrice(string s) => long.TryParse(s.Split(',')[0], out var v) && v > 0 ? v : null;
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
