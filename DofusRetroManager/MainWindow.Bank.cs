using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DofusRetroManager.Data;
using DofusRetroManager.Net;

namespace DofusRetroManager;

public sealed record BankRow(int ItemId, string Name, int Quantity, int Level, string Type, int UsedIn)
{
    public int Index { get; init; }
    /// <summary>Valeur estimée à l'HDV (quantité × meilleur prix unitaire relevé).</summary>
    public long? Value { get; init; }
    /// <summary>Meilleur prix unitaire relevé à l'HDV (×1, ×10 ÷ 10, ×100 ÷ 100).</summary>
    public double? UnitPrice { get; init; }
    /// <summary>Prix moyen affiché par l'HDV.</summary>
    public long? AveragePrice { get; init; }
}

public partial class MainWindow
{
    bool _bankTypesUpdating;

    void ApplyBankFilter()
    {
        if (!_ready || _data == null || _bankTypesUpdating) return;
        var search = BankSearch.Text.Trim();

        // Personnage : celui en jeu (1re entrée, suit les changements de personnage) ou la dernière banque connue d'un autre
        var current = _tracker.State;
        var snapshots = _tracker.AllSnapshots();
        var chars = snapshots.Skip(1).Select(x => x.Character).Distinct().Order()
            .Prepend("En jeu" + (current.Character.Length > 0 ? $" ({current.Character})" : "")).ToList();
        var chosen = BankCharacter.SelectedIndex > 0 ? BankCharacter.SelectedItem as string : null;
        if (!chars.SequenceEqual(BankCharacter.Items.Cast<string>()))
        {
            _bankTypesUpdating = true;
            BankCharacter.ItemsSource = chars;
            BankCharacter.SelectedIndex = chosen != null && chars.IndexOf(chosen) is > 0 and var i ? i : 0;
            _bankTypesUpdating = false;
        }
        var shown = BankCharacter.SelectedIndex > 0
            ? snapshots.Skip(1).FirstOrDefault(x => x.Character == BankCharacter.SelectedItem as string) ?? current
            : current;

        var all = shown.Bank.Values
            .GroupBy(i => i.TemplateId)
            .Select(g =>
            {
                var it = _data.GetItem(g.Key);
                int qty = g.Sum(i => i.Quantity);
                var unit = UnitPrice(g.Key);
                return new BankRow(g.Key, it.Name, qty, it.Level, it.TypeName, _usage.GetValueOrDefault(g.Key))
                {
                    Value = unit is { } u ? (long)Math.Round(u * qty) : null,
                    UnitPrice = unit,
                    AveragePrice = _hdv?.Prices.GetValueOrDefault(g.Key)?.Average,
                };
            })
            .ToList();

        // Liste déroulante des types : uniquement ceux présents en banque, sélection conservée
        var types = all.Select(r => r.Type).Distinct().Order().Prepend(AllTypes).ToList();
        var type = BankTypeFilter.SelectedItem as string ?? AllTypes;
        if (!types.SequenceEqual(BankTypeFilter.Items.Cast<string>()))
        {
            _bankTypesUpdating = true;
            BankTypeFilter.ItemsSource = types;
            if (!types.Contains(type)) type = AllTypes;
            BankTypeFilter.SelectedItem = type;
            _bankTypesUpdating = false;
        }

        var rows = all
            .Where(r => type == AllTypes || r.Type == type)
            .Where(r => search.Length == 0 || Contains(r.Name, search) || Contains(r.Type, search))
            .OrderBy(r => r.Type).ThenBy(r => r.Name)
            .Select((r, i) => r with { Index = i + 1 })
            .ToList();
        SetRows(BankGrid, rows);
        var owner = shown.Character;
        BankOwner.Text = (owner.Length > 0 ? $"Banque de {owner}" : "Banque")
            + (shown != current && shown.BankUpdatedAt > DateTime.MinValue ? $" · relevée le {shown.BankUpdatedAt:dd/MM/yyyy HH:mm}" : "");
        BankCount.Text = $"{rows.Count} objet(s) distinct(s)";

        int priced = rows.Count(r => r.Value != null);
        BankValue.Text = priced == 0 ? ""
            : $"Valeur estimée : {rows.Sum(r => r.Value ?? 0):N0} kamas ({priced}/{rows.Count} objets avec un prix)";
    }

    void BankFilter_Changed(object sender, RoutedEventArgs e) => ApplyBankFilter();

    // --- Banque -> recettes utilisant un objet ---

    /// <summary>Objet dont on affiche les recettes qui l'utilisent (posé depuis l'onglet Banque).</summary>
    int? _ingredientFilter;

    void BankRecipes_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BankRow row) ShowRecipesUsing(row.ItemId);
    }

    /// <summary>Ouvre l'onglet Recettes sur toutes les recettes qui utilisent cet objet, les autres filtres remis à zéro.</summary>
    void ShowRecipesUsing(int itemId)
    {
        if (_data == null) return;
        _ready = false; // une seule mise à jour à la fin
        _ingredientFilter = itemId;
        RecipeSearch.Text = "";
        JobFilter.SelectedIndex = 0;
        ModeFilter.SelectedIndex = 4; // Toutes les recettes (les faisables restent en haut)
        LevelMin.Text = "1";
        LevelMax.Text = "200";
        foreach (var t in SlotFilters.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()) t.IsChecked = false;
        _ready = true;

        IngredientChipText.Text = $"Utilisant : {_data.GetItem(itemId).Name}";
        IngredientChip.Visibility = Visibility.Visible;
        ApplyRecipeFilters();
        RecipesTab.IsSelected = true;
    }

    void IngredientChipClear_Click(object sender, RoutedEventArgs e)
    {
        _ingredientFilter = null;
        IngredientChip.Visibility = Visibility.Collapsed;
        ApplyRecipeFilters();
    }
}
