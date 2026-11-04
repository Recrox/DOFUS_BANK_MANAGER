using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using DofusRetroManager.Data;

namespace DofusRetroManager;

public sealed record TtgRow(int ItemId, int? Number, string Name, string Rarity, int Owned,
    long? MerchantPrice, long? BestPrice, string Seen, DateTime SeenAt, double? HdvPrice, string BestMerchant, string BestWhere,
    CarteTarget? Goto) : IHasCarteTarget
{
    public int Index { get; init; }
    /// <summary>Carte rangée dans le classeur TTG (du personnage choisi, ou d'au moins un).</summary>
    public bool InBinder { get; init; }
    public string BinderText => InBinder ? "✓" : "";
    /// <summary>Possédée : en banque, en inventaire ou dans le classeur.</summary>
    public bool Has => Owned > 0 || InBinder;
}

/// <summary>Offre vue chez un marchand : prix, quantité, pseudo et position (« groupe » si elle vient du partage).</summary>
public sealed record TtgOfferRow(string Price, int Quantity, string Merchant, string Where, string Seen, CarteTarget? Goto) : IHasCarteTarget;

/// <summary>
/// Onglet TTG : cartes et paquets de cartes, avec leur prix chez les marchands (relevé quand le joueur
/// consulte une boutique en jeu), leur prix HDV et la collection du joueur (banque et inventaire).
/// </summary>
public partial class MainWindow
{
    Ttg.MerchantTracker? _merchants;

    void InitTtg(GameData data)
    {
        _merchants = new Ttg.MerchantTracker(data);
        _merchants.Log += AddLog;
        _merchants.Changed += () => { ApplyTtgFilters(); ShowTtgDetail(); };
        _tracker.ShopList += _merchants.OnShopList;
        _tracker.ShopOpened += _merchants.OnShopOpened;
        _tracker.MapChanged += _merchants.OnMapChanged;
        _tracker.MapSprites += _merchants.OnSprites;
        _sniffer.StreamClosed += stream => Ui(() => _merchants.OnStreamClosed(stream));

        TtgRarity.Items.Add("Toutes les raretés");
        foreach (var t in Ttg.MerchantTracker.CardTypes)
            TtgRarity.Items.Add(data.Items.Values.FirstOrDefault(i => i.TypeId == t)?.TypeName ?? $"Type {t}");
        TtgRarity.SelectedIndex = 0;
    }

    bool _ttgCharsUpdating;

