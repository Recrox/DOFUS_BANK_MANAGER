using System.Windows;
using System.Windows.Controls;

namespace DofusRetroManager;

/// <summary>Liens entre onglets : un ingrédient de recette ouvre l'HDV sur cet objet, un objet du brisage ouvre sa recette.</summary>
public partial class MainWindow
{
    /// <summary>Ouvre l'onglet HDV sur l'objet : recherche par son nom, type « Tous », et tous les objets s'il n'a pas de prix.</summary>
    void ShowInHdv(int itemId)
    {
        if (_data == null || _hdv == null) return;
        if (!_hdv.Prices.ContainsKey(itemId)) HdvShowAll.IsChecked = true;
        HdvTypeFilter.SelectedIndex = 0;
        HdvSearch.Text = _data.GetItem(itemId).Name;
        ApplyHdvFilters();
        HdvTab.IsSelected = true;
        if ((HdvGrid.ItemsSource as IEnumerable<HdvRow>)?.FirstOrDefault(r => r.ItemId == itemId) is { } row)
        {
            HdvGrid.SelectedItem = row;
            HdvGrid.ScrollIntoView(row);
        }
    }

    /// <summary>Ouvre l'onglet Recettes sur la recette de l'objet, les filtres remis à zéro (rien si l'objet ne se fabrique pas).</summary>
    bool ShowRecipeOf(int itemId)
    {
        if (_data == null || _rows.All(r => r.Recipe.ResultId != itemId)) return false;
        _ready = false; // une seule mise à jour à la fin
        _ingredientFilter = null;
        IngredientChip.Visibility = Visibility.Collapsed;
        RecipeSearch.Text = _data.GetItem(itemId).Name;
        JobFilter.SelectedIndex = 0;
        ModeFilter.SelectedIndex = 4; // Toutes les recettes
        LevelMin.Text = "1";
        LevelMax.Text = "200";
        foreach (var t in SlotFilters.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()) t.IsChecked = false;
        _ready = true;
        ApplyRecipeFilters();
        RecipesTab.IsSelected = true;
        if ((RecipeGrid.ItemsSource as IEnumerable<RecipeRow>)?.FirstOrDefault(r => r.Recipe.ResultId == itemId) is { } row)
        {
            RecipeGrid.SelectedItem = row;
            RecipeGrid.ScrollIntoView(row);
        }
        return true;
    }

    /// <summary>Clic sur un ingrédient (nom ou icône) dans le détail d'une recette.</summary>
    void IngredientLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is IngredientRow row) ShowInHdv(row.ItemId);
    }

    /// <summary>Clic sur le nom d'un objet de l'onglet Brisage, ou sur « Voir la recette ».</summary>
    void BrisageRecipe_Click(object sender, RoutedEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as BrisageRow ?? BrisageGrid.SelectedItem as BrisageRow;
        if (row != null && !ShowRecipeOf(row.ItemId)) AddLog($"{row.Name} : pas de recette.");
    }
}
