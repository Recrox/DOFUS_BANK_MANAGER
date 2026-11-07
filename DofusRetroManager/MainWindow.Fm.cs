using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DofusRetroManager.Data;
using DofusRetroManager.Net;

namespace DofusRetroManager;

public sealed record FmItemOption(long Uid, string Label)
{
    public override string ToString() => Label; // texte affiché dans la liste déroulante
}

public sealed record FmHistoryRow(int Index, string Time, int RuneId, string Rune, string Outcome, Brush OutcomeBrush,
    string Gains, string Losses, string Weight, string Reliquat);

/// <summary>Ligne de l'écran « Poids des stats » (onglet FM) : rune, stat, poids par point et valeur par défaut.</summary>
public sealed record FmWeightRow(int Stat, int RuneId, string Rune, string Label, string Weight, string Default, bool Modified);

public partial class MainWindow
{
    // --- Forgemagie ---

    Fm.FmTracker? _fm;
    bool _fmUpdating;

    void InitFm(GameData data)
    {
        _fm = new Fm.FmTracker(data);
        _fm.Log += AddLog;
        _fm.Changed += RefreshFm;
        _tracker.CraftOpened += _fm.OnCraftOpened;
        _tracker.CraftItemSeen += _fm.OnCraftItemSeen;
        _tracker.CraftInfo += _fm.OnCraftInfo;
        _tracker.CraftItemConsumed += (uid, tpl, count) => _fm.OnItemConsumed(uid, tpl, count);

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => _fm.Tick();
        timer.Start();
        RefreshFm();
    }

    void RefreshFm()
    {
        if (_fm == null || _data == null) return;
        RefreshFmWeights();
        _fmUpdating = true;
        // Plusieurs exemplaires du même objet : ses caractéristiques et la dernière rune les distinguent
        var options = _fm.Items.Values.OrderByDescending(i => i.LastUsed)
            .Select(i =>
            {
                var stats = string.Join(", ", _fm.ParseStats(i.Effects).Where(kv => kv.Value != 0).Take(3)
                    .Select(kv => _fm.Describe(kv.Key, kv.Value)));
                return new FmItemOption(i.Uid, $"{_data.GetItem(i.TemplateId).Name}  ·  {i.History.Count} rune(s)"
                    + (stats.Length > 0 ? $"  ·  {stats}" : "") + $"  ·  {i.LastUsed:dd/MM HH:mm}");
            })
            .ToList();
        FmItemCombo.ItemsSource = options;
        FmItemCombo.SelectedItem = options.FirstOrDefault(o => o.Uid == _fm.CurrentUid);
        _fmUpdating = false;

        var item = _fm.CurrentUid is { } uid ? _fm.Items.GetValueOrDefault(uid) : null;
        FmEmpty.Visibility = item == null || item.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (item == null)
        {
            FmItemName.Text = "Aucun objet";
            FmReliquat.Text = "0";
            FmStats.ItemsSource = null;
            FmHistoryGrid.ItemsSource = null;
            return;
        }

        FmItemName.Text = _data.GetItem(item.TemplateId).Name;
        FmReliquat.Text = item.Reliquat.ToString("0.##");
        FmStats.ItemsSource = _fm.ParseStats(item.Effects)
            .Where(kv => kv.Value != 0)
            .Select(kv => _fm.Describe(kv.Key, kv.Value))
            .ToList();

        string Changes(IEnumerable<Fm.FmStatChange> changes) =>
            string.Join(", ", changes.Select(c => _fm.Describe(c.Stat, c.Delta)));

        SetRows(FmHistoryGrid, item.History
            .Select((ev, i) => new FmHistoryRow(
                i + 1,
                ev.Time.ToString("HH:mm:ss"),
                ev.RuneTemplateId ?? 0,
                ev.RuneTemplateId is { } r ? _data.GetItem(r).Name : "?",
                ev.Outcome,
                (Brush)FindResource(ev.Outcome switch
                {
                    Fm.FmTracker.SuccesCritique => "Ok",
                    Fm.FmTracker.SuccesNeutre => "Warning",
                    _ => "Missing",
                }),
                Changes(ev.Changes.Where(c => c.Delta > 0)),
                Changes(ev.Changes.Where(c => c.Delta < 0)),
                $"+{ev.WeightGained:0.##} / -{ev.WeightLost:0.##}",
                ev.ReliquatAfter.ToString("0.##")))
            .Reverse() // le plus récent en haut
            .ToList());
    }

    void FmItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fmUpdating || _fm == null || FmItemCombo.SelectedItem is not FmItemOption o) return;
        _fm.Select(o.Uid);
        RefreshFm();
    }

    void FmSetReliquat_Click(object sender, RoutedEventArgs e)
    {
        if (_fm?.CurrentUid is not { } uid) return;
        var text = FmReliquatInput.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            AddLog("Reliquat invalide : saisissez un nombre.");
            return;
        }
        _fm.SetReliquat(uid, value);
        FmReliquatInput.Text = "";
    }

    void FmClear_Click(object sender, RoutedEventArgs e)
    {
        if (_fm?.CurrentUid is not { } uid) return;
        if (_webConfirmed || MessageBox.Show(this, "Effacer l'historique de cet objet et remettre son reliquat à 0 ?", "Forgemagie",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _fm.ClearHistory(uid);
    }

    void FmForget_Click(object sender, RoutedEventArgs e)
    {
        if (_fm?.CurrentUid is not { } uid) return;
        if (_webConfirmed || MessageBox.Show(this, "Retirer cet objet et tout son historique de la liste ?", "Forgemagie",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _fm.Forget(uid);
    }

    // --- Poids des stats : affichés à la place de l'historique, modifiables, pris en compte tout de suite ---

    void FmWeights_Click(object sender, RoutedEventArgs e)
    {
        bool show = FmWeightsPanel.Visibility != Visibility.Visible;
        FmWeightsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        FmWeights.Content = show ? "Retour à l'historique" : "Poids des stats";
    }

    static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);

    void RefreshFmWeights()
    {
        if (_fm == null || _data == null) return;
        SetRows(FmWeightsGrid, _fm.WeightList().Select(w => new FmWeightRow(w.Stat, w.RuneId,
            w.Rune.Length > 0 ? w.Rune : "—", _data.FormatEffect(w.Stat, 1), Num(w.Weight),
            w.Default is { } d ? Num(d) : "—", w.Default != w.Weight)).ToList());
    }

    /// <summary>Saisie d'un poids : enregistrée en quittant le champ ou avec Entrée ; vide ou invalide : valeur par défaut.</summary>
    void SetFmWeight(int stat, string text)
    {
        if (_fm == null) return;
        text = text.Trim().Replace(',', '.');
        if (text.Length == 0) { _fm.SetWeight(stat, null); return; }
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0)
            _fm.SetWeight(stat, v);
        else
        {
            AddLog("Poids invalide : saisissez un nombre positif (ex. 0,25).");
            RefreshFmWeights(); // la valeur précédente revient
        }
    }

    void FmWeightBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FmWeightRow row } box && box.Text != row.Weight) SetFmWeight(row.Stat, box.Text);
    }

    void FmWeightBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || sender is not TextBox { DataContext: FmWeightRow row } box) return;
        e.Handled = true;
        if (box.Text != row.Weight) SetFmWeight(row.Stat, box.Text);
    }

    void FmWeightReset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FmWeightRow row) _fm?.SetWeight(row.Stat, null);
    }

    void FmWeightsReset_Click(object sender, RoutedEventArgs e) => _fm?.ResetWeights();
}
