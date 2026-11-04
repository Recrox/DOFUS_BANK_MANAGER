using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Harvest;

/// <summary>Ressource posée sur une case d'une map visitée.</summary>
public sealed record ResourceCell(int Cell, int Resource);

/// <summary>Map visitée : version du fichier (date du message GDM), clé de lecture et ressources trouvées.</summary>
public sealed class HarvestMap
{
    public string Date { get; set; } = "";
    public string Key { get; set; } = "";
    public List<ResourceCell> Cells { get; set; } = new();
}

/// <summary>Temps de réapparition observés pour une ressource (moyenne des récoltes vues jusqu'à la repousse).</summary>
public sealed class RespawnStats
{
    public int Count { get; set; }
    public double TotalSeconds { get; set; }
    public double Average => Count == 0 ? 0 : TotalSeconds / Count;
}

public sealed class HarvestData
{
    public Dictionary<int, HarvestMap> Maps { get; set; } = new();
    public Dictionary<int, RespawnStats> Respawn { get; set; } = new();
}

/// <summary>État d'une ressource de la map : récoltée depuis (null : disponible) ; Seen : récolte vue (sinon déjà récoltée à l'arrivée).</summary>
public sealed record CellState(DateTime? HarvestedAt, bool Seen);

/// <summary>Ressources d'un même type sur la map, pour l'affichage (« Petits poissons (mer) ×4 »).</summary>
public sealed record HarvestLine(HarvestResource Resource, int Total, int Available, IReadOnlyList<CellState> Harvested, double? RespawnSeconds);

/// <summary>
/// Onglets Carte et Récolte : ressources des maps visitées et leur état sur la map actuelle.
/// - Les ressources viennent du fichier de la map, dans le client installé (lecture seule), déchiffré avec la clé du
///   message GDM ; elles sont retenues dans recolte.json, sur ce PC seulement (données du jeu, jamais partagées).
/// - L'état (disponible, récoltée) vient du message GDF : numéro de case et état, rien d'autre.
/// - Le jeu ne donne pas le temps de repousse : il est appris en voyant une ressource récoltée puis réapparaître.
/// Rien n'est envoyé au serveur (règles 1 et 4).
/// </summary>
public sealed class HarvestTracker
{
    static string DataPath => Path.Combine(GameData.AppDir, "recolte.json");
    /// <summary>Repousse plausible : en dehors, l'observation est ignorée (map quittée, état manqué…).</summary>
    static readonly TimeSpan MinRespawn = TimeSpan.FromSeconds(10), MaxRespawn = TimeSpan.FromHours(3);

    readonly GameData _data;
    HarvestData _store = new();
    /// <summary>Map actuelle de chaque connexion (fenêtre de jeu).</summary>
    readonly Dictionary<string, int> _streamMap = new();
    /// <summary>État des ressources par map puis par case (cases absentes : disponibles).</summary>
    readonly Dictionary<int, Dictionary<int, CellState>> _states = new();

    /// <summary>Connexion du message en cours de traitement (à renseigner avant chaque message).</summary>
    public string Stream { get; set; } = "";

    public event Action? Changed;
    public event Action<string>? Log;

    public IReadOnlyDictionary<int, HarvestMap> Maps => _store.Maps;

    public HarvestTracker(GameData data)
    {
        _data = data;
        Load();
    }

    /// <summary>Entrée sur une map : « GDM|id|date|clé ». La map est lue dans le client si elle ne l'a pas déjà été.</summary>
    public void OnMapLoaded(string body)
    {
        var p = body.Split('|');
        if (p.Length < 2 || !int.TryParse(p[0], out var mapId)) return;
        string date = p[1], key = p.Length > 2 ? p[2] : "";
        _streamMap[Stream] = mapId;

        // À l'arrivée, le jeu renvoie l'état des ressources (GDF) : on repart de « disponible », en gardant l'heure
        // de récolte déjà vue pour celles qui le sont encore
        _entryStates[mapId] = _states.GetValueOrDefault(mapId) ?? new();
        _states[mapId] = new();

        if (!_store.Maps.TryGetValue(mapId, out var known) || known.Date != date || (known.Key.Length == 0 && key.Length > 0))
        {
            try
            {
                var cells = MapDecoder.InteractiveCells(mapId, date, key);
                if (cells != null)
                {
                    _store.Maps[mapId] = new HarvestMap
                    {
                        Date = date, Key = key,
                        Cells = cells.Where(c => _data.InteractiveGfx.TryGetValue(c.Gfx, out var io) && _data.Resources.ContainsKey(io))
                            .Select(c => new ResourceCell(c.Cell, _data.InteractiveGfx[c.Gfx])).ToList(),
                    };
                    Save();
                }
            }
            catch (Exception ex) { Log?.Invoke($"Récolte : map {mapId} illisible ({ex.Message})."); }
        }
        Changed?.Invoke();
    }

    readonly Dictionary<int, Dictionary<int, CellState>> _entryStates = new();

