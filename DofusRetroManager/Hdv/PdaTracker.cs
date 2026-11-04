using System.Globalization;
using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hdv;

public sealed class PdaSnapshot
{
    public DateTime Time { get; set; }
    public long? Price { get; set; }
    public int Offers { get; set; }
}

/// <summary>Prix de la pierre d'âme la moins chère d'une catégorie (Monstre, Donjon, Archi-monstre) contenant un monstre.</summary>
public sealed class PdaPrice
{
    public int MonsterId { get; set; }
    public string Category { get; set; } = "";
    public long? Price { get; set; }
    /// <summary>Prix moyen indiqué par l'HDV pour ce monstre.</summary>
    public long? Average { get; set; }
    public int Offers { get; set; }
    /// <summary>Modèle de la pierre la moins chère (pour son icône).</summary>
    public int StoneTemplate { get; set; }
    public DateTime SeenAt { get; set; }
    /// <summary>Version des règles de relevé (voir PdaTracker.CurrentRules) : les relevés trop anciens sont écartés.</summary>
    public int Rules { get; set; }
    /// <summary>Vrai si le dernier relevé vient d'un autre membre du groupe (partage des prix).</summary>
    public bool Shared { get; set; }
    public List<PdaSnapshot> History { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => PdaTracker.Key(MonsterId, Category);
}

/// <summary>
/// Prix des pierres d'âme pleines, relevés passivement à l'HDV des pierres d'âme. Cet HDV liste les offres
/// par monstre (EHl{monstre}|offre|…, chaque offre portant le modèle de la pierre) : un prix par monstre,
/// pas par objet. Les autres objets sont relevés par HdvTracker, qui ignore ces listes.
///
/// Une même liste mélange les catégories : la liste d'un Bulbuisson contient des pierres de monstre, mais aussi
/// des pierres d'archi-monstre où il accompagne l'archi. Les prix sont donc tenus par monstre ET par catégorie,
/// et une pierre d'archi-monstre ne compte que pour un monstre de la race Archi-monstres.
/// Comme pour l'HDV, aucune requête n'est envoyée au serveur (README, règles 1 et 3).
/// </summary>
public sealed class PdaTracker
{
    const int MaxHistory = 50;
    /// <summary>2 : une pierre de donjon ne compte plus que pour son boss.</summary>
    const int CurrentRules = 2;

    /// <summary>Pierre d'âme pleine, de gardien de donjon, d'archi-monstre.</summary>
    public static readonly int[] StoneTypes = [85, 124, 125];

    public const string Monstre = "Monstre", Donjon = "Donjon", Archi = "Archi-monstre";

    /// <summary>Effet d'une pierre d'âme : un monstre capturé, "26f####{monstre en hexa}".</summary>
    const int CapturedMonsterEffect = 0x26f;

    static string PricesPath => Path.Combine(GameData.AppDir, "pda.json");

    readonly GameData _data;

    /// <summary>Monstre dont la liste d'offres est affichée en jeu, et ses offres retenues : id -> (prix unitaire, pierre).</summary>
    int? _current;
    readonly Dictionary<long, (long? Price, int Stone)> _offers = new();
    /// <summary>Catégories vues dans la liste affichée (une catégorie vidée par les achats est relevée sans prix).</summary>
    readonly HashSet<string> _listingCategories = new();

    /// <summary>Clé « monstre:catégorie » -> prix.</summary>
    public Dictionary<string, PdaPrice> Prices { get; private set; } = new();

    public event Action? Changed;
    public event Action<string>? Log;
    /// <summary>Prix relevé par le joueur lui-même (à partager avec le groupe, si le partage est activé).</summary>
    public event Action<PdaPrice>? Observed;

    public PdaTracker(GameData data)
    {
        _data = data;
        Load();
    }

    public static string Key(int monster, string category) => $"{monster}:{category}";

    /// <summary>Catégorie <-> code échangé avec le serveur de partage (0 = monstre, 1 = donjon, 2 = archi-monstre).</summary>
    public static int CategoryCode(string category) => category switch { Donjon => 1, Archi => 2, _ => 0 };
    public static string CategoryName(int code) => code switch { 1 => Donjon, 2 => Archi, _ => Monstre };

    /// <summary>Un modèle de pierre de la catégorie, pour l'icône d'un prix reçu du groupe.</summary>
    public int StoneOf(string category) =>
        _data.Items.Values.FirstOrDefault(i => StoneTypes.Contains(i.TypeId) && Category(i.Id) == category)?.Id ?? 0;

