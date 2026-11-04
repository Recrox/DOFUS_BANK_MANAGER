using System.Windows;
using System.Windows.Controls;
using DofusRetroManager.Data;
using DofusRetroManager.Hdv;

namespace DofusRetroManager;

/// <summary>Ligne de l'onglet PDA : un monstre (prix relevé à l'HDV) ou une pierre possédée.</summary>
public sealed record PdaRow(string Key, int MonsterId, int StoneTemplate, string Name, string Level, int LevelSort, string Race,
    string Category, long? Price, long? Average, int? Offers, string Seen, DateTime SeenAt, int Owned, string Where)
{
    public int Index { get; init; }
}

/// <summary>
/// Onglet PDA : prix des pierres d'âme pleines par monstre (relevés à l'HDV des pierres d'âme) et pierres
/// possédées, avec leur contenu et une valeur estimée. Simple affichage, rien n'est demandé au serveur.
/// </summary>
public partial class MainWindow
{
    PdaTracker? _pda;

    void InitPda(GameData data)
    {
        _pda = new PdaTracker(data);
        _pda.Log += AddLog;
        _pda.Changed += () => { ApplyPdaFilters(); ShowPdaDetail(); RefreshHunt(); };
        _tracker.HdvOffers += _pda.OnOffers;
        _tracker.HdvOfferMove += _pda.OnOfferMove;
        _tracker.HdvAverage += _pda.OnAverage;
    }

    const string AllCharacters = "Tous les personnages";
    bool _pdaCharsUpdating;