    /// <summary>
    /// État des objets interactifs de la map : « GDF|case;état;interactif|… ». États 2 à 4 : en cours de récolte ou
    /// récoltée ; les autres : disponible (ou en train de repousser).
    /// </summary>
    public void OnStates(string body)
    {
        if (!_streamMap.TryGetValue(Stream, out var mapId) || !_store.Maps.TryGetValue(mapId, out var map)) return;
        var resources = map.Cells.ToDictionary(c => c.Cell, c => c.Resource);
        if (!_states.TryGetValue(mapId, out var states)) _states[mapId] = states = new();
        var entry = _entryStates.GetValueOrDefault(mapId);
        bool changed = false;
        foreach (var part in body.Split('|'))
        {
            var f = part.Split(';');
            if (f.Length < 2 || !int.TryParse(f[0], out var cell) || !int.TryParse(f[1], out var state)) continue;
            if (!resources.TryGetValue(cell, out var resource)) continue;
            bool harvested = state is 2 or 3 or 4;
            var old = states.GetValueOrDefault(cell);
            if (harvested && old?.HarvestedAt == null)
            {
                // Déjà récoltée avant l'arrivée : on garde l'heure vue lors d'un passage précédent, sinon elle est inconnue
                var before = entry?.GetValueOrDefault(cell);
                states[cell] = before?.HarvestedAt != null ? before
                    : new CellState(DateTime.Now, Seen: entry == null);
                changed = true;
            }
            else if (!harvested && old?.HarvestedAt is { } at)
            {
                // Repousse vue sur place : on apprend son délai
                var elapsed = DateTime.Now - at;
                if (old.Seen && elapsed >= MinRespawn && elapsed <= MaxRespawn)
                {
                    if (!_store.Respawn.TryGetValue(resource, out var r)) _store.Respawn[resource] = r = new();
                    r.Count++;
                    r.TotalSeconds += elapsed.TotalSeconds;
                    Save();
                }
                states.Remove(cell);
                changed = true;
            }
        }
        // Le premier GDF après l'arrivée est l'état complet de la map : les suivants sont des changements vus sur place
        _entryStates.Remove(mapId);
        if (changed) Changed?.Invoke();
    }

    /// <summary>Fenêtre de jeu fermée : sa map n'est plus suivie.</summary>
    public void OnStreamClosed(string stream) => _streamMap.Remove(stream);

    /// <summary>Ressources de la map, par type, les plus nombreuses en premier.</summary>
    public List<HarvestLine> Lines(int mapId)
    {
        if (!_store.Maps.TryGetValue(mapId, out var map)) return [];
        var states = _states.GetValueOrDefault(mapId) ?? new();
        return map.Cells.GroupBy(c => c.Resource)
            .Select(g =>
            {
                var harvested = g.Select(c => states.GetValueOrDefault(c.Cell)).Where(s => s?.HarvestedAt != null).Select(s => s!).ToList();
                return new HarvestLine(_data.Resources[g.Key], g.Count(), g.Count() - harvested.Count, harvested,
                    _store.Respawn.TryGetValue(g.Key, out var r) && r.Count > 0 ? r.Average : null);
            })
            .OrderByDescending(l => l.Total).ThenBy(l => l.Resource.Name).ToList();
    }

    /// <summary>Map actuelle d'une connexion, si elle a été lue.</summary>
    public int? MapOf(string stream) => _streamMap.TryGetValue(stream, out var id) && _store.Maps.ContainsKey(id) ? id : null;

    /// <summary>
    /// Texte d'une ligne : « Petits poissons (mer) ×4 », puis les récoltées : « 1 récoltée · réapparition dans ~2 min ».
    /// </summary>
    public static string Describe(HarvestLine l)
    {
        var text = $"{l.Resource.Name} ×{l.Available}" + (l.Available < l.Total ? $" / {l.Total}" : "");
        if (l.Harvested.Count == 0) return text;
        var next = l.Harvested.Where(h => h.Seen).Select(h => h.HarvestedAt!.Value).DefaultIfEmpty().Min();
        string when;
        if (next != default && l.RespawnSeconds is { } avg)
        {
            var left = next.AddSeconds(avg) - DateTime.Now;
            when = left.TotalSeconds <= 0 ? "réapparition imminente" : $"réapparition dans ~{Duration(left)}";
        }
        else if (next != default) when = $"récoltée il y a {Duration(DateTime.Now - next)}";
        else when = l.RespawnSeconds is { } a ? $"repousse ~{Duration(TimeSpan.FromSeconds(a))}" : "repousse inconnue";
        return $"{text} · {l.Harvested.Count} récoltée(s), {when}";
    }

    static string Duration(TimeSpan t) =>
        t.TotalMinutes < 1 ? $"{Math.Max(1, (int)t.TotalSeconds)} s" : t.TotalHours < 1 ? $"{(int)Math.Round(t.TotalMinutes)} min" : $"{t.TotalHours:0.#} h";

    void Load()
    {
        try
        {
            if (File.Exists(DataPath)) _store = JsonSerializer.Deserialize<HarvestData>(File.ReadAllText(DataPath)) ?? new();
        }
        catch { _store = new(); }
    }

    void Save()
    {
        try { File.WriteAllText(DataPath, JsonSerializer.Serialize(_store)); }
        catch (Exception ex) { Log?.Invoke($"Récolte : sauvegarde impossible ({ex.Message})."); }
    }
}
