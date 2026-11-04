using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DofusRetroManager.Data;
using DofusRetroManager.Hdv;

namespace DofusRetroManager;

/// <summary>Ligne de l'onglet Ocre : une étape de la quête et ce qu'il y manque.</summary>
public sealed record OcreStepRow(int Step, string Kind, int Total, int Owned, int Missing, int MissingBoss, int MissingArchi,
    long? Cost, int Unpriced, string State, bool Validable, bool Done)
{
    public string CostText => Missing == 0 ? "" : (Cost is { } c ? $"{c:N0} k" : "—") + (Unpriced > 0 && Cost != null ? $" (+{Unpriced} sans prix)" : "");
}

/// <summary>Âme demandée à une étape : monstre, sa catégorie de pierre, possédée (où) ou son prix relevé à l'HDV.</summary>
public sealed record OcreSoulRow(int MonsterId, string Name, string Level, string Kind, bool Owned, string Where, long? Price, int StoneTemplate)
{
    public string PriceText => Owned ? "" : Price is { } p ? $"{p:N0} k" : "prix inconnu";
}

/// <summary>Étape atteinte dans la quête, par personnage (« » : tous), retenue entre deux lancements.</summary>
public sealed class OcreData
{
    public Dictionary<string, int> Step { get; set; } = new();
}

/// <summary>
/// Onglet Ocre : la quête du Dofus Ocre (« L'éternelle moisson »), ses 34 étapes et les âmes à rapporter (fichier de
/// langue « quests »), comparées aux pierres d'âme pleines de la banque et de l'inventaire. Les âmes manquantes sont
/// estimées au prix relevé à l'HDV des pierres d'âme (onglet PDA). Simple affichage : c'est le joueur qui rend les pierres.
/// </summary>
public partial class MainWindow
{
    static string OcrePath => Path.Combine(GameData.AppDir, "ocre.json");
    OcreData _ocre = new();
    bool _ocreUpdating;
    const string KindMonster = "Monstre", KindBoss = "Boss", KindArchi = "Archi-monstre";

    void InitOcre(GameData data)
    {
        try { if (File.Exists(OcrePath)) _ocre = JsonSerializer.Deserialize<OcreData>(File.ReadAllText(OcrePath)) ?? new(); }
        catch { _ocre = new(); }
        _ocreUpdating = true;
        OcreStep.ItemsSource = Enumerable.Range(1, Math.Max(1, data.OcreSteps.Count)).Select(i => $"Étape {i}").ToList();
        OcreStep.SelectedIndex = 0;
        _ocreUpdating = false;
        _tracker.BankChanged += ApplyOcre;
        _pda!.Changed += ApplyOcre;
        ApplyOcre();
    }

    void Ocre_Changed(object sender, RoutedEventArgs e)
    {
        if (_ocreUpdating) return;
        // Étape atteinte : retenue pour le personnage choisi
        if (sender == OcreStep && OcreStep.SelectedIndex >= 0)
        {
            _ocre.Step[OcreCharacterKey] = OcreStep.SelectedIndex + 1;
            try { File.WriteAllText(OcrePath, JsonSerializer.Serialize(_ocre)); } catch { }
        }
        ApplyOcre();
    }

    string OcreCharacterKey => OcreCharacter.SelectedIndex > 0 ? OcreCharacter.SelectedItem as string ?? "" : "";

    string OcreKind(int monster)
    {
        var m = _data!.GetMonster(monster);
        return m.IsArchi ? KindArchi : m.IsBoss ? KindBoss : KindMonster;
    }

    /// <summary>Prix relevé de la pierre la moins chère contenant ce monstre, dans la catégorie qui lui correspond.</summary>
    PdaPrice? OcrePrice(int monster)
    {
        if (_pda == null) return null;
        PdaPrice? P(string category) => _pda.Prices.GetValueOrDefault(PdaTracker.Key(monster, category)) is { Price: not null } p ? p : null;
        return OcreKind(monster) switch
        {
            KindArchi => P(PdaTracker.Archi),
            KindBoss => P(PdaTracker.Donjon) ?? P(PdaTracker.Monstre),
            _ => P(PdaTracker.Monstre) ?? P(PdaTracker.Donjon),
        };
    }

