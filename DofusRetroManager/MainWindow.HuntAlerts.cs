using System.Windows;
using System.Windows.Controls;
using DofusRetroManager.Hunt;
using DofusRetroManager.Multi;

namespace DofusRetroManager;

/// <summary>Objet obtenu en combat : quantité, prix unitaire (HDV, sinon marchand) et total.</summary>
public sealed record HuntDropRow(int ItemId, string Name, int Quantity, double? Unit)
{
    public double? Total => Unit * Quantity;
}

/// <summary>Alerte monstre affichée dans l'onglet Combat.</summary>
public sealed record HuntAlertRow(MonsterAlert Alert, string Text)
{
    public bool Enabled => Alert.Enabled;
}

/// <summary>
/// Onglet Combat : détail des objets obtenus (session ou combat choisi), pop-up quand un archi-monstre apparaît, et
/// alertes monstres du joueur (un monstre précis, en nombre et à un niveau précis, dans un groupe de la map).
/// Simple affichage : c'est toujours le joueur qui se déplace et qui combat (REGLES.md, règle 3).
/// </summary>
public partial class MainWindow
{
    List<FightRecord> _huntSessionFights = [];
    DateTime? _huntSelectedFight;
    bool _huntFightsUpdating;

    void InitHuntAlerts()
    {
        HuntArchiPopup.IsChecked = _hunt!.ArchiPopup;
        _hunt.AlertMatched += (c, alert, count) => Safe(() => OnAlertMatched(c, alert, count));
        RefreshAlertList();
    }

    // --- Drops ---

