using System.Windows;
using System.Windows.Controls;
using DofusBankManager.Data;

namespace DofusBankManager;

public sealed record BrisageRow(int ItemId, string Name, int Level, string Type, string Stats, string Where,
    int Owned, int? Craftable, string Missing, double? CraftCost, double? HdvPrice, int Score)
{
    public int Index { get; init; }
}

/// <summary>
/// Onglet Brisage : cherche les objets dont les caractéristiques atteignent des seuils (ex. 104 en vitalité
/// pour générer une rune), parmi ses objets, les recettes faisables avec sa banque ou tout le jeu.
/// Simple affichage : le brisage se fait toujours à la main, en jeu (README, règle 3).
/// </summary>
public partial class MainWindow
{
    sealed record StatFilter(StatDef Stat, CheckBox Box, TextBox Min, TextBox Max);

    readonly List<StatFilter> _statFilters = new();

    void InitBrisage(GameData data)
    {
        foreach (var stat in ItemStats.All.Where(s => s.Main))
        {
            var box = new CheckBox { Content = stat.Name, VerticalAlignment = VerticalAlignment.Center, MinWidth = 104 };
            var min = new TextBox { Width = 52, Padding = new Thickness(4, 0, 4, 0), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = "Minimum" };
            var max = new TextBox { Width = 52, Padding = new Thickness(4, 0, 4, 0), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = "Maximum (vide = pas de limite)" };
            var filter = new StatFilter(stat, box, min, max);
            box.Checked += BrisageFilters_Changed;
            box.Unchecked += BrisageFilters_Changed;
            // Saisir un seuil coche la caractéristique
            foreach (var t in new[] { min, max })
                t.TextChanged += (_, _) =>
                {
                    if (t.Text.Trim().Length > 0 && box.IsChecked != true) box.IsChecked = true;
                    else ApplyBrisageFilters();
                };
            _statFilters.Add(filter);

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(box);
            panel.Children.Add(min);
            panel.Children.Add(new TextBlock { Text = "à", Style = (Style)FindResource("Label"), Margin = new Thickness(6, 0, 6, 0) });
            panel.Children.Add(max);
            var frame = new Border
            {
                Child = panel, Padding = new Thickness(10, 4, 6, 4), Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            };
            // Couleurs liées au thème (comme DynamicResource)
            frame.SetResourceReference(Border.BackgroundProperty, "Surface2");
            frame.SetResourceReference(Border.BorderBrushProperty, "Border");
            BrisageStats.Children.Add(frame);
        }

        BrisageTypeFilter.Items.Add(AllTypes);
        foreach (var t in data.Items.Values.Where(i => i.MaxStats.Count > 0).Select(i => i.TypeName).Distinct().Order())
            BrisageTypeFilter.Items.Add(t);
        BrisageTypeFilter.SelectedIndex = 0;
    }

    void BrisageFilters_Changed(object sender, RoutedEventArgs e) => ApplyBrisageFilters();

