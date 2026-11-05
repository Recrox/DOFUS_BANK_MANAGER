using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DofusRetroManager.Data;
using DofusRetroManager.Harvest;

namespace DofusRetroManager;

/// <summary>
/// Récolte : ressources des maps visitées sur la carte (filtrées par métier puis par ressource), et ressources de la map
/// actuelle avec leur état et une estimation de leur repousse (onglet Carte et superposition en jeu).
/// Simple affichage : c'est toujours le joueur qui récolte (règle 3).
/// </summary>
public partial class MainWindow
{
    HarvestTracker? _harvest;
    /// <summary>Filtre de la carte pour une ressource : base + identifiant de l'objet interactif.</summary>
    const int ResourceFilterBase = 100000;
    /// <summary>Points « ressource » des maps visitées (un par map et par ressource, ex. « If ×3 »).</summary>
    List<MapHint> _carteResourceHints = [];
    int _carteResourceMaps = -1;
    readonly DispatcherTimer _harvestTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    static bool IsResourceFilter(int key) => key >= ResourceFilterBase;

    void InitHarvest(GameData data)
    {
        _harvest = new HarvestTracker(data);
        _harvest.Log += AddLog;
        _harvest.Changed += OnHarvestChanged;
        _tracker.MapLoaded += s => Safe(() => _harvest.OnMapLoaded(s));
        _tracker.MapResourceStates += s => Safe(() => _harvest.OnStates(s));
        _sniffer.StreamClosed += stream => Ui(() => _harvest.OnStreamClosed(stream));
        _hunt!.Changed += RefreshHarvest;

        // Métiers de récolte, puis leurs ressources
        var jobs = data.Resources.Values.SelectMany(r => r.Skills).Select(s => s.Job).Distinct().Order().ToList();
        CarteJob.ItemsSource = jobs.Prepend("Aucune").ToList();
        CarteJob.SelectedIndex = 0;
        _harvestTimer.Tick += (_, _) => { RefreshHarvest(); UpdateOverlay(); };
        _harvestTimer.Start();
        RebuildResourceHints();
        RefreshHarvest();
    }

    void OnHarvestChanged()
    {
        if (_harvest != null && _harvest.Maps.Count != _carteResourceMaps)
        {
            RebuildResourceHints();
            if (_carteSuperArea >= 0 && _carteShown.Any(IsResourceFilter)) ApplyCarteFilters();
        }
        RefreshHarvest();
        UpdateOverlay();
    }

    void RebuildResourceHints()
    {
        if (_harvest == null || _data == null) return;
        _carteResourceMaps = _harvest.Maps.Count;
        _carteResourceHints = _harvest.Maps
            .Where(m => _data.Maps.ContainsKey(m.Key))
            .SelectMany(m => m.Value.Cells.GroupBy(c => c.Resource).Select(g =>
            {
                var r = _data.Resources[g.Key];
                // Gfx : objet obtenu, pour l'icône
                return new MapHint($"{r.Name} ×{g.Count()}", ResourceFilterBase + r.Id, r.ItemId, _data.Maps[m.Key]);
            }))
            .ToList();
        CarteResourceInfo.Text = _harvest.Maps.Count == 0 ? "aucune map lue pour l'instant"
            : $"{_harvest.Maps.Count} map(s) lue(s)";
    }

    /// <summary>Métier choisi : ses ressources deviennent des boutons à cocher (icône de l'objet obtenu).</summary>
    void CarteJob_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_data == null) return;
        CarteResourceFilters.Children.Clear();
        var job = CarteJob.SelectedIndex > 0 ? CarteJob.SelectedItem as string : null;
        if (job == null) return;
        foreach (var r in _data.Resources.Values.Where(r => r.Skills.Any(s => s.Job == job))
                     .OrderBy(r => _data.GetItem(r.Skills.First(s => s.Job == job).ItemId).Level).ThenBy(r => r.Name))
        {
            int key = ResourceFilterBase + r.Id;
            var item = r.Skills.First(s => s.Job == job).ItemId;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (ItemIcons.Get(item) is { } icon)
                content.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = r.Name, VerticalAlignment = VerticalAlignment.Center });
            int maps = _carteResourceHints.Count(h => h.Category == key);
            var toggle = new ToggleButton
            {
                Content = content, Tag = key, Style = (Style)FindResource("PillToggle"), Margin = new Thickness(0, 0, 4, 4),
                IsChecked = _carteShown.Contains(key),
                ToolTip = $"{r.Name} : {string.Join(", ", r.Skills.Select(s => _data.GetItem(s.ItemId).Name + " (" + s.Job + ")"))}\n"
                          + (maps == 0 ? "Pas encore vue sur une map visitée." : $"Vue sur {maps} map(s) visitée(s)."),
            };
            toggle.Checked += CarteFilter_Changed;
            toggle.Unchecked += CarteFilter_Changed;
            CarteResourceFilters.Children.Add(toggle);
        }
    }

    /// <summary>Ressources de la map du personnage affiché (onglet Combat) : texte du bloc « Récolte sur la map ».</summary>
    List<HarvestLine> HarvestLinesOf(Hunt.HuntCharacter? c) =>
        _harvest != null && c != null && _harvest.MapOf(c.Stream) is { } map ? _harvest.Lines(map) : [];

    void RefreshHarvest()
    {
        if (_harvest == null) return;
        var me = _hunt?.Shown;
        var lines = HarvestLinesOf(me);
        CarteHarvestTitle.Text = "Récolte sur la map" + (me != null ? $" · {me.Label}" : "");
        CarteHarvest.Text = me == null ? "Changez de map en jeu pour voir ses ressources."
            : _harvest.MapOf(me.Stream) == null ? "Map pas encore lue : changez de map en jeu (application lancée)."
            : lines.Count == 0 ? "Aucune ressource sur cette map."
            : "";
        CarteHarvest.Visibility = CarteHarvest.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CarteHarvestList.ItemsSource = lines.Select(HarvestTracker.Chip).ToList();
    }

    /// <summary>Ressources pour la page HTML : métiers et leurs ressources (avec icône), points des maps visitées.</summary>
    string CarteResourcesJson()
    {
        if (_data == null) return "null";
        var jobs = _data.Resources.Values.SelectMany(r => r.Skills.Select(s => (s.Job, r, s.ItemId)))
            .GroupBy(x => x.Job).OrderBy(g => g.Key)
            .Select(g => new
            {
                n = g.Key,
                res = g.OrderBy(x => _data.GetItem(x.ItemId).Level).ThenBy(x => x.r.Name)
                    .Select(x => new { k = ResourceFilterBase + x.r.Id, n = x.r.Name, i = $"icon/item/{x.ItemId}.png", t = _data.GetItem(x.ItemId).Name }),
            });
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            jobs,
            maps = _carteResourceMaps,
            hints = _carteResourceHints.Where(h => h.Map.SuperArea is 0 or 3).Select(h => new
            {
                n = h.Name, k = h.Category, i = $"icon/item/{h.Gfx}.png", x = h.Map.X, y = h.Map.Y, sa = h.Map.SuperArea, l = h.Map.Label,
            }),
        });
    }
}