    public bool IsStone(int tpl) => StoneTypes.Contains(_data.GetItem(tpl).TypeId);

    public string Category(int stoneTpl) => _data.GetItem(stoneTpl).TypeId switch
    {
        124 => Donjon,
        125 => Archi,
        _ => Monstre,
    };

    /// <summary>Monstres d'une pierre possédée, d'après ses effets (un effet par monstre capturé).</summary>
    public static List<int> MonstersOf(string effects)
    {
        var result = new List<int>();
        foreach (var part in effects.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split('#');
            if (f.Length >= 5 && int.TryParse(f[0], NumberStyles.HexNumber, null, out var id) && id == CapturedMonsterEffect
                && int.TryParse(f[4], NumberStyles.HexNumber, null, out var monster))
                result.Add(monster);
        }
        return result;
    }

    /// <summary>
    /// Vrai si cette pierre, vendue dans la liste du monstre, compte pour son prix. Une pierre d'archi-monstre
    /// ne compte que pour l'archi-monstre, une pierre de donjon que pour son boss : pas pour leurs accompagnants
    /// (un Chafer dans la pierre de Dark Vlad). Le boss n'est pas indiqué par le jeu : c'est le monstre de plus
    /// haut niveau de la pierre.
    /// </summary>
    bool Counts(int monster, int stone, string effects)
    {
        if (!IsStone(stone)) return false;
        return Category(stone) switch
        {
            Archi => _data.GetMonster(monster).IsArchi,
            Donjon => BossOf(MonstersOf(effects)) is not { } boss || boss == monster,
            _ => true,
        };
    }

    /// <summary>Boss d'une pierre de donjon : le monstre de plus haut niveau (null si contenu inconnu).</summary>
    int? BossOf(List<int> monsters) =>
        monsters.Count == 0 ? null : monsters.MaxBy(id => _data.GetMonster(id).LevelMax);

    /// <summary>Monstre principal d'une pierre possédée : l'archi-monstre, le boss d'un donjon, sinon le plus haut niveau.</summary>
    public int? MainMonster(int stone, List<int> monsters) =>
        Category(stone) == Archi && monsters.FirstOrDefault(id => _data.GetMonster(id).IsArchi) is var archi && archi != 0
            ? archi : BossOf(monsters);

    /// <summary>Offres de l'HDV (message EHl sans le préfixe) : seules les listes de pierres d'âme sont retenues.</summary>
    public void OnOffers(string body)
    {
        var parts = body.Split('|');
        _current = null;
        _offers.Clear();
        _listingCategories.Clear();
        if (!int.TryParse(parts[0], out var monster)) return;

        bool stones = false;
        foreach (var line in parts.Skip(1))
        {
            // "id;effets;×1;×10;×100;pierre"
            var f = line.Split(';');
            if (f.Length < 6 || !long.TryParse(f[0], out var id) || !int.TryParse(f[5], out var stone) || !IsStone(stone)) continue;
            stones = true;
            if (Counts(monster, stone, f[1])) _offers[id] = (UnitPrice(f[2], f[3], f[4]), stone);
        }
        if (!stones) return; // liste d'un objet ordinaire
        _current = monster;
        foreach (var o in _offers.Values) _listingCategories.Add(Category(o.Stone));
        Record();
    }

    /// <summary>Offre ajoutée ou retirée pendant la consultation (message EHm sans le préfixe).</summary>
    public void OnOfferMove(string body)
    {
        if (_current is not { } monster || body.Length < 2) return;
        var f = body[1..].Split('|');
        if (!long.TryParse(f[0], out var id)) return;
        if (body[0] == '+' && f.Length >= 6 && int.TryParse(f[1], out var stone) && Counts(monster, stone, f[2]))
        {
            _offers[id] = (UnitPrice(f[3], f[4], f[5]), stone);
            _listingCategories.Add(Category(stone));
        }
        else if (body[0] != '-' || !_offers.Remove(id)) return;
        Record();
    }

