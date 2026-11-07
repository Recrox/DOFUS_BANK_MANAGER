using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DofusRetroManager.Data;

namespace DofusRetroManager.Ttg;

/// <summary>Prix vu chez un marchand, à un instant donné (prix unitaire le plus bas de sa boutique, en kamas).</summary>
public sealed class MerchantSnapshot
{
    public DateTime Time { get; set; }
    public long Price { get; set; }
    public int Quantity { get; set; }
    /// <summary>Vrai si le relevé vient d'un autre membre du groupe (partage des prix).</summary>
    public bool Shared { get; set; }
    /// <summary>
    /// Pseudo du marchand, sa map et sa case : gardés sur ce PC seulement, pour retrouver la boutique.
    /// Jamais envoyés au partage entre amis (HdvSync n'envoie que la carte, le prix, la quantité et la date).
    /// </summary>
    public string? Merchant { get; set; }
    public int? MapId { get; set; }
    public int? Cell { get; set; }
}

public sealed class MerchantPrice
{
    public int TemplateId { get; set; }
    public List<MerchantSnapshot> History { get; set; } = new();

    [JsonIgnore] public MerchantSnapshot? Last => History.LastOrDefault();

    /// <summary>Relevés de ces derniers jours (les marchands changent souvent).</summary>
    public IEnumerable<MerchantSnapshot> Recent(int days) => History.Where(s => DateTime.Now - s.Time < TimeSpan.FromDays(days));

    /// <summary>Meilleur prix vu ces derniers jours.</summary>
    public long? BestRecent(int days) => Recent(days).Select(s => (long?)s.Price).Min();
}

/// <summary>Marchand présent sur la map : pseudo et case.</summary>
sealed record MapMerchant(string Name, int Cell);

/// <summary>
/// Prix des cartes et paquets de cartes TTG, relevés passivement quand le joueur consulte la boutique
/// d'un marchand en jeu. Aucune requête n'est envoyée (REGLES.md, règles 1 et 3) : seules les boutiques
/// consultées donnent un prix. Le pseudo et la position du marchand (lus sur la map, message GM de type -5)
/// sont gardés sur ce PC pour retrouver la boutique ; ils ne sont jamais partagés.
/// </summary>
public sealed class MerchantTracker
{
    const int MaxHistory = 100;
    /// <summary>Une même carte chez un même marchand n'est relevée qu'une fois dans ce délai.</summary>
    static readonly TimeSpan MerchantRepeat = TimeSpan.FromHours(1);

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

    /// <summary>Par fenêtre de jeu (multi-compte) : sa map, ses marchands visibles, la boutique ouverte.</summary>
    sealed class Window
    {
        public int? MapId;
        public Dictionary<long, MapMerchant> Merchants = new();
        public (string Name, int Cell, int? MapId)? Shop;
    }

    readonly Dictionary<string, Window> _windows = new();

    /// <summary>Connexion du message en cours de traitement (à renseigner avant chaque message).</summary>
    public string Stream { get; set; } = "";

    Window Current => _windows.TryGetValue(Stream, out var w) ? w : _windows[Stream] = new Window();

    public void OnStreamClosed(string stream) => _windows.Remove(stream);

    public void OnMapChanged(string mapId)
    {
        var w = Current;
        w.MapId = int.TryParse(mapId, out var id) ? id : null;
        w.Merchants.Clear();
    }

    /// <summary>
    /// Sprites de la map : seuls les marchands sont retenus, "+cellule;direction;…;id;pseudo;-5;…" ;
    /// "-id" quand l'un d'eux part. Les autres sprites sont ignorés.
    /// </summary>
    public void OnSprites(string body)
    {
        var w = Current;
        foreach (var s in body.Split('|'))
        {
            if (s.Length < 2) continue;
            if (s[0] == '-')
            {
                if (long.TryParse(s.AsSpan(1), out var gone)) w.Merchants.Remove(gone);
                continue;
            }
            if (s[0] is not ('+' or '~')) continue;
            var f = s[1..].Split(';');
            if (f.Length >= 6 && f[5] == "-5" && long.TryParse(f[3], out var id) && int.TryParse(f[0], out var cell))
                w.Merchants[id] = new MapMerchant(f[4], cell);
        }
    }

    /// <summary>Boutique ouverte : le marchand est retrouvé parmi les sprites de la map.</summary>
    public void OnShopOpened(long seller)
    {
        var w = Current;
        w.Shop = w.Merchants.TryGetValue(seller, out var m) ? (m.Name, m.Cell, w.MapId) : (null!, 0, w.MapId);
    }

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
        var shop = Current.Shop;
        var now = DateTime.Now;
        int recorded = 0;
        foreach (var g in lots.Where(l => IsCard(l.Tpl)).GroupBy(l => l.Tpl))
        {
            var best = g.MinBy(l => l.Price);
            if (!Prices.TryGetValue(g.Key, out var p)) Prices[g.Key] = p = new MerchantPrice { TemplateId = g.Key };
            // Même boutique rouverte aussitôt : pas de doublon
            if (p.Last is { } last && now - last.Time < TimeSpan.FromMinutes(1) && last.Price == best.Price) continue;
            // Même carte chez le même marchand vue il y a moins d'une heure : relevé inchangé
            if (shop?.Name is { } seller && p.History.Any(s => s.Merchant == seller && now - s.Time < MerchantRepeat)) continue;
            var snap = new MerchantSnapshot
            {
                Time = now, Price = best.Price, Quantity = g.Sum(l => l.Qty),
                Merchant = shop?.Name, MapId = shop?.MapId, Cell = shop?.Name != null ? shop.Value.Cell : null,
            };
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
