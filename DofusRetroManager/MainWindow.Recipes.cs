using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DofusRetroManager.Data;
using DofusRetroManager.Net;

namespace DofusRetroManager;

public sealed record RecipeRow(Recipe Recipe, string Name, int Level, string Type, string Jobs,
    int Craftable, int MissingKinds, string MissingText, bool UsesOwned)
{
    public int Index { get; init; }
    /// <summary>Nombre de cases de la recette (ingrédients différents).</summary>
    public int Slots => Recipe.Ingredients.Count;

    /// <summary>Coût de fabrication d'après les prix HDV (null si un ingrédient n'a pas de coût connu).</summary>
    public double? Cost { get; init; }
    /// <summary>Prix de vente unitaire de l'objet à l'HDV.</summary>
    public double? Sell { get; init; }
    public double? Profit => Sell - Cost;
    /// <summary>Bénéfice rapporté au coût, en fraction (0,25 = +25 %).</summary>
    public double? Margin => Cost > 0 ? Profit / Cost : null;
    /// <summary>1 si rentable, -1 si à perte, 0 si inconnu : couleur de la colonne Bénéfice.</summary>
    public int ProfitSign => Profit is { } p ? Math.Sign(p) : 0;
}

/// <summary>Ingrédient d'une recette. Children : ingrédients de sa propre recette (sous-recette), quantités totales.</summary>
public sealed record IngredientRow(int ItemId, string Name, string Ratio, Brush Brush, string Note, string Price,
    IReadOnlyList<IngredientRow> Children)
{
    public bool HasChildren => Children.Count > 0;
}

public partial class MainWindow
{
    // --- Calcul des recettes ---

    Dictionary<int, int> OwnedQuantities()
    {
        var s = _tracker.State;
        IEnumerable<OwnedItem> items = s.Bank.Values;
        if (IncludeInventory.IsChecked == true) items = items.Concat(s.Inventory.Values);
        return items.GroupBy(i => i.TemplateId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
    }

    void Refresh()
    {
        if (!_ready || _data == null) return;
        var s = _tracker.State;
        BankStatus.Text = s.Bank.Count == 0
            ? "Aucune banque reçue — ouvrez votre banque en jeu."
            : $"{(s.Character.Length > 0 ? s.Character + " — " : "")}Banque : {s.Bank.Count} objets ({s.Bank.Values.Sum(i => (long)i.Quantity):N0} unités), "
              + $"{s.BankKamas:N0} kamas — mise à jour le {s.BankUpdatedAt:dd/MM/yyyy HH:mm:ss}";

        var owned = OwnedQuantities();
        _rows = _data.Recipes.Select(r =>
        {
            var it = _data.GetItem(r.ResultId);
            int craftable = r.Ingredients.Min(i => owned.GetValueOrDefault(i.ItemId) / i.Quantity);
            var missing = r.Ingredients.Where(i => owned.GetValueOrDefault(i.ItemId) < i.Quantity).ToList();
            var missingText = string.Join(", ", missing.Select(i =>
                $"{i.Quantity - owned.GetValueOrDefault(i.ItemId)}× {_data.GetItem(i.ItemId).Name}"));
            return new RecipeRow(r, it.Name, it.Level, it.TypeName, string.Join(", ", r.Jobs),
                craftable, missing.Count, missingText, r.Ingredients.Any(i => owned.ContainsKey(i.ItemId)))
                { Cost = RecipeCost(r), Sell = UnitPrice(r.ResultId) };
        }).ToList();

        ApplyRecipeFilters();
        ApplyBankFilter();
        ApplyBrisageFilters();
        ApplyTtgFilters();
        ApplyPdaFilters();
        RefreshHunt();
    }

    void ApplyRecipeFilters()
    {
        if (!_ready) return;
        var search = RecipeSearch.Text.Trim();
        var job = JobFilter.SelectedItem as string;
        int min = int.TryParse(LevelMin.Text, out var a) ? a : 0;
        int max = int.TryParse(LevelMax.Text, out var b) ? b : int.MaxValue;

        IEnumerable<RecipeRow> q = _rows;
        q = ModeFilter.SelectedIndex switch
        {
            0 => q.Where(r => r.Craftable > 0),
            1 => q.Where(r => r.MissingKinds <= 1),
            2 => q.Where(r => r.MissingKinds <= 2),
            3 => q.Where(r => r.UsesOwned),
            5 or 7 => q.Where(r => r.Profit > 0),
            6 => q.Where(r => r.Profit != null),
            _ => q
        };
        var profitMode = ModeFilter.SelectedIndex >= 5;
        SellMinPanel.Visibility = profitMode ? Visibility.Visible : Visibility.Collapsed;
        if (profitMode && long.TryParse(new string(SellMin.Text.Where(char.IsDigit).ToArray()), out var sellMin) && sellMin > 0)
            q = q.Where(r => r.Sell >= sellMin);
        if (job != null && job != AllJobs) q = q.Where(r => r.Recipe.Jobs.Contains(job));
        q = q.Where(r => r.Level >= min && r.Level <= max);
        var slots = SlotFilters.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()
            .Where(t => t.IsChecked == true).Select(t => (int)t.Tag).ToHashSet();
        if (slots.Count > 0) q = q.Where(r => slots.Contains(r.Slots));
        if (_ingredientFilter is { } ingredient) q = q.Where(r => r.Recipe.Ingredients.Any(i => i.ItemId == ingredient));
        if (search.Length > 0)
            q = q.Where(r => Contains(r.Name, search) || Contains(r.Type, search)
                || r.Recipe.Ingredients.Any(i => Contains(_data!.GetItem(i.ItemId).Name, search)));

        // Modes « rentabilité » : les plus gros bénéfices (ou marges) en haut ; sinon les faisables d'abord
        var ordered = ModeFilter.SelectedIndex == 7 ? q.OrderByDescending(r => r.Margin).ThenByDescending(r => r.Profit).ThenBy(r => r.Name)
            : profitMode ? q.OrderByDescending(r => r.Profit).ThenBy(r => r.Name)
            : q.OrderByDescending(r => r.Craftable > 0).ThenBy(r => r.MissingKinds).ThenByDescending(r => r.Level).ThenBy(r => r.Name);
        var list = ordered.Select((r, i) => r with { Index = i + 1 }).ToList();
        var selected = (RecipeGrid.SelectedItem as RecipeRow)?.Recipe;
        SetRows(RecipeGrid, list);
        RecipeGrid.SelectedItem = list.FirstOrDefault(r => r.Recipe == selected);
        RecipeCount.Text = $"{list.Count} recette(s) · bénéfice calculable pour {_rows.Count(r => r.Profit != null)}";
    }

    void RecipeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecipeGrid.SelectedItem is not RecipeRow row || _data == null) return;