    void BrisageReset_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _ready = false; // une seule mise à jour à la fin
        foreach (var f in _statFilters) { f.Min.Text = ""; f.Max.Text = ""; f.Box.IsChecked = false; }
        _ready = true;
        ApplyBrisageFilters();
    }

    void ApplyBrisageFilters()
    {
        if (!_ready || _data == null || BrisageTypeFilter.SelectedItem == null) return;
        var search = BrisageSearch.Text.Trim();
        var type = BrisageTypeFilter.SelectedItem as string;
        int levelMin = int.TryParse(BrisageLevelMin.Text, out var a) ? a : 0;
        int levelMax = int.TryParse(BrisageLevelMax.Text, out var b) ? b : int.MaxValue;
        var active = _statFilters.Where(f => f.Box.IsChecked == true)
            .Select(f => (f.Stat.Characteristic,
                Min: int.TryParse(f.Min.Text, out var min) ? min : 1,
                Max: int.TryParse(f.Max.Text, out var max) ? max : int.MaxValue))
            .ToList();

        var owned = OwnedQuantities();
        var recipeRows = _rows.ToLookup(r => r.Recipe.ResultId);

        // Source : objets possédés (stats réelles), ou modèles d'objets (stats maximales)
        IEnumerable<(ItemInfo Item, IReadOnlyDictionary<int, int> Stats, string Where, int Count)> candidates;
        if (BrisageSource.SelectedIndex == 0)
        {
            var s = _tracker.State;
            candidates = s.Bank.Values.Select(i => (Owned: i, Where: "Banque"))
                .Concat(s.Inventory.Values.Select(i => (Owned: i, Where: "Inventaire")))
                .Where(x => x.Owned.Effects.Length > 0 && _fm != null)
                .Select(x => (_data.GetItem(x.Owned.TemplateId),
                    (IReadOnlyDictionary<int, int>)ItemStats.FromParsed(_fm!.ParseStats(x.Owned.Effects)),
                    x.Where, x.Owned.Quantity));
        }
        else
        {
            IEnumerable<ItemInfo> items = BrisageSource.SelectedIndex switch
            {
                1 => _rows.Where(r => r.Craftable > 0).Select(r => _data.GetItem(r.Recipe.ResultId)),
                2 => _rows.Select(r => _data.GetItem(r.Recipe.ResultId)),
                _ => _data.Items.Values,
            };
            candidates = items.DistinctBy(i => i.Id)
                .Select(it => (it, it.MaxStats, "", owned.GetValueOrDefault(it.Id)));
        }

        var list = candidates
            .Where(x => x.Stats.Count > 0)
            .Where(x => type == AllTypes || x.Item.TypeName == type)
            .Where(x => x.Item.Level >= levelMin && x.Item.Level <= levelMax)
            .Where(x => search.Length == 0 || Contains(x.Item.Name, search))
            .Where(x => active.All(f => x.Stats.GetValueOrDefault(f.Characteristic) is var v && v >= f.Min && v <= f.Max))
            .Select(x =>
            {
                // Caractéristiques filtrées en premier, puis les autres
                var ordered = x.Stats
                    .OrderBy(kv => active.Any(f => f.Characteristic == kv.Key) ? 0 : 1)
                    .ThenBy(kv => ItemStats.Order(kv.Key));
                var recipe = recipeRows[x.Item.Id].FirstOrDefault();
                return new BrisageRow(x.Item.Id, x.Item.Name, x.Item.Level, x.Item.TypeName,
                    string.Join(" · ", ordered.Select(kv => ItemStats.Format(kv.Key, kv.Value))),
                    x.Where, x.Count, recipe?.Craftable, recipe?.MissingText ?? "",
                    CraftCost(x.Item.Id), UnitPrice(x.Item.Id),
                    active.Sum(f => x.Stats.GetValueOrDefault(f.Characteristic)));
            })
            .OrderByDescending(r => r.Score).ThenByDescending(r => r.Level).ThenBy(r => r.Name)
            .Take(2000) // « Tous les objets du jeu » sans filtre : la liste reste fluide
            .Select((r, i) => r with { Index = i + 1 })
            .ToList();

        SetRows(BrisageGrid, list);
        BrisageCount.Text = list.Count == 2000 ? "2000 objets affichés (affinez les filtres)" : $"{list.Count} objet(s)";
        BrisageEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var bank = _tracker.State.Bank.Values;
        BrisageEmpty.Text = BrisageSource.SelectedIndex != 0 ? "Aucun objet ne correspond à ces critères."
            : bank.Count == 0 ? "Aucune banque reçue : ouvrez votre banque en jeu, ou choisissez une autre source."
            // Banque sauvegardée par une ancienne version, sans les caractéristiques des objets
            : bank.All(i => i.Effects.Length == 0)
                ? "Les caractéristiques de vos objets ne sont pas connues : rouvrez votre banque en jeu (capture active) pour les relever."
            : "Aucun objet ne correspond à ces critères.";
    }
}