    void TtgCharacter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ttgCharsUpdating) TtgFilters_Changed(sender, e);
    }

    /// <summary>Prix d'une carte : HDV (prix unitaire), sinon meilleur marchand de la période, sinon dernier prix marchand.</summary>
    static double? CardPrice(TtgRow r) => r.HdvPrice ?? r.BestPrice ?? r.MerchantPrice;

    void TtgFilters_Changed(object sender, RoutedEventArgs e)
    {
        ApplyTtgFilters();
        ShowTtgDetail();
    }

    /// <summary>Période des prix marchands retenus (« Meilleur N j » et liste des offres), 7 jours par défaut.</summary>
    int TtgDays => int.TryParse(TtgDaysBox?.Text, out var d) && d > 0 ? Math.Min(d, 365) : 7;

    /// <summary>Numéro de la carte dans le classeur : « “Krtek” - No.007 » -> 7.</summary>
    static int? CardNumber(string name) =>
        Regex.Match(name, @"No\.(\d+)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    void ApplyTtgFilters()
    {
        if (!_ready || _data == null || _merchants == null || TtgRarity.SelectedItem == null) return;
        var search = TtgSearch.Text.Trim();
        var types = TtgRarity.SelectedIndex == 0
            ? Ttg.MerchantTracker.CardTypes
            : [Ttg.MerchantTracker.CardTypes[TtgRarity.SelectedIndex - 1]];

        // Cartes possédées (banque et inventaire) d'un personnage ou de tous
        var character = CharacterChoice(TtgCharacter, ref _ttgCharsUpdating);
        var owned = OwnedItems(character).Select(x => x.Item)
            .GroupBy(i => i.TemplateId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        // Cartes du classeur (message reçu à son ouverture en jeu), converties en objets carte
        var binder = _tracker.AllSnapshots().Where(x => character == null ? x.Character.Length > 0 || x == _tracker.State : x.Character == character)
            .SelectMany(x => x.Binder).Select(c => _data.TtgCardItems.GetValueOrDefault(c)).Where(i => i > 0).ToHashSet();

        int days = TtgDays;
        TtgBestColumn.Header = $"Meilleur {days} j";
        var all = _data.Items.Values.Where(i => types.Contains(i.TypeId)).Select(it =>
        {
            var m = _merchants.Prices.GetValueOrDefault(it.Id);
            var last = m?.Last;
            // Meilleure offre de la période : son marchand et sa position (là où aller acheter)
            var best = m?.Recent(days).OrderBy(sn => sn.Price).ThenByDescending(sn => sn.Time).FirstOrDefault();
            return new TtgRow(it.Id, CardNumber(it.Name), it.Name, it.TypeName, owned.GetValueOrDefault(it.Id),
                last?.Price, best?.Price, last != null ? Ago(last.Time) + (last.Shared ? " · groupe" : "") : "", last?.Time ?? DateTime.MinValue,
                UnitPrice(it.Id), best == null ? "" : OfferMerchant(best), best == null ? "" : OfferWhere(best), best == null ? null : OfferGoto(best))
                { InBinder = binder.Contains(it.Id) };
        }).ToList();

        IEnumerable<TtgRow> q = all;
        // Comme l'HDV : seulement les cartes avec un prix, sauf « Toutes les cartes du jeu »
        bool showAll = TtgShowAll.IsChecked == true;
        if (!showAll) q = q.Where(r => r.MerchantPrice != null || r.HdvPrice != null);
        q = TtgShow.SelectedIndex switch
        {
            1 => q.Where(r => r.Has),
            2 => q.Where(r => !r.Has),
            _ => q,
        };
        if (search.Length > 0) q = q.Where(r => Contains(r.Name, search) || Contains(r.Rarity, search));

        var list = q.OrderBy(r => r.Number ?? int.MaxValue).ThenBy(r => r.Name)
            .Select((r, i) => r with { Index = i + 1 }).ToList();
        var selected = (TtgGrid.SelectedItem as TtgRow)?.ItemId;
        SetRows(TtgGrid, list);
        TtgGrid.SelectedItem = list.FirstOrDefault(r => r.ItemId == selected);

        // Progression de la collection : cartes différentes possédées (hors paquets)
        var cards = all.Where(r => r.Number != null).ToList();
        TtgCount.Text = $"{list.Count} objet(s)";
        TtgCollection.Text = cards.Count == 0 ? ""
            : $"Collection : {cards.Count(r => r.Has)} / {cards.Count} cartes (dont {cards.Count(r => r.InBinder)} au classeur) · {_merchants.Prices.Count} prix marchand(s)";
        // Cartes manquantes et leur coût : meilleur prix relevé (HDV, marchand de la période, sinon dernier prix marchand)
        var missing = cards.Where(r => !r.Has).Select(r => CardPrice(r)).ToList();
        TtgMissing.Text = cards.Count == 0 || missing.Count == 0 ? ""
            : $"Manquantes : {missing.Count} · coût estimé {Kamas(missing.Sum(p => p ?? 0))} ({missing.Count(p => p != null)}/{missing.Count} avec un prix)";
        TtgEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TtgEmpty.Text = showAll || all.Any(r => r.MerchantPrice != null || r.HdvPrice != null)
            ? "Aucune carte ne correspond à ces critères."
            : "Aucun prix relevé pour l'instant. Ouvrez la boutique d'un marchand qui vend des cartes, ou consultez-les à l'HDV. "
              + "Cochez « Toutes les cartes du jeu » pour voir toute la collection.";
    }

    /// <summary>Pseudo du marchand d'une offre (« groupe » si elle vient du partage, « ? » si relevée avant cette version).</summary>
    static string OfferMerchant(Ttg.MerchantSnapshot s) => s.Merchant ?? (s.Shared ? "groupe" : "?");

    /// <summary>Position d'une offre : coordonnées, zone et case, ex. « [4,-19] Astrub (Cité d'Astrub) · case 301 ».</summary>
    string OfferWhere(Ttg.MerchantSnapshot s) =>
        s.MapId is { } map ? _data!.MapLabel(map) + (s.Cell is { } c ? $" · case {c}" : "") : "";

    /// <summary>Case du marchand sur la carte (clic sur la position), avec son pseudo et sa case dans la map.</summary>
    CarteTarget? OfferGoto(Ttg.MerchantSnapshot s) =>
        CarteTargetOf(s.MapId, OfferMerchant(s) + (s.Cell is { } c ? $" · case {c}" : ""));

    void TtgGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowTtgDetail();

    void ShowTtgDetail()
    {
        if (_data == null || _merchants == null || TtgGrid.SelectedItem is not TtgRow row) return;
        var m = _merchants.Prices.GetValueOrDefault(row.ItemId);
        var hdv = _hdv?.Prices.GetValueOrDefault(row.ItemId);

        TtgDetailTitle.Text = row.Name;
        TtgDetailIcon.Source = ItemIcons.Get(row.ItemId);
        int days = TtgDays;
        var lines = new List<string> { $"{row.Rarity} · {row.Owned} possédé(s)" + (row.InBinder ? " · dans le classeur" : "") };
        lines.Add(m?.Last is { } last
            ? $"Marchand : {Kamas(last.Price)} ({Ago(last.Time)}, {last.Quantity} en vente) · meilleur sur {days} j : {Kamas(m.BestRecent(days))}"
            : "Marchand : pas encore vu. Consultez la boutique d'un marchand qui la vend.");
        lines.Add(hdv?.UnitPrice is { } u ? $"HDV : {Kamas(u)} ({Ago(hdv.SeenAt)})" : "HDV : pas de prix relevé.");
        TtgDetailSub.Text = string.Join("\n", lines);

        // Offres vues sur la période, de la moins chère à la plus chère, avec le marchand et sa position
        // Offres de la période (moins chères d'abord), ou tout l'historique (plus récents d'abord)
        bool all = TtgOffersMode.SelectedIndex == 1;
        IEnumerable<Ttg.MerchantSnapshot> snapshots = m == null ? []
            : all ? m.History.OrderByDescending(s => s.Time)
            : m.Recent(days).OrderBy(s => s.Price).ThenByDescending(s => s.Time);
        var offers = snapshots.Select(s => new TtgOfferRow(
            Kamas(s.Price), s.Quantity,
            OfferMerchant(s), OfferWhere(s),
            all ? s.Time.ToString("dd/MM HH:mm") : Ago(s.Time), OfferGoto(s))).ToList();
        TtgOffersTitle.Text = all ? $"Historique ({offers.Count} relevé(s))" : $"Offres vues ces {days} jours ({offers.Count})";
        TtgOffers.ItemsSource = offers;

        TtgMerchantChart.Points = m?.History.Select(sn => (sn.Time, (double)sn.Price)).ToList() ?? [];
        TtgHdvChart.Points = hdv?.History
            .Where(sn => sn.UnitPrice != null).Select(sn => (sn.Time, sn.UnitPrice!.Value)).ToList() ?? [];
    }

    void TtgForget_Click(object sender, RoutedEventArgs e)
    {
        if (TtgGrid.SelectedItem is TtgRow row) _merchants?.Forget(row.ItemId);
    }
}