    void HuntFightGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_huntFightsUpdating) return;
        _huntSelectedFight = (HuntFightGrid.SelectedItem as FightRow)?.TimeSort;
        ShowHuntDrops();
    }

    /// <summary>Objets obtenus : ceux du combat choisi, sinon ceux de toute la session, les plus rentables en haut.</summary>
    void ShowHuntDrops()
    {
        if (_data == null) return;
        var fight = _huntSessionFights.FirstOrDefault(f => f.Time == _huntSelectedFight);
        var fights = fight != null ? [fight] : _huntSessionFights;
        var rows = fights.SelectMany(f => f.Drops).GroupBy(d => d.ItemId)
            .Select(g => new HuntDropRow(g.Key, _data.GetItem(g.Key).Name, g.Sum(d => d.Quantity), DropPrice(g.Key)))
            .OrderByDescending(r => r.Total ?? -1).ThenBy(r => r.Name).ToList();
        SetRows(HuntDropGrid, rows);
        HuntDropsTitle.Text = (fight != null ? $"Drops du combat de {fight.Time:HH:mm}" : "Drops de la session")
            + $" · {rows.Sum(r => r.Quantity):N0} objet(s) · {Kamas(rows.Sum(r => r.Total ?? 0))}"
            + (fight != null ? " (Ctrl+clic sur le combat pour revoir la session)" : "");
    }

    // --- Pop-up archi-monstre ---

    void HuntArchiPopup_Changed(object sender, RoutedEventArgs e)
    {
        if (_hunt != null) _hunt.ArchiPopup = HuntArchiPopup.IsChecked == true;
    }

    /// <summary>Bulle au premier plan : l'archi-monstre, sa map et le prix de sa pierre ; un clic affiche la fenêtre qui le voit.</summary>
    void ShowArchiPopup(ArchiSighting a)
    {
        if (_data == null || _hunt?.ArchiPopup != true) return;
        try
        {
            var price = _pda?.Prices.GetValueOrDefault(Hdv.PdaTracker.Key(a.MonsterId, Hdv.PdaTracker.Archi))?.Price;
            var who = _hunt.Characters.FirstOrDefault(c => c.MapId == a.MapId);
            var window = WindowOf(who);
            // La bulle s'affiche sur l'écran de la fenêtre qui voit l'archi (jeu sur un second écran)
            // Compact : le nom en titre, puis le prix de la pierre et la position (sans la sous-zone entre parenthèses)
            var pos = System.Text.RegularExpressions.Regex.Replace(MapText(a.MapId), @"\s*\(.*\)\s*$", "");
            NoticeToast.Show(_data.GetMonster(a.MonsterId).Name,
                (price is { } p ? $"💎 {p:N0} k" : "💎 prix inconnu") + $"\n📍 {pos}",
                window is { } h ? () => FocusWindow(h) : null, window ?? default, sticky: true);
        }
        catch (Exception ex) { AddLog($"Pop-up archi-monstre impossible ({ex.GetType().Name} : {ex.Message})."); }
    }

    /// <summary>Fenêtre du jeu d'un personnage (par son nom), si elle est ouverte.</summary>
    IntPtr? WindowOf(HuntCharacter? c) =>
        c == null || c.Name.Length == 0 ? null
        : (_windows.Count > 0 ? _windows : GameWindows.Find())
            .FirstOrDefault(w => string.Equals(w.Name, c.Name, StringComparison.OrdinalIgnoreCase))?.Handle;

    // --- Alertes monstres ---

    void HuntAlertsToggle_Changed(object sender, RoutedEventArgs e) =>
        HuntAlertsPanel.Visibility = HuntAlertsToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    void RefreshAlertList()
    {
        if (_data == null || _hunt == null) return;
        HuntAlertList.ItemsSource = _hunt.Alerts.Select(a => new HuntAlertRow(a,
            $"{(a.MinCount > 1 ? $"{a.MinCount}× " : "")}{_data.GetMonster(a.MonsterId).Name} · niv. {a.MinLevel}–{a.MaxLevel}")).ToList();
        HuntAlertsToggle.Content = _hunt.Alerts.Count == 0 ? "Alertes monstres" : $"Alertes monstres ({_hunt.Alerts.Count(a => a.Enabled)})";
    }

    void HuntAlertAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_data == null || _hunt == null) return;
        var name = HuntAlertMonster.Text.Trim();
        // Nom exact d'abord, sinon un seul monstre dont le nom contient le texte (hors archi-monstres, déjà signalés)
        var matches = _data.Monsters.Values.Where(m => m.Name.Length > 0 && !m.IsArchi).ToList();
        var exact = matches.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        var found = exact.Count > 0 ? exact : name.Length < 3 ? [] : matches.Where(m => Contains(m.Name, name)).ToList();
        var distinct = found.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count != 1)
        {
            HuntAlertStatus.Text = distinct.Count == 0 ? $"Aucun monstre « {name} »."
                : $"Plusieurs monstres : {string.Join(", ", distinct.Take(6))}{(distinct.Count > 6 ? "…" : "")}. Précisez le nom.";
            return;
        }
        int Parse(WebTextBox t, int fallback) => int.TryParse(t.Text, out var v) && v > 0 ? v : fallback;
        int min = Parse(HuntAlertMin, 1), max = Parse(HuntAlertMax, 9999);
        // Un même nom peut couvrir plusieurs identifiants (versions du monstre) : une alerte pour chacun
        foreach (var m in found.Where(m => string.Equals(m.Name, distinct[0], StringComparison.OrdinalIgnoreCase)))
            _hunt.Alerts.Add(new MonsterAlert
            {
                MonsterId = m.Id, MinCount = Parse(HuntAlertCount, 1), MinLevel = Math.Min(min, max), MaxLevel = Math.Max(min, max),
            });
        _hunt.SaveAlerts();
        HuntAlertStatus.Text = $"Alerte ajoutée : {distinct[0]}.";
        HuntAlertMonster.Text = "";
        RefreshAlertList();
    }

    void HuntAlertTest_Click(object sender, RoutedEventArgs e)
    {
        try { ArchiSound.PlayMonster(_hunt?.SoundVolume ?? 70); } catch { }
    }

    void HuntAlertToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HuntAlertRow row || _hunt == null) return;
        row.Alert.Enabled = !row.Alert.Enabled;
        _hunt.SaveAlerts();
        RefreshAlertList();
    }

    void HuntAlertDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HuntAlertRow row || _hunt == null) return;
        _hunt.Alerts.Remove(row.Alert);
        _hunt.SaveAlerts();
        RefreshAlertList();
    }

    /// <summary>Un groupe de la map répond à une alerte : son propre son, et une bulle (un clic affiche la fenêtre qui le voit).</summary>
    void OnAlertMatched(HuntCharacter c, MonsterAlert alert, int count)
    {
        if (_data == null) return;
        try { ArchiSound.PlayMonster(_hunt?.SoundVolume ?? 70); } catch { }
        var name = _data.GetMonster(alert.MonsterId).Name;
        var text = $"{count}× {name} (niv. {alert.MinLevel}–{alert.MaxLevel})\n{MapText(c.MapId)} · {c.Label}";
        HuntStatus.Text = $"🔔 {count}× {name} sur la map";
        AddLog($"Alerte monstre : {count}× {name} ({c.Label}).");
        var window = WindowOf(c);
        NoticeToast.Show("Alerte monstre", text, window is { } h ? () => FocusWindow(h) : null, window ?? default);
    }
}
