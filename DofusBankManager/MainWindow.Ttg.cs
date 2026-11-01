using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using DofusBankManager.Data;

namespace DofusBankManager;

public sealed record TtgRow(int ItemId, int? Number, string Name, string Rarity, int Owned,
    long? MerchantPrice, long? BestPrice, string Seen, DateTime SeenAt, double? HdvPrice)
{
    public int Index { get; init; }
}

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

        TtgRarity.Items.Add("Toutes les raretés");
        foreach (var t in Ttg.MerchantTracker.CardTypes)
            TtgRarity.Items.Add(data.Items.Values.FirstOrDefault(i => i.TypeId == t)?.TypeName ?? $"Type {t}");
        TtgRarity.SelectedIndex = 0;
    }

    void TtgFilters_Changed(object sender, RoutedEventArgs e) => ApplyTtgFilters();

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

        var s = _tracker.State;
        var owned = s.Bank.Values.Concat(s.Inventory.Values)
            .GroupBy(i => i.TemplateId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var all = _data.Items.Values.Where(i => types.Contains(i.TypeId)).Select(it =>
        {
            var m = _merchants.Prices.GetValueOrDefault(it.Id);
            var last = m?.Last;
            return new TtgRow(it.Id, CardNumber(it.Name), it.Name, it.TypeName, owned.GetValueOrDefault(it.Id),
                last?.Price, m?.BestRecent, last != null ? Ago(last.Time) : "", last?.Time ?? DateTime.MinValue,
                UnitPrice(it.Id));
        }).ToList();

        IEnumerable<TtgRow> q = all;
        q = TtgShow.SelectedIndex switch
        {
            1 => q.Where(r => r.Owned > 0),
            2 => q.Where(r => r.Owned == 0),
            3 => q.Where(r => r.MerchantPrice != null || r.HdvPrice != null),
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
            : $"Collection : {cards.Count(r => r.Owned > 0)} / {cards.Count} cartes · {_merchants.Prices.Count} prix marchand(s)";
    }

    void TtgGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowTtgDetail();

    void ShowTtgDetail()
    {
        if (_data == null || _merchants == null || TtgGrid.SelectedItem is not TtgRow row) return;
        var m = _merchants.Prices.GetValueOrDefault(row.ItemId);
        var hdv = _hdv?.Prices.GetValueOrDefault(row.ItemId);

        TtgDetailTitle.Text = row.Name;
        TtgDetailIcon.Source = ItemIcons.Get(row.ItemId);
        var lines = new List<string> { $"{row.Rarity} · {row.Owned} possédé(s)" };
        lines.Add(m?.Last is { } last
            ? $"Marchand : {Kamas(last.Price)} ({Ago(last.Time)}, {last.Quantity} en vente) · meilleur sur 7 j : {Kamas(m.BestRecent)}"
            : "Marchand : pas encore vu. Consultez la boutique d'un marchand qui la vend.");
        lines.Add(hdv?.UnitPrice is { } u ? $"HDV : {Kamas(u)} ({Ago(hdv.SeenAt)})" : "HDV : pas de prix relevé.");
        TtgDetailSub.Text = string.Join("\n", lines);

        TtgMerchantChart.Points = m?.History.Select(sn => (sn.Time, (double)sn.Price)).ToList() ?? [];
        TtgHdvChart.Points = hdv?.History
            .Where(sn => sn.UnitPrice != null).Select(sn => (sn.Time, sn.UnitPrice!.Value)).ToList() ?? [];
    }

    void TtgForget_Click(object sender, RoutedEventArgs e)
    {
        if (TtgGrid.SelectedItem is TtgRow row) _merchants?.Forget(row.ItemId);
    }
}