    /// <summary>Détail de chaque étape : âmes couvertes par vos pierres (et où), ou manquantes.</summary>
    List<List<OcreSoulRow>> _ocreSouls = [];

    void ApplyOcre()
    {
        if (!_ready || _data == null || _pda == null) return;
        var steps = _data.OcreSteps;
        if (steps.Count == 0)
        {
            OcreSummary.Text = "Étapes de la quête introuvables dans les données du jeu.";
            return;
        }

        var character = CharacterChoice(OcreCharacter, ref _ocreUpdating);
        _ocreUpdating = true;
        OcreStep.SelectedIndex = Math.Clamp(_ocre.Step.GetValueOrDefault(OcreCharacterKey, 1), 1, steps.Count) - 1;
        _ocreUpdating = false;
        int current = OcreStep.SelectedIndex + 1;
        var kind = (OcreKindFilter.SelectedItem as ComboBoxItem)?.Content as string ?? "Toutes";
        bool Counted(string k) => kind == "Toutes" || kind == k || (kind == "Boss et archi-monstres" && k != KindMonster);

        // Pierres pleines (une par exemplaire) : chacune ne sert qu'une fois, à l'étape où elle est rendue
        var stones = OwnedItems(character).Where(x => _pda.IsStone(x.Item.TemplateId))
            .SelectMany(x => Enumerable.Repeat((x.Item, x.Where, Monsters: PdaTracker.MonstersOf(x.Item.Effects).ToHashSet(),
                Archi: _pda.Category(x.Item.TemplateId) == PdaTracker.Archi), Math.Max(1, x.Item.Quantity)))
            .Select(x => (x.Item, x.Where, x.Monsters, x.Archi, Used: false))
            .ToList();

        var rows = new List<OcreStepRow>();
        _ocreSouls = [];
        bool chain = true; // étapes validables d'affilée depuis l'étape atteinte
        for (int i = 0; i < steps.Count; i++)
        {
            int number = i + 1;
            var souls = steps[i];
            var covered = new Dictionary<int, string>(); // monstre -> où est la pierre
            if (number >= current)
            {
                // Une pierre qui contient plusieurs âmes de l'étape les rend toutes : on prend celle qui en couvre le plus,
                // en gardant les pierres d'archi-monstre pour les archi-monstres
                while (true)
                {
                    var need = souls.Where(m => !covered.ContainsKey(m)).ToHashSet();
                    int best = -1, bestCount = 0;
                    for (int s = 0; s < stones.Count; s++)
                    {
                        if (stones[s].Used) continue;
                        int n = stones[s].Monsters.Count(m => need.Contains(m) && (stones[s].Archi || !_data.GetMonster(m).IsArchi));
                        if (n == 0) continue;
                        bool archiForMonsters = stones[s].Archi && !stones[s].Monsters.Any(m => need.Contains(m) && _data.GetMonster(m).IsArchi);
                        int score = n * 2 - (archiForMonsters ? 1 : 0);
                        if (score > bestCount) { best = s; bestCount = score; }
                    }
                    if (best < 0) break;
                    var stone = stones[best];
                    stones[best] = stone with { Used = true };
                    foreach (var m in stone.Monsters.Where(need.Contains)) covered[m] = stone.Where;
                }
            }

            var soulRows = souls.Select(m =>
            {
                var info = _data.GetMonster(m);
                var price = OcrePrice(m);
                return new OcreSoulRow(m, info.Name, LevelText(info), OcreKind(m), covered.ContainsKey(m), covered.GetValueOrDefault(m, ""),
                    price?.Price, price?.StoneTemplate ?? _pda.StoneOf(info.IsArchi ? PdaTracker.Archi : PdaTracker.Monstre));
            }).ToList();
            _ocreSouls.Add(soulRows);

            var missing = soulRows.Where(r => !r.Owned && Counted(r.Kind)).ToList();
            var allMissing = soulRows.Where(r => !r.Owned).ToList();
            bool done = number < current;
            bool validable = !done && allMissing.Count == 0;
            if (!done && !validable) chain = false;
            var kinds = souls.Select(OcreKind).Distinct().ToList();
            static string Plural(string k) => k == KindBoss ? "Boss" : k + "s";
            rows.Add(new OcreStepRow(number, string.Join(" + ", kinds.OrderBy(k => k == KindMonster ? 0 : k == KindBoss ? 1 : 2).Select(Plural)),
                souls.Count, soulRows.Count(r => r.Owned), missing.Count,
                missing.Count(r => r.Kind == KindBoss), missing.Count(r => r.Kind == KindArchi),
                missing.Any(r => r.Price != null) ? missing.Sum(r => r.Price ?? 0) : null, missing.Count(r => r.Price == null),
                done ? "Validée" : validable ? (chain ? "✔ Validable" : "✔ Complète (après les précédentes)") : $"Manque {allMissing.Count}",
                validable, done));
        }

        var selected = (OcreGrid.SelectedItem as OcreStepRow)?.Step ?? current;
        SetRows(OcreGrid, rows);
        OcreGrid.SelectedItem = rows.FirstOrDefault(r => r.Step == selected);

        // Résumé : étapes validables d'affilée, puis ce qu'il reste à acheter jusqu'à la fin
        int validUpTo = current - 1;
        while (validUpTo < rows.Count && rows[validUpTo].Validable) validUpTo++;
        var left = rows.Where(r => !r.Done).ToList();
        long cost = left.Sum(r => r.Cost ?? 0);
        OcreSummary.Text = (validUpTo >= current
                ? $"Validables avec vos pierres : étape{(validUpTo > current ? "s" : "")} {current}{(validUpTo > current ? $" à {validUpTo}" : "")}"
                : $"Étape {current} : il manque encore des pierres")
            + $" · reste à acheter ({(kind == "Toutes" ? "tout" : kind.ToLowerInvariant())}) jusqu'à l'étape {rows.Count} : {Kamas(cost)}"
            + (left.Sum(r => r.Unpriced) is > 0 and var u ? $" ({u} âme(s) sans prix)" : "");
        ShowOcreDetail();
    }

