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

        _harvestTimer.Tick += (_, _) => { RefreshHarvest(); UpdateOverlay(); };
        _harvestTimer.Start();
        RebuildResourceHints();
        RefreshHarvest();
    }

    void OnHarvestChanged()
    {
        if (_harvest != null && _harvest.Maps.Count != _carteResourceMaps) RebuildResourceHints();
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