        DetailTitle.Text = row.Name;
        DetailSub.Text = $"Niveau {row.Level} · {row.Type} · {row.Jobs}"
            + (row.Craftable > 0 ? $"\nFaisable {row.Craftable} fois" : $"\nIl manque {row.MissingKinds} ingrédient(s)");

        var stats = StatLines(_data.GetItem(row.Recipe.ResultId).MaxStats);
        DetailStats.ItemsSource = stats;
        DetailStatsPanel.Visibility = stats.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Nouvelle recette : autant de fabrications que les ressources le permettent (au moins 1)
        if (_detailRecipe != row.Recipe)
        {
            _detailRecipe = row.Recipe;
            DetailCraftCount.Text = Math.Max(1, row.Craftable).ToString();
        }
        DetailCraftMax.IsEnabled = row.Craftable > 0;
        ShowDetailCost();
    }

    Recipe? _detailRecipe;

    void DetailCraftCount_Changed(object sender, TextChangedEventArgs e) => ShowDetailCost();

    void DetailCraftMax_Click(object sender, RoutedEventArgs e)
    {
        if (RecipeGrid.SelectedItem is RecipeRow { Craftable: > 0 } row) DetailCraftCount.Text = row.Craftable.ToString();
    }

    /// <summary>
    /// Coût et rentabilité de N fabrications : prix HDV de chaque ingrédient (ou coût de sa sous-recette s'il n'a pas de prix),
    /// bénéfice en comptant tous les ingrédients, et bénéfice en ne comptant que ce qu'il faut acheter.
    /// </summary>
    void ShowDetailCost()
    {
        if (!_ready || RecipeGrid?.SelectedItem is not RecipeRow row || _data == null) return;
        int n = int.TryParse(DetailCraftCount.Text, out var c) && c > 0 ? Math.Min(c, 100_000) : 1;
        var owned = OwnedQuantities();

        // Ingrédients (et sous-recettes) pour les N fabrications
        var recipesByResult = _rows.ToLookup(r => r.Recipe.ResultId);
        DetailIngredients.ItemsSource = row.Recipe.Ingredients
            .Select(i => BuildIngredient(i.ItemId, i.Quantity * n, owned, recipesByResult, 1))
            .ToList();

        double total = 0, toBuy = 0;
        int unpriced = 0;
        foreach (var i in row.Recipe.Ingredients)
        {
            if (UnitCost(i.ItemId) is not { } unit) { unpriced++; continue; }
            total += unit * i.Quantity * n;
            toBuy += unit * Math.Max(0, i.Quantity * n - owned.GetValueOrDefault(i.ItemId));
        }
        int count = row.Recipe.Ingredients.Count;
        DetailCostPanel.Visibility = Visibility.Visible;
        DetailCostTitle.Text = n > 1 ? $"Coût de {n} fabrications" : "Coût de la recette";
        DetailCost.Text = unpriced == count ? "Coût inconnu" : Kamas(total) + (unpriced > 0 ? " (partiel)" : "");

        var lines = new List<string>();
        if (unpriced > 0)
            lines.Add($"{unpriced} ingrédient(s) sur {count} sans prix relevé : consultez-les à l'HDV.");
        if (unpriced < count) lines.Add($"À acheter (manquants) : {Kamas(toBuy)}");
        var sell = UnitPrice(row.Recipe.ResultId) * n;
        lines.Add(sell is { } s ? $"Vente HDV{(n > 1 ? $" ({n} × {Kamas(s / n)})" : "")} : {Kamas(s)}" : "Prix HDV de l'objet inconnu");
        DetailCostNote.Text = string.Join("\n", lines);

        // Bénéfices : seulement si tout est chiffré (sinon le résultat serait faux)
        bool known = sell != null && unpriced == 0;
        SetProfit(DetailProfit, "Bénéfice", known ? sell - total : null, total);
        SetProfit(DetailProfitOwned, "Hors ressources possédées", known && toBuy < total ? sell - toBuy : null, toBuy);
    }

    void SetProfit(WebText target, string label, double? profit, double cost)
    {
        target.Text = profit is { } p
            ? $"{label} : {(p >= 0 ? "+" : "")}{Kamas(p)}" + (cost > 0 ? $" ({p / cost:+0%;-0%;0%})" : "")
            : "";
        target.SetResourceReference(TextBlock.ForegroundProperty, profit >= 0 ? "Ok" : "Missing");
    }

    /// <summary>Profondeur maximale des sous-recettes affichées et des coûts de fabrication (garde-fou contre les cycles).</summary>
    const int MaxRecipeDepth = 4;
    Dictionary<int, Recipe> _recipeByResult = new();

    IngredientRow BuildIngredient(int itemId, int needed, Dictionary<int, int> owned, ILookup<int, RecipeRow> recipesByResult, int depth)
    {
        int have = owned.GetValueOrDefault(itemId);
        bool ok = have >= needed;
        var sub = recipesByResult[itemId].FirstOrDefault();
        var note = "";
        if (!ok && sub != null)
            note = sub.Craftable > 0 ? $"Fabricable avec vos ressources (×{sub.Craftable})" : "Fabricable (ressources manquantes)";

        // Quantités de la sous-recette pour fabriquer les « needed » exemplaires demandés
        var children = sub != null && depth < MaxRecipeDepth
            ? sub.Recipe.Ingredients.Select(i => BuildIngredient(i.ItemId, i.Quantity * needed, owned, recipesByResult, depth + 1)).ToList()
            : new List<IngredientRow>();

        var price = UnitPrice(itemId) is { } u
            ? Kamas(u * needed) + (needed > 1 ? $"  ({Kamas(u)} / u)" : "")
            : CraftCost(itemId) is { } c ? $"Fabrication ≈ {Kamas(c * needed)}" : "Prix inconnu";

        return new IngredientRow(itemId, $"{needed}× {_data!.GetItem(itemId).Name}", $"{have} / {needed}",
            ok ? (Brush)FindResource("Ok") : (Brush)FindResource("Missing"), note, price, children);
    }

    /// <summary>Meilleur prix unitaire relevé à l'HDV.</summary>
    double? UnitPrice(int itemId) => _hdv?.Prices.GetValueOrDefault(itemId)?.UnitPrice;

    /// <summary>Coût d'une unité : prix HDV, sinon coût de fabrication si tous ses ingrédients ont un coût connu.</summary>
    double? UnitCost(int itemId, int depth = 0) =>
        UnitPrice(itemId) ?? (depth < MaxRecipeDepth ? CraftCost(itemId, depth + 1) : null);

    /// <summary>Coût de fabrication d'une unité ; null si l'objet n'a pas de recette ou si un ingrédient n'a pas de coût connu.</summary>
    double? CraftCost(int itemId, int depth = 0) =>
        _recipeByResult.TryGetValue(itemId, out var recipe) ? RecipeCost(recipe, depth) : null;

    /// <summary>Coût d'une recette ; null si un ingrédient n'a pas de coût connu (prix HDV ou sous-recette).</summary>
    double? RecipeCost(Recipe recipe, int depth = 0)
    {
        double total = 0;
        foreach (var i in recipe.Ingredients)
        {
            if (UnitCost(i.ItemId, depth) is not { } c) return null;
            total += c * i.Quantity;
        }
        return total;
    }

    /// <summary>Caractéristiques triées et formatées, ex. « +200 Vitalité ».</summary>
    static List<string> StatLines(IReadOnlyDictionary<int, int> stats) =>
        stats.OrderBy(kv => ItemStats.Order(kv.Key)).Select(kv => ItemStats.Format(kv.Key, kv.Value)).ToList();

    void Filters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender == IncludeInventory) { UpdateInventoryHint(); Refresh(); }
        else ApplyRecipeFilters();
    }

    /// <summary>Indique à côté de « Inclure l'inventaire » si l'inventaire est réellement connu.</summary>
    void UpdateInventoryHint()
    {
        int count = _tracker.State.Inventory.Count;
        if (_tracker.InventoryComplete)
        {
            InventoryHint.Text = $"({count} objet(s) en inventaire)";
            InventoryHint.Foreground = (Brush)FindResource("TextMuted");
        }
        else
        {
            InventoryHint.Text = IncludeInventory.IsChecked == true
                ? "Inventaire inconnu : l'application doit être lancée avant de vous connecter à votre personnage. Reconnectez-vous (ou changez de personnage et revenez)."
                : "";
            InventoryHint.Foreground = (Brush)FindResource("Warning");
        }
    }
}
