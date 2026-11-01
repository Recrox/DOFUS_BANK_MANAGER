using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DofusBankManager.Data;

namespace DofusBankManager.Ttg;

/// <summary>Prix vu chez un marchand, à un instant donné (prix unitaire le plus bas de sa boutique, en kamas).</summary>
public sealed class MerchantSnapshot
{
    public DateTime Time { get; set; }
    public long Price { get; set; }
    public int Quantity { get; set; }
    /// <summary>Vrai si le relevé vient d'un autre membre du groupe (partage des prix).</summary>
    public bool Shared { get; set; }
}

public sealed class MerchantPrice
{
    public int TemplateId { get; set; }
    public List<MerchantSnapshot> History { get; set; } = new();

    [JsonIgnore] public MerchantSnapshot? Last => History.LastOrDefault();

    /// <summary>Meilleur prix vu ces 7 derniers jours (les marchands changent souvent).</summary>
    [JsonIgnore] public long? BestRecent => History.Where(s => DateTime.Now - s.Time < TimeSpan.FromDays(7))
        .Select(s => (long?)s.Price).Min();
}

/// <summary>
/// Prix des cartes et paquets de cartes TTG, relevés passivement quand le joueur consulte la boutique
/// d'un marchand en jeu. Aucune requête n'est envoyée (README, règles 1 et 3) : seules les boutiques
/// consultées donnent un prix. Le marchand n'est pas enregistré, seulement l'objet, son prix et la date.
/// </summary>
public sealed class MerchantTracker
{
    const int MaxHistory = 100;

    /// <summary>Carte commune, rare, épique, unique, paquet de cartes.</summary>
    public static readonly int[] CardTypes = [119, 120, 121, 122, 123];

    static string PricesPath => Path.Combine(GameData.AppDir, "marchands.json");

    readonly GameData _data;

    public Dictionary<int, MerchantPrice> Prices { get; private set; } = new();

    public event Action? Changed;
    public event Action<string>? Log;
    /// <summary>Relevé fait par le joueur lui-même (à partager avec le groupe, si le partage est activé).</summary>
    public event Action<int, MerchantSnapshot>? Observed;

    public MerchantTracker(GameData data)
    {
        _data = data;
        Load();
    }

    public bool IsCard(int tpl) => CardTypes.Contains(_data.GetItem(tpl).TypeId);

    /// <summary>Boutique d'un marchand : "uid;qté;tpl;effets;prix|…" (prix unitaire).</summary>
    public void OnShopList(string body)
    {
        var lots = new List<(int Tpl, int Qty, long Price)>();
        int unknown = 0;
        foreach (var line in body.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split(';');
            if (f.Length >= 5 && int.TryParse(f[1], out var qty) && int.TryParse(f[2], out var tpl)
                && long.TryParse(f[4], out var price) && price > 0 && _data.Items.ContainsKey(tpl))
                lots.Add((tpl, qty, price));
            else unknown++;
        }
        if (unknown > 0 && lots.Count == 0)
        {
            Log?.Invoke($"Marchand : format de boutique non reconnu ({body[..Math.Min(body.Length, 120)]}).");
            return;
        }

        // Un relevé par carte : le lot le moins cher de cette boutique
        var now = DateTime.Now;
        int recorded = 0;
        foreach (var g in lots.Where(l => IsCard(l.Tpl)).GroupBy(l => l.Tpl))
        {
            var best = g.MinBy(l => l.Price);
            if (!Prices.TryGetValue(g.Key, out var p)) Prices[g.Key] = p = new MerchantPrice { TemplateId = g.Key };
            // Même boutique rouverte aussitôt : pas de doublon
            if (p.Last is { } last && now - last.Time < TimeSpan.FromMinutes(1) && last.Price == best.Price) continue;
            var snap = new MerchantSnapshot { Time = now, Price = best.Price, Quantity = g.Sum(l => l.Qty) };
            p.History.Add(snap);
            if (p.History.Count > MaxHistory) p.History.RemoveRange(0, p.History.Count - MaxHistory);
            Observed?.Invoke(g.Key, snap);
            recorded++;
        }
        if (recorded == 0) return;
        Log?.Invoke($"Marchand : {recorded} prix de carte(s) TTG relevé(s).");
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Ajoute les relevés reçus du groupe à l'historique. Un relevé déjà connu (même carte, même prix, à la
    /// seconde près, y compris les siens renvoyés par le serveur) est ignoré. Retourne le nombre de relevés ajoutés.
    /// </summary>
    public int MergeShared(IEnumerable<(int Tpl, long Price, int Qty, DateTime SeenAt)> snapshots)
    {
        int added = 0;
        foreach (var r in snapshots)
        {
            if (!IsCard(r.Tpl)) continue;
            if (!Prices.TryGetValue(r.Tpl, out var p)) Prices[r.Tpl] = p = new MerchantPrice { TemplateId = r.Tpl };
            if (p.History.Any(s => s.Price == r.Price && (s.Time - r.SeenAt).Duration() < TimeSpan.FromSeconds(1))) continue;
            p.History.Add(new MerchantSnapshot { Time = r.SeenAt, Price = r.Price, Quantity = r.Qty, Shared = true });
            p.History.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (p.History.Count > MaxHistory) p.History.RemoveRange(0, p.History.Count - MaxHistory);
            added++;
        }
        if (added > 0)
        {
            Save();
            Changed?.Invoke();
        }
        return added;
    }

    public void Forget(int templateId)
    {
        if (!Prices.Remove(templateId)) return;
        Save();
        Changed?.Invoke();
    }

    void Load()
    {
        try
        {
            if (File.Exists(PricesPath))
                Prices = JsonSerializer.Deserialize<Dictionary<int, MerchantPrice>>(File.ReadAllText(PricesPath)) ?? new();
        }
        catch { Prices = new(); }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(PricesPath, JsonSerializer.Serialize(Prices));
        }
        catch (Exception ex) { Log?.Invoke("Sauvegarde des prix marchands impossible : " + ex.Message); }
    }
}