    void PdaFilters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_pdaCharsUpdating) ApplyPdaFilters();
    }

    /// <summary>
    /// Liste « Personnage » (PDA, TTG, Ocre) : « Tous les personnages » puis ceux dont une banque est connue ; la sélection
    /// est gardée. Rend le personnage choisi, null pour tous.
    /// </summary>
    string? CharacterChoice(ComboBox box, ref bool updating)
    {
        var chars = _tracker.AllSnapshots().Select(x => x.Character).Where(n => n.Length > 0).Distinct().Order()
            .Prepend(AllCharacters).ToList();
        var chosen = box.SelectedItem as string ?? AllCharacters;
        if (!chars.SequenceEqual(box.Items.Cast<string>()))
        {
            updating = true;
            box.ItemsSource = chars;
            if (!chars.Contains(chosen)) chosen = AllCharacters;
            box.SelectedItem = chosen;
            updating = false;
        }
        return chosen == AllCharacters ? null : chosen;
    }

    /// <summary>
    /// Objets en banque et en inventaire d'un personnage (null : tous) : celui en jeu (état en cours), les autres d'après
    /// leur dernière banque connue, avec leur emplacement (« Banque de … », « Inventaire de … »). La banque est commune
    /// aux personnages d'un même compte : un objet (même identifiant) vu dans plusieurs banques n'est compté qu'une fois,
    /// d'après la banque relevée le plus récemment.
    /// </summary>
    List<(Net.OwnedItem Item, string Where)> OwnedItems(string? character)
    {
        var snapshots = _tracker.AllSnapshots().Where(x => x.Character.Length > 0 || x == _tracker.State)
            .Where(x => character == null || x.Character == character).ToList();
        static string Of(string name) => name.Length > 0 ? $" de {name}" : "";
        var bank = snapshots.SelectMany(x => x.Bank.Select(b => (Uid: b.Key, Item: b.Value, Snap: x)))
            .GroupBy(b => b.Uid)
            .Select(g =>
            {
                var latest = g.MaxBy(b => b.Snap.BankUpdatedAt);
                var names = g.Select(b => b.Snap.Character).Where(n => n.Length > 0).Distinct().Order().ToList();
                return (latest.Item, Where: "Banque" + Of(string.Join(", ", names)));
            });
        return bank.Concat(snapshots.SelectMany(x => x.Inventory.Values.Select(i => (Item: i, Where: "Inventaire" + Of(x.Character)))))
            .ToList();
    }

    static string LevelText(MonsterInfo m) => m.LevelMin == m.LevelMax ? $"{m.LevelMax}" : $"{m.LevelMin}–{m.LevelMax}";

    void ApplyPdaFilters()
    {
        if (!_ready || _data == null || _pda == null) return;
        var search = PdaSearch.Text.Trim();
        var category = (PdaCategory.SelectedItem as ComboBoxItem)?.Content as string ?? "Toutes";

        // Pierres possédées (banque et inventaire) et leurs monstres, pour un personnage ou tous
        var chosen = CharacterChoice(PdaCharacter, ref _pdaCharsUpdating);
        var mine = PdaSource.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PdaCharacter.Visibility = PdaCharacterLabel.Visibility = mine;
        var stones = OwnedItems(PdaSource.SelectedIndex == 1 ? chosen : null)
            .Where(x => _pda.IsStone(x.Item.TemplateId))
            .Select(x => (x.Item, x.Where, Monsters: PdaTracker.MonstersOf(x.Item.Effects)))
            .ToList();
        var ownedByMonster = stones.SelectMany(x => x.Monsters.Distinct().Select(m => (m, x.Item.Quantity)))
            .GroupBy(x => x.m).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        // Valeur estimée d'une pierre : elle est vendue sous chacun de ses monstres, on retient le prix relevé le plus haut
        // pour sa catégorie (une pierre de monstre n'a pas le prix d'une pierre d'archi-monstre)
        PdaPrice? BestPrice(List<int> monsters, string category) => monsters.Distinct()
            .Select(id => _pda.Prices.GetValueOrDefault(PdaTracker.Key(id, category))).Where(p => p?.Price != null).MaxBy(p => p!.Price);

        List<PdaRow> rows;
        if (PdaSource.SelectedIndex == 0)
        {
            rows = _pda.Prices.Values.Select(p =>
            {
                var m = _data.GetMonster(p.MonsterId);
                return new PdaRow(p.Key, p.MonsterId, p.StoneTemplate, m.Name, LevelText(m), m.LevelMax, m.Race,
                    p.Category, p.Price, p.Average, p.Offers, Ago(p.SeenAt) + (p.Shared ? " · groupe" : ""), p.SeenAt,
                    ownedByMonster.GetValueOrDefault(p.MonsterId), "");
            }).ToList();

            // Comme l'HDV : « Tous les monstres du jeu » ajoute ceux sans prix relevé (hors PNJ, invocations de classe, tutoriel)
            if (PdaShowAll.IsChecked == true)
            {
                var priced = _pda.Prices.Values.Select(p => p.MonsterId).ToHashSet();
                var stoneIcons = new[] { PdaTracker.Monstre, PdaTracker.Archi }.ToDictionary(c => c, _pda.StoneOf);
                rows.AddRange(_data.Monsters.Values
                    .Where(m => !priced.Contains(m.Id) && m.Name.Length > 0 && m.RaceId is not (19 or 29 or 96))
                    .Select(m =>
                    {
                        var c = m.IsArchi ? PdaTracker.Archi : PdaTracker.Monstre;
                        return new PdaRow(PdaTracker.Key(m.Id, c), m.Id, stoneIcons[c], m.Name, LevelText(m), m.LevelMax, m.Race,
                            c, null, null, null, "", DateTime.MinValue, ownedByMonster.GetValueOrDefault(m.Id), "");
                    }));
            }
        }
        else
        {
            rows = stones.Select(x =>
            {
                var stoneCategory = _pda.Category(x.Item.TemplateId);
                var priced = BestPrice(x.Monsters, stoneCategory);
                var main = priced?.MonsterId
                    ?? _pda.MainMonster(x.Item.TemplateId, x.Monsters) ?? 0;
                var m = _data.GetMonster(main);
                var content = string.Join(", ", x.Monsters.GroupBy(id => id).OrderByDescending(g => _data.GetMonster(g.Key).LevelMax)
                    .Select(g => (g.Count() > 1 ? $"{g.Count()}× " : "") + _data.GetMonster(g.Key).Name));
                return new PdaRow(PdaTracker.Key(main, stoneCategory), main, x.Item.TemplateId, content.Length > 0 ? content : _data.GetItem(x.Item.TemplateId).Name,
                    LevelText(m), m.LevelMax, m.Race, stoneCategory, priced?.Price, priced?.Average, null,
                    priced != null ? Ago(priced.SeenAt) : "", priced?.SeenAt ?? DateTime.MinValue, x.Item.Quantity, x.Where);
            }).ToList();
        }

        var list = rows
            .Where(r => category == "Toutes" || r.Category == category)
            .Where(r => search.Length == 0 || Contains(r.Name, search) || Contains(r.Race, search))
            .OrderByDescending(r => r.Price ?? -1).ThenByDescending(r => r.LevelSort).ThenBy(r => r.Name)
            .Select((r, i) => r with { Index = i + 1 })
            .ToList();
        var selected = (PdaGrid.SelectedItem as PdaRow)?.Key;
        SetRows(PdaGrid, list);
        PdaGrid.SelectedItem = list.FirstOrDefault(r => r.Key == selected);

        PdaCount.Text = $"{list.Count} ligne(s) · {_pda.Prices.Count} prix relevé(s)";
        var values = stones.Select(x => BestPrice(x.Monsters, _pda.Category(x.Item.TemplateId))?.Price * x.Item.Quantity).ToList();
        PdaValue.Text = stones.Count == 0 ? ""
            : $"Mes pierres : {stones.Sum(x => x.Item.Quantity)} · valeur estimée {Kamas(values.Sum(v => v ?? 0))} "
              + $"({values.Count(v => v != null)}/{stones.Count} avec un prix)";
        PdaEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PdaEmpty.Text = PdaSource.SelectedIndex == 0
            ? (_pda.Prices.Count > 0 || PdaShowAll.IsChecked == true ? "Aucun monstre ne correspond à ces critères."
                : "Aucun prix relevé : à l'HDV des pierres d'âme, consultez un monstre, un boss ou un archi-monstre. "
                  + "Cochez « Tous les monstres du jeu » pour voir tous les monstres.")
            : "Aucune pierre d'âme pleine dans votre banque ou votre inventaire (ouvrez votre banque en jeu pour la relever).";
    }

    void PdaGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowPdaDetail();

    void ShowPdaDetail()
    {
        if (_data == null || _pda == null || PdaGrid.SelectedItem is not PdaRow row) return;
        var m = _data.GetMonster(row.MonsterId);
        var price = _pda.Prices.GetValueOrDefault(row.Key);
        PdaDetailIcon.Source = ItemIcons.Get(row.StoneTemplate);
        PdaDetailTitle.Text = m.Name;
        PdaDetailSub.Text = $"{m.Race} · niveau {LevelText(m)} · {row.Category}\n" + (price?.Price is { } p
            ? $"Pierre la moins chère : {Kamas(p)} · moyen : {Kamas(price.Average)}\n{price.Offers} offre(s) au dernier relevé ({Ago(price.SeenAt)})"
            : "Pas encore de prix : consultez ce monstre à l'HDV des pierres d'âme.");
        PdaChart.Points = price?.History.Where(sn => sn.Price != null).Select(sn => (sn.Time, (double)sn.Price!.Value)).ToList() ?? [];
    }

    void PdaForget_Click(object sender, RoutedEventArgs e)
    {
        if (PdaGrid.SelectedItem is PdaRow row) _pda?.Forget(row.Key);
    }
}