    void OcreGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowOcreDetail();

    void ShowOcreDetail()
    {
        if (OcreGrid.SelectedItem is not OcreStepRow row || row.Step - 1 >= _ocreSouls.Count)
        {
            OcreDetailTitle.Text = "Choisissez une étape";
            OcreDetailSub.Text = "";
            OcreSouls.ItemsSource = null;
            return;
        }
        var souls = _ocreSouls[row.Step - 1];
        OcreDetailTitle.Text = $"Étape {row.Step} · {row.Kind}";
        var missing = souls.Where(s => !s.Owned).ToList();
        string Count(string kind, string one, string many) => missing.Count(s => s.Kind == kind) is var n and > 0 ? $"{n} {(n > 1 ? many : one)}" : "";
        var parts = new[] { Count(KindMonster, "monstre", "monstres"), Count(KindBoss, "boss", "boss"), Count(KindArchi, "archi-monstre", "archi-monstres") }
            .Where(p => p.Length > 0).ToList();
        OcreDetailSub.Text = row.Done ? "Étape déjà validée (étape atteinte choisie plus haut)."
            : missing.Count == 0 ? $"Toutes les âmes sont dans vos pierres ({souls.Count}/{souls.Count})."
            : $"Il manque {string.Join(", ", parts)} : "
              + string.Join(", ", missing.Select(s => $"{s.Name} ({s.PriceText})"))
              + $"\nTotal : {Kamas(missing.Sum(s => s.Price ?? 0))}"
              + (missing.Count(s => s.Price == null) is > 0 and var u ? $" (+ {u} sans prix relevé)" : "");
        OcreSouls.ItemsSource = souls.OrderBy(s => s.Owned).ThenBy(s => s.Kind).ThenBy(s => s.Name).ToList();
    }
}
