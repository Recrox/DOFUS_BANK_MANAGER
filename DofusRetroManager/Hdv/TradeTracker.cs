using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hdv;

/// <summary>Achat ou vente d'un de vos personnages. Prix : total payé ou demandé pour le lot.</summary>
public sealed class Trade
{
    /// <summary>Achat (HDV ou boutique d'un marchand) ou offre mise en vente à l'HDV.</summary>
    public string Kind { get; set; } = Purchase;
    /// <summary>« HDV » ou « Marchand » (le marchand n'est jamais enregistré).</summary>
    public string Source { get; set; } = "HDV";
    public DateTime Time { get; set; }
    public string Character { get; set; } = "";
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public long Price { get; set; }
    /// <summary>Offre : identifiant de l'objet mis en vente, taxe payée, état et date de fin (vente ou retrait).</summary>
    public long ListingId { get; set; }
    public long Tax { get; set; }
    public string State { get; set; } = OnSale;
    public DateTime? ClosedAt { get; set; }
    /// <summary>Vente constatée à l'ouverture du mode vente (offre disparue) : faite entre la dernière visite et ClosedAt.</summary>
    public bool SoldUnseen { get; set; }

    public const string Purchase = "Achat", Listing = "Offre";
    public const string OnSale = "En vente", Sold = "Vendue", Withdrawn = "Retirée", Expired = "Expirée";
}

sealed class TradeData
{
    public List<Trade> Trades { get; set; } = new();
}

/// <summary>
/// « Mes échanges » (onglet HDV) : achats et ventes de vos personnages, déduits des messages déjà lus pour l'HDV, la
/// boutique des marchands et l'inventaire (objets, quantités et prix ; aucun nom de vendeur ni d'acheteur).
///   Achat à l'HDV : un lot disparaît d'une offre consultée (EHm) et l'inventaire gagne la même quantité de l'objet.
///   Achat chez un marchand : l'inventaire gagne un objet de la boutique ouverte, à son prix.
///   Mise en vente : EmK+ en mode vente ; retrait : EmK- puis l'objet revient dans l'inventaire ; vente : l'offre
///   disparaît sans revenir, ou n'est plus listée à l'ouverture suivante du mode vente (vente faite hors connexion).
///   Le jeu annonce aussi les ventes (INFOS_65, et INFOS_73 pour celles faites hors jeu, à la connexion) et les ventes
///   expirées (INFOS_67) : kamas, objet et quantité suffisent à retrouver l'offre.
/// Tout reste sur ce PC (echanges.json), rien n'est partagé (REGLES.md, règle 4).
/// </summary>
public sealed class TradeTracker
{
    const int MaxTrades = 5000;
    static readonly TimeSpan Match = TimeSpan.FromSeconds(3);
    static string DataPath => Path.Combine(GameData.AppDir, "echanges.json");

    readonly GameData _data;
    TradeData _store = new();

    /// <summary>Connexion du message en cours (à renseigner avant chaque message).</summary>
    public string Stream { get; set; } = "";

    sealed class Window
    {
        public int Exchange = -1;
        /// <summary>Personnage de la fenêtre (le dernier vu sur cette connexion).</summary>
        public string Character = "";
        public (long Id, (int Tpl, long?[] Prices) Old)? LastGone;
        public int[] LotSizes = [1, 10, 100];
        public double TaxPercent = 2;
        /// <summary>Offres consultées en mode achat : ligne -> (objet, prix des lots ×1, ×10, ×100).</summary>
        public readonly Dictionary<long, (int Tpl, long?[] Prices)> Lines = new();
        /// <summary>Lots disparus d'une offre, en attente de l'objet dans l'inventaire.</summary>
        public readonly List<(int Tpl, int Size, long Price, DateTime Time)> Removed = new();
        /// <summary>Objets arrivés dans l'inventaire, en attente du lot disparu (l'ordre des messages peut varier).</summary>
        public readonly List<(int Tpl, int Qty, DateTime Time)> Gained = new();
        /// <summary>Boutique de marchand ouverte : objet -> prix unitaire le plus bas.</summary>
        public readonly Dictionary<int, long> Shop = new();
        /// <summary>Offres retirées du mode vente, en attente : revenues dans l'inventaire (retrait) ou non (vendues).</summary>
        public readonly List<(Trade Listing, DateTime Time)> Leaving = new();
    }

    readonly Dictionary<string, Window> _windows = new();
    Window Current => _windows.TryGetValue(Stream, out var w) ? w : _windows[Stream] = new Window();

    public IReadOnlyList<Trade> Trades => _store.Trades;
    public event Action? Changed;
    public event Action<string>? Log;