    /// <summary>Prix moyen (message EHP sans le préfixe) : retenu s'il concerne le monstre affiché.</summary>
    public void OnAverage(string body)
    {
        var parts = body.Split('|');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var monster) || monster != _current
            || !long.TryParse(parts[1].Split(',')[0], out var avg) || avg <= 0) return;
        foreach (var c in _listingCategories) Get(monster, c).Average = avg;
        Save();
        Changed?.Invoke();
    }

    void Record()
    {
        if (_current is not { } monster) return;
        var now = DateTime.Now;
        foreach (var category in _listingCategories)
        {
            var offers = _offers.Values.Where(o => Category(o.Stone) == category).ToList();
            var price = Get(monster, category);
            var cheapest = offers.Where(o => o.Price != null).MinBy(o => o.Price);
            price.Price = cheapest.Price;
            if (cheapest.Stone != 0) price.StoneTemplate = cheapest.Stone;
            price.Offers = offers.Count;
            price.SeenAt = now;
            price.Rules = CurrentRules;
            price.Shared = false;
            AddSnapshot(price);

            Log?.Invoke($"HDV pierres d'âme {_data.GetMonster(monster).Name} ({category}) : {price.Price:N0} k ({price.Offers} offre(s)).");
            Observed?.Invoke(price);
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>Un relevé par consultation : on remplace le précédent s'il date de moins d'une minute.</summary>
    static void AddSnapshot(PdaPrice price)
    {
        var snap = new PdaSnapshot { Time = price.SeenAt, Price = price.Price, Offers = price.Offers };
        var last = price.History.LastOrDefault();
        if (last != null && (price.SeenAt - last.Time).Duration() < TimeSpan.FromMinutes(1)) price.History[^1] = snap;
        else price.History.Add(snap);
        if (price.History.Count > MaxHistory) price.History.RemoveRange(0, price.History.Count - MaxHistory);
    }

    /// <summary>
    /// Fusionne les prix reçus du groupe : un relevé distant ne remplace le local que s'il est plus récent.
    /// Retourne le nombre de prix mis à jour.
    /// </summary>
    public int MergeShared(IEnumerable<(int Monster, int Category, long? Price, int Offers, long? Average, DateTime SeenAt)> prices)
    {
        int updated = 0;
        foreach (var r in prices)
        {
            var category = CategoryName(r.Category);
            var price = Get(r.Monster, category);
            if (r.SeenAt <= price.SeenAt) continue;
            price.Price = r.Price;
            price.Offers = r.Offers;
            if (r.Average != null) price.Average = r.Average;
            if (price.StoneTemplate == 0) price.StoneTemplate = StoneOf(category);
            price.SeenAt = r.SeenAt;
            price.Rules = CurrentRules;
            price.Shared = true;
            AddSnapshot(price);
            updated++;
        }
        if (updated > 0)
        {
            Save();
            Changed?.Invoke();
        }
        return updated;
    }

    public void Forget(string key)
    {
        if (!Prices.Remove(key)) return;
        Save();
        Changed?.Invoke();
    }

    PdaPrice Get(int monster, string category)
    {
        var key = Key(monster, category);
        if (!Prices.TryGetValue(key, out var p)) Prices[key] = p = new PdaPrice { MonsterId = monster, Category = category };
        return p;
    }

    /// <summary>Meilleur prix par unité parmi les lots ×1, ×10, ×100 (prix écrits "547,0" ; vide ou 0 = aucun lot).</summary>
    static long? UnitPrice(string lot1, string lot10, string lot100)
    {
        static long? P(string s) => long.TryParse(s.Split(',')[0], out var v) && v > 0 ? v : null;
        return new[] { P(lot1), P(lot10) / 10, P(lot100) / 100 }.Where(p => p > 0).Min();
    }

    void Load()
    {
        try
        {
            if (File.Exists(PricesPath))
                Prices = (JsonSerializer.Deserialize<Dictionary<string, PdaPrice>>(File.ReadAllText(PricesPath)) ?? new())
                    // Les relevés de la première version (clé = monstre seul, catégories mélangées) sont abandonnés
                    .Where(kv => kv.Key.Contains(':'))
                    // Relevés « Donjon » d'avant la règle du boss : ils pouvaient appartenir à un accompagnant
                    .Where(kv => kv.Value.Category != Donjon || kv.Value.Rules >= CurrentRules).ToDictionary();
        }
        catch { Prices = new(); }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(PricesPath, JsonSerializer.Serialize(Prices));
        }
        catch (Exception ex) { Log?.Invoke("Sauvegarde des prix de pierres d'âme impossible : " + ex.Message); }
    }
}
