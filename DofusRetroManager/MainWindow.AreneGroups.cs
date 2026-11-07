using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DofusRetroManager.Data;

namespace DofusRetroManager;

/// <summary>Groupes de personnages de le chronomètre de l'onglet Combat (arene.json) et groupe choisi (null : tous les personnages).</summary>
public sealed class AreneSettings
{
    public Dictionary<string, List<string>> Groups { get; set; } = new();
    public string? Selected { get; set; }
}

/// <summary>
/// Onglet Combat, chronomètre (ex-onglet Arène) : le chronomètre et les challenges ne comptent que les personnages du groupe choisi (ex. les 8 comptes du
/// groupe principal, sans un 9e compte qui joue à côté), ou tous les personnages.
/// </summary>
public partial class MainWindow
{
    static string ArenePath => Path.Combine(GameData.AppDir, "arene.json");
    AreneSettings _arene = new();
    bool _areneUpdating;
    const string AllCharactersGroup = "Tous les personnages";

    void InitAreneGroups()
    {
        try { if (File.Exists(ArenePath)) _arene = JsonSerializer.Deserialize<AreneSettings>(File.ReadAllText(ArenePath)) ?? new(); }
        catch { _arene = new(); }
        FillAreneGroups();
    }

    void SaveArene()
    {
        try { File.WriteAllText(ArenePath, JsonSerializer.Serialize(_arene)); } catch { }
    }

    /// <summary>Personnages du groupe choisi (null : tous).</summary>
    HashSet<string>? AreneMembers =>
        _arene.Selected != null && _arene.Groups.TryGetValue(_arene.Selected, out var names)
            ? names.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;

    bool InAreneGroup(string character) => AreneMembers is not { } members || members.Contains(character);

    void FillAreneGroups()
    {
        _areneUpdating = true;
        var items = _arene.Groups.Keys.Order().Prepend(AllCharactersGroup).ToList();
        AreneGroup.ItemsSource = items;
        AreneGroup.SelectedItem = _arene.Selected != null && items.Contains(_arene.Selected) ? _arene.Selected : AllCharactersGroup;
        _areneUpdating = false;
        FillAreneMembers();
    }

    /// <summary>Personnages connus (fenêtres suivies, combats, banques sauvegardées), cochés s'ils sont dans le groupe choisi.</summary>
    void FillAreneMembers()
    {
        var known = (_hunt?.Characters.Select(c => c.Name) ?? [])
            .Concat(_hunt?.Fights.Select(f => f.Character) ?? [])
            .Concat(_tracker.AllSnapshots().Select(s => s.Character))
            .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var members = AreneMembers;
        AreneGroupMembers.Children.Clear();
        foreach (var name in known)
            AreneGroupMembers.Children.Add(new CheckBox { Content = name, IsChecked = members?.Contains(name) == true, Margin = new Thickness(0, 0, 14, 4) });
        AreneGroupName.Text = _arene.Selected ?? "";
    }

    void AreneGroup_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_areneUpdating) return;
        _arene.Selected = AreneGroup.SelectedIndex > 0 ? AreneGroup.SelectedItem as string : null;
        SaveArene();
        FillAreneMembers();
        RefreshArene();
    }

    void AreneGroupEdit_Changed(object sender, RoutedEventArgs e)
    {
        AreneGroupPanel.Visibility = AreneGroupEdit.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (AreneGroupEdit.IsChecked == true) FillAreneMembers();
    }

    void AreneGroupSave_Click(object sender, RoutedEventArgs e)
    {
        var name = AreneGroupName.Text.Trim();
        var members = AreneGroupMembers.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => c.Content as string ?? "").ToList();
        if (name.Length == 0 || name == AllCharactersGroup) { AreneGroupStatus.Text = "Donnez un nom au groupe."; return; }
        if (members.Count == 0) { AreneGroupStatus.Text = "Cochez au moins un personnage."; return; }
        _arene.Groups[name] = members;
        _arene.Selected = name;
        SaveArene();
        AreneGroupStatus.Text = $"Groupe « {name} » enregistré ({members.Count} personnage(s)).";
        FillAreneGroups();
        RefreshArene();
    }

    void AreneGroupDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_arene.Selected is not { } name) { AreneGroupStatus.Text = "Choisissez d'abord un groupe."; return; }
        _arene.Groups.Remove(name);
        _arene.Selected = null;
        SaveArene();
        AreneGroupStatus.Text = $"Groupe « {name} » supprimé.";
        FillAreneGroups();
        RefreshArene();
    }
}
