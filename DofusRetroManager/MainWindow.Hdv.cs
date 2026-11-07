using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DofusRetroManager.Data;
using DofusRetroManager.Net;

namespace DofusRetroManager;

public sealed record HdvRow(int Index, int ItemId, string Name, int Level, string Type,
    long? Lot1, long? Lot10, long? Lot100, double? Unit, long? Average, string Seen, DateTime SeenAt);

public sealed record HdvHistoryRow(string When, string Lot1, string Lot10, string Lot100);

public partial class MainWindow
{
    // --- Hôtel de vente ---

    const string AllTypes = "Tous les types";
    Hdv.HdvTracker? _hdv;

    void InitHdv(GameData data)
    {
        _hdv = new Hdv.HdvTracker(data);
        _hdv.Log += AddLog;
        _hdv.Changed += () =>
        {
            ApplyHdvFilters();
            Refresh(); // coût, vente et bénéfice des recettes, puis banque, brisage, TTG…
            ShowTtgDetail();
            RecipeGrid_SelectionChanged(RecipeGrid, null!); // coûts du détail de la recette
        };
        _tracker.HdvOffers += _hdv.OnOffers;
        _tracker.HdvAverage += _hdv.OnAverage;
        _tracker.HdvOfferMove += _hdv.OnOfferMove;

        _sync = new Hdv.HdvSync(_hdv, _merchants!, _pda!);
        _sync.Log += AddLog;
        _sync.StatusChanged += () => ShareStatus.Text = _sync.Status;
        ShareEnabled.IsChecked = _sync.Settings.Enabled;
        ShareUrl.Text = _sync.Settings.ServerUrl;
        ShareKey.Text = _sync.Settings.GroupKey;
        ShareStatus.Text = _sync.Status;
        var syncTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        syncTimer.Tick += async (_, _) => await _sync.TickAsync();
        syncTimer.Start();
        _ = _sync.TickAsync();

        HdvTypeFilter.Items.Add(AllTypes);
        foreach (var t in data.Items.Values.Select(i => i.TypeName).Distinct().Order()) HdvTypeFilter.Items.Add(t);
        HdvTypeFilter.SelectedIndex = 0;
        ApplyHdvFilters();
    }

    void ApplyHdvFilters()
    {
        if (_hdv == null || _data == null || HdvTypeFilter.SelectedItem == null) return;
        var search = HdvSearch.Text.Trim();
        var type = HdvTypeFilter.SelectedItem as string;
        var selected = (HdvGrid.SelectedItem as HdvRow)?.ItemId;

        IEnumerable<int> ids = HdvShowAll.IsChecked == true ? _data.Items.Keys : _hdv.Prices.Keys;
        var rows = ids
            .Select(id => (Item: _data.GetItem(id), Price: _hdv.Prices.GetValueOrDefault(id)))
            .Where(x => type == AllTypes || x.Item.TypeName == type)
            .Where(x => search.Length == 0 || Contains(x.Item.Name, search))
            .OrderByDescending(x => x.Price?.SeenAt ?? DateTime.MinValue).ThenBy(x => x.Item.Name)
            .Select((x, i) => new HdvRow(i + 1, x.Item.Id, x.Item.Name, x.Item.Level, x.Item.TypeName,
                x.Price?.Lot1, x.Price?.Lot10, x.Price?.Lot100, x.Price?.UnitPrice, x.Price?.Average,
                x.Price is { } p ? Ago(p.SeenAt) + (p.Shared ? " · groupe" : "") : "", x.Price?.SeenAt ?? DateTime.MinValue))
            .ToList();

        SetRows(HdvGrid, rows);
        HdvGrid.SelectedItem = rows.FirstOrDefault(r => r.ItemId == selected);
        HdvCount.Text = $"{rows.Count} objet(s) · {_hdv.Prices.Count} prix relevé(s)";
        HdvEmpty.Visibility = _hdv.Prices.Count == 0 && HdvShowAll.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
    }

    void HdvFilters_Changed(object sender, RoutedEventArgs e) => ApplyHdvFilters();

    Hdv.HdvSync? _sync;

    void ShareToggle_Click(object sender, RoutedEventArgs e) =>
        SharePanel.Visibility = SharePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    async void ShareSync_Click(object sender, RoutedEventArgs e)
    {
        if (_sync == null) return;
        if (!_sync.Settings.Enabled) { ShareStatus.Text = "Activez et enregistrez le partage d'abord."; return; }
        await _sync.SyncNowAsync();
    }

    async void ShareTest_Click(object sender, RoutedEventArgs e)
    {
        if (_sync == null) return;
        ShareStatus.Text = "Test en cours…";
        ShareStatus.Text = await _sync.TestAsync(ShareUrl.Text, ShareKey.Text);
    }

    async void ShareSave_Click(object sender, RoutedEventArgs e)
    {
        if (_sync == null) return;
        if (ShareEnabled.IsChecked == true && (ShareUrl.Text.Trim().Length == 0 || ShareKey.Text.Trim().Length == 0))
        {
            ShareStatus.Text = "Renseignez l'adresse du serveur et le code de groupe.";
            return;
        }
        _sync.Configure(ShareEnabled.IsChecked == true, ShareUrl.Text, ShareKey.Text);
        await _sync.TickAsync();
    }

    void HdvGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_hdv == null || _data == null || HdvGrid.SelectedItem is not HdvRow row) return;
        var price = _hdv.Prices.GetValueOrDefault(row.ItemId);
        HdvDetailTitle.Text = row.Name;
        HdvDetailSub.Text = price == null
            ? "Pas encore de prix relevé : consultez cet objet dans un hôtel de vente en jeu."
            : $"Prix unitaire : {Kamas(price.UnitPrice)} · moyen : {Kamas(price.Average)}\n{price.Offers} lot(s) en vente au dernier relevé ({Ago(price.SeenAt)})";
        HdvChart.Points = price?.History
            .Where(s => s.UnitPrice != null).Select(s => (s.Time, s.UnitPrice!.Value)).ToList() ?? [];
        HdvHistory.ItemsSource = price?.History.AsEnumerable().Reverse()
            .Select(s => new HdvHistoryRow(s.Time.ToString("dd/MM/yyyy HH:mm"), Kamas(s.Lot1), Kamas(s.Lot10), Kamas(s.Lot100)))
            .ToList();
    }

    void HdvForget_Click(object sender, RoutedEventArgs e)
    {
        if (HdvGrid.SelectedItem is HdvRow row) _hdv?.Forget(row.ItemId);
    }
}