    public TradeTracker(GameData data)
    {
        _data = data;
        try { if (File.Exists(DataPath)) _store = JsonSerializer.Deserialize<TradeData>(File.ReadAllText(DataPath)) ?? new(); }
        catch { _store = new(); }
    }

    public void OnStreamClosed(string stream) => _windows.Remove(stream);

    /// <summary>Échange ouvert (« ECK11|1,10,100;…;2.0;… » : tailles des lots et taxe) ou fermé (-1).</summary>
    public void OnExchange(string character, int type, string args)
    {
        var w = Current;
        Flush(w, force: true);
        if (character.Length > 0) w.Character = character;
        w.Exchange = type;
        w.Lines.Clear();
        w.Shop.Clear();
        if (type is 10 or 11)
        {
            var f = args.Split(';');
            var sizes = f[0].Split(',').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
            if (sizes.Length == 3 && sizes.All(n => n > 0)) w.LotSizes = sizes;
            if (f.Length > 2 && double.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tax))
                w.TaxPercent = tax;
        }
    }

    /// <summary>Offres d'un objet consultées (EHl) : « objet|ligne;effets;×1;×10;×100;objet|… », prix « 1849,1 ».</summary>
    public void OnOffers(string body)
    {
        var w = Current;
        if (w.Exchange != 11) return;
        var parts = body.Split('|');
        if (!int.TryParse(parts[0], out var tpl)) return;
        foreach (var line in parts.Skip(1))
        {
            var f = line.Split(';');
            if (f.Length >= 5 && long.TryParse(f[0], out var id)) w.Lines[id] = (tpl, [Price(f[2]), Price(f[3]), Price(f[4])]);
        }
    }

    /// <summary>Offre modifiée pendant la consultation (EHm) : « -ligne », « +ligne|objet|effets|×1|×10|×100 ».</summary>
    public void OnOfferMove(string body)
    {
        var w = Current;
        if (w.Exchange != 11 || body.Length < 2) return;
        if (body[0] == '-' && long.TryParse(body.AsSpan(1), out var gone) && w.Lines.Remove(gone, out var old))
        {
            // Lots disparus : tous, en attendant de voir ceux qui reviennent avec « + »
            for (int i = 0; i < 3; i++)
                if (old.Prices[i] is { } p) w.Removed.Add((old.Tpl, w.LotSizes[i], p, DateTime.Now));
            w.LastGone = (gone, old);
        }
        else if (body[0] == '+')
        {
            var f = body[1..].Split('|');
            if (f.Length < 6 || !long.TryParse(f[0], out var id) || !int.TryParse(f[1], out var tpl)) return;
            long?[] prices = [Price(f[3]), Price(f[4]), Price(f[5])];
            w.Lines[id] = (tpl, prices);
            // La ligne revient aussitôt (modifiée) : seuls les lots absents ont disparu
            if (w.LastGone is { } g && g.Id == id)
            {
                for (int i = 0; i < 3; i++)
                    if (g.Old.Prices[i] is { } p && prices[i] == p)
                        w.Removed.RemoveAll(r => r.Tpl == tpl && r.Size == w.LotSizes[i] && r.Price == p);
                w.LastGone = null;
            }
        }
        Flush(w);
    }

    /// <summary>Boutique d'un marchand ouverte (EL) : « uid;qté;objet;effets;prix|… ».</summary>
    public void OnShopList(string body)
    {
        var w = Current;
        foreach (var line in body.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split(';');
            if (f.Length >= 5 && int.TryParse(f[2], out var tpl) && long.TryParse(f[4], out var price) && price > 0)
                w.Shop[tpl] = Math.Min(price, w.Shop.GetValueOrDefault(tpl, long.MaxValue));
        }
    }

    /// <summary>Inventaire modifié : un gain peut être un achat, un retour d'offre retirée.</summary>
    public void OnInventoryDelta(string character, int tpl, int qty)
    {
        if (character.Length == 0) return;
        var w = Current;
        w.Character = character;
        if (qty > 0)
        {
            // Offre retirée du mode vente : l'objet revient
            var back = w.Leaving.FindIndex(l => l.Listing.ItemId == tpl && l.Listing.Quantity == qty);
            if (back >= 0)
            {
                Close(w.Leaving[back].Listing, Trade.Withdrawn, DateTime.Now, false);
                w.Leaving.RemoveAt(back);
                return;
            }
            if (w.Exchange == 11) { w.Gained.Add((tpl, qty, DateTime.Now)); Flush(w); }
            else if (w.Exchange == 4 && w.Shop.TryGetValue(tpl, out var unit))
                Add(new Trade { Kind = Trade.Purchase, Source = "Marchand", Time = DateTime.Now, Character = character, ItemId = tpl, Quantity = qty, Price = unit * qty });
        }
    }

    /// <summary>Rapproche lots disparus et objets reçus (même objet, même quantité, à 3 s près) : ce sont des achats.</summary>
    void Flush(Window w, bool force = false)
    {
        var now = DateTime.Now;
        for (int i = w.Gained.Count - 1; i >= 0; i--)
        {
            var g = w.Gained[i];
            int r = w.Removed.FindIndex(x => x.Tpl == g.Tpl && x.Size == g.Qty && (now - x.Time) < Match);
            if (r < 0) continue;
            Add(new Trade { Kind = Trade.Purchase, Time = g.Time, Character = w.Character, ItemId = g.Tpl, Quantity = g.Qty, Price = w.Removed[r].Price });
            w.Removed.RemoveAt(r);
            w.Gained.RemoveAt(i);
        }
        w.Removed.RemoveAll(x => force || now - x.Time > Match);
        w.Gained.RemoveAll(x => force || now - x.Time > Match);
        // Offres retirées qui ne sont pas revenues dans l'inventaire : vendues
        foreach (var l in w.Leaving.Where(l => force || now - l.Time > Match).ToList())
        {
            if (l.Listing.State == Trade.OnSale) Close(l.Listing, Trade.Sold, l.Time, false); // sinon déjà annoncée par le jeu
            w.Leaving.Remove(l);
        }
    }

    /// <summary>Appelé régulièrement : achève les rapprochements en attente.</summary>
    public void Tick()
    {
        foreach (var w in _windows.Values) Flush(w);
    }

    /// <summary>Mode vente : « +id|qté|objet|effets|prix|heures » (mise en vente) ou « -id » (offre retirée ou vendue).</summary>
    public void OnSellMove(string character, string body)
    {
        var w = Current;
        if (body.Length < 2 || character.Length == 0) return;
        if (body[0] == '+')
        {
            var f = body[1..].Split('|');
            if (f.Length < 5 || !long.TryParse(f[0], out var id) || !int.TryParse(f[1], out var qty) || !int.TryParse(f[2], out var tpl)
                || !long.TryParse(f[4], out var price)) return;
            if (Active(character).Any(t => t.ListingId == id)) return;
            Add(new Trade
            {
                Kind = Trade.Listing, Time = DateTime.Now, Character = character, ItemId = tpl, Quantity = qty, Price = price, ListingId = id,
                Tax = (long)Math.Ceiling(price * w.TaxPercent / 100),
            });
        }
        else if (body[0] == '-' && long.TryParse(body.AsSpan(1), out var gone)
                 && Active(character).FirstOrDefault(t => t.ListingId == gone) is { } listing)
            w.Leaving.Add((listing, DateTime.Now));
    }

    /// <summary>
    /// Ouverture du mode vente : vos offres en cours (« id;qté;objet;effets;prix;heures|… »). Une offre connue qui n'y est
    /// plus a été vendue depuis (souvent hors connexion) ; une offre inconnue (mise en vente avant l'application) est ajoutée.
    /// </summary>
    public void OnSellList(string character, string body)
    {
        if (character.Length == 0) return;
        var current = new List<(long Id, int Qty, int Tpl, long Price)>();
        foreach (var line in body.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split(';');
            if (f.Length < 5 || !long.TryParse(f[0], out var id) || !int.TryParse(f[1], out var qty) || !int.TryParse(f[2], out var tpl)
                || !long.TryParse(f[4], out var price))
            {
                // Format inattendu : on ne déduit aucune vente plutôt que d'en inventer
                Log?.Invoke($"Mes échanges : liste du mode vente non reconnue ({line[..Math.Min(line.Length, 60)]}).");
                return;
            }
            current.Add((id, qty, tpl, price));
        }
        var now = DateTime.Now;
        foreach (var t in Active(character).ToList())
            if (current.All(c => c.Id != t.ListingId)) Close(t, Trade.Sold, now, true);
        foreach (var c in current.Where(c => Active(character).All(t => t.ListingId != c.Id)))
            Add(new Trade { Kind = Trade.Listing, Time = now, Character = character, ItemId = c.Tpl, Quantity = c.Qty, Price = c.Price, ListingId = c.Id }, save: false);
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Vente annoncée par le jeu. 65 / 73 : « kamas~?~objet~quantité » (vendue en jeu / hors jeu) ; 67 : « objet~quantité »
    /// (expirée, renvoyée à la banque). L'objet est un identifiant ou un nom selon le message : les deux sont acceptés.
    /// L'offre est cherchée parmi celles du personnage, sinon parmi toutes (message reçu avant le nom du personnage).
    /// </summary>
    public void OnSaleInfo(string character, int code, string[] args)
    {
        var now = DateTime.Now;
        if (code is 65 or 73)
        {
            if (args.Length < 4 || !long.TryParse(args[0], out var kamas) || !int.TryParse(args[3], out var qty)) { Unknown(code, args); return; }
            var items = ItemsOf(args[2]).Concat(ItemsOf(args[1])).ToHashSet();
            var offer = Find(character, t => t.Quantity == qty && t.Price == kamas && (items.Count == 0 || items.Contains(t.ItemId)))
                ?? Find(character, t => t.Quantity == qty && items.Contains(t.ItemId));
            if (offer != null) { Close(offer, Trade.Sold, now, code == 73); return; }
            // Déjà comptée à l'instant (offre disparue du mode vente ouvert) : pas de doublon
            if (_store.Trades.Any(t => t.Kind == Trade.Listing && t.State == Trade.Sold && t.Quantity == qty && t.Price == kamas
                && t.ClosedAt is { } at && now - at < TimeSpan.FromSeconds(30))) return;
            // Offre jamais vue (mise en vente sans l'application) : la vente est quand même enregistrée
            if (items.Count == 0) { Unknown(code, args); return; }
            Add(new Trade
            {
                Kind = Trade.Listing, Time = now, Character = character, ItemId = items.First(), Quantity = qty, Price = kamas,
                State = Trade.Sold, ClosedAt = now, SoldUnseen = code == 73,
            });
        }
        else if (code == 67)
        {
            if (args.Length < 2 || !int.TryParse(args[1], out var qty)) { Unknown(code, args); return; }
            var items = ItemsOf(args[0]).ToHashSet();
            // Plusieurs offres identiques : la plus ancienne expire en premier
            if (Find(character, t => t.Quantity == qty && items.Contains(t.ItemId)) is { } offer) Close(offer, Trade.Expired, now, false);
        }
    }

    Trade? Find(string character, Func<Trade, bool> match) =>
        (character.Length > 0 ? Active(character).FirstOrDefault(match) : null)
        ?? _store.Trades.FirstOrDefault(t => t.Kind == Trade.Listing && t.State == Trade.OnSale && match(t));

    /// <summary>Objets désignés par un paramètre : identifiant, ou nom (sans balises ni guillemets).</summary>
    IEnumerable<int> ItemsOf(string arg)
    {
        arg = System.Text.RegularExpressions.Regex.Replace(arg, "<[^>]*>", "").Trim().Trim('\'', '"').Trim();
        if (arg.Length == 0) return [];
        if (int.TryParse(arg, out var id)) return _data.Items.ContainsKey(id) ? [id] : [];
        return _data.Items.Values.Where(i => string.Equals(i.Name, arg, StringComparison.OrdinalIgnoreCase)).Select(i => i.Id);
    }

    /// <summary>Format inattendu : signalé dans le journal (objet, prix et quantité seulement : aucun nom de joueur).</summary>
    void Unknown(int code, string[] args) =>
        Log?.Invoke($"Mes échanges : message de vente {code} non reconnu ({string.Join("~", args)[..Math.Min(80, string.Join("~", args).Length)]}).");

    IEnumerable<Trade> Active(string character) =>
        _store.Trades.Where(t => t.Kind == Trade.Listing && t.State == Trade.OnSale && t.Character == character);

    void Close(Trade t, string state, DateTime at, bool unseen)
    {
        t.State = state;
        t.ClosedAt = at;
        t.SoldUnseen = unseen && state == Trade.Sold;
        Log?.Invoke($"Mes échanges : {_data.GetItem(t.ItemId).Name} ×{t.Quantity} {state.ToLowerInvariant()}.");
        Save();
        Changed?.Invoke();
    }

    void Add(Trade t, bool save = true)
    {
        _store.Trades.Add(t);
        if (_store.Trades.Count > MaxTrades) _store.Trades.RemoveRange(0, _store.Trades.Count - MaxTrades);
        if (t.Kind == Trade.Purchase) Log?.Invoke($"Mes échanges : achat de {_data.GetItem(t.ItemId).Name} ×{t.Quantity} pour {t.Price:N0} k ({t.Source}).");
        if (!save) return;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Oublie une ligne du tableau de bord (erreur de rapprochement).</summary>
    public void Remove(Trade t)
    {
        if (!_store.Trades.Remove(t)) return;
        Save();
        Changed?.Invoke();
    }

    static long? Price(string s) => long.TryParse(s.Split(',')[0], out var p) && p > 0 ? p : null;

    void Save()
    {
        try
        {
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(DataPath, JsonSerializer.Serialize(_store));
        }
        catch (Exception ex) { Log?.Invoke("Mes échanges : sauvegarde impossible (" + ex.Message + ")."); }
    }
}
