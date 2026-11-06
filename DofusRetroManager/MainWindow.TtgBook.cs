using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DofusRetroManager.Data;

namespace DofusRetroManager;

/// <summary>
/// Carte du livre : State 2 = rangée au classeur, 1 = possédée ailleurs (banque, inventaire), 0 = manquante.
/// ArtItem : objet dont l'icône est l'image de la carte (celui de la carte normale pour une brillante).
/// </summary>
public sealed record BookCard(int Id, int Number, string Name, int Rarity, int ArtItem, bool Shiny, int State, string Tip, double? Price = null);

/// <summary>
/// Famille du livre : cartes normales et brillantes, et combien sont rangées au classeur. PackPrice : prix du paquet de la
/// famille (ItemId) ; MissingCost : prix connu des cartes normales manquantes. Tip : résumé affiché au survol.
/// </summary>
public sealed record BookFamily(int Id, string Name, int ItemId, int Done, int Owned, int Total, int ShinyDone, List<BookCard> Cards, List<BookCard> Shiny,
    double? PackPrice = null, double MissingCost = 0, string Tip = "");

public sealed record BookRarity(int Rarity, int Done, int Total);

/// <summary>Livre de la collection TTG, comme celui du jeu : totaux, raretés, familles et leurs cartes.</summary>
public sealed record TtgBook(string Owner, int Done, int Owned, int Total, int ShinyDone, int ShinyTotal, int FamiliesDone, int FamiliesTotal,
    List<BookRarity> Rarities, List<BookFamily> Families);

public partial class MainWindow
{
    /// <summary>
    /// Livre de la collection du personnage choisi dans l'onglet TTG (ou de tous) : une carte est « au classeur » si elle y
    /// est rangée (message reçu à l'ouverture du classeur en jeu), « possédée » si elle est en banque ou en inventaire.
    /// </summary>
    TtgBook? BuildTtgBook()
    {
        if (_data == null || _data.TtgCards.Count == 0) return null;

        var character = CharacterChoice(TtgCharacter, ref _ttgCharsUpdating);
        var owned = OwnedItems(character).Select(x => x.Item.TemplateId).ToHashSet();
        var binder = _tracker.AllSnapshots().Where(x => character == null ? x.Character.Length > 0 || x == _tracker.State : x.Character == character)
            .SelectMany(x => x.Binder).ToHashSet();

        BookCard Card(TtgCard c)
        {
            int art = c.ItemId > 0 ? c.ItemId : _data.TtgCardItems.GetValueOrDefault(c.Number);
            int state = binder.Contains(c.Id) ? 2 : !c.Shiny && c.ItemId > 0 && owned.Contains(c.ItemId) ? 1 : 0;
            var stars = new string('★', Math.Max(1, c.Rarity));
            var tip = $"#{c.Number} {c.Name}{(c.Shiny ? " (brillante)" : "")} · {stars}\n"
                + (state == 2 ? "Rangée au classeur" : state == 1 ? "Possédée (banque ou inventaire), pas au classeur" : "Manquante");
            double? price = c.ItemId > 0 ? BookCardPrice(c.ItemId) : null;
            tip += price is { } p ? $"\nPrix : {Kamas(p)}" : "\nPrix inconnu";
            return new BookCard(c.Id, c.Number, c.Name, c.Rarity, art, c.Shiny, state, tip, price);
        }

        var all = _data.TtgCards.Values.OrderBy(c => c.Number).Select(c => (c, b: Card(c))).ToList();
        var families = _data.TtgFamilies.Values.OrderBy(f => f.Id).Select(f =>
        {
            var normal = all.Where(x => x.c.Family == f.Id && !x.c.Shiny).Select(x => x.b).ToList();
            var shiny = all.Where(x => x.c.Family == f.Id && x.c.Shiny).Select(x => x.b).ToList();
            int done = normal.Count(b => b.State == 2), ownedCount = normal.Count(b => b.State > 0);
            double? pack = f.ItemId > 0 ? BookCardPrice(f.ItemId) : null;
            double missing = normal.Where(b => b.State == 0).Sum(b => b.Price ?? 0);
            var tip = $"{f.Name} : {done} / {normal.Count} au classeur" + (ownedCount > done ? $", {ownedCount} possédée(s)" : "")
                + (shiny.Count(b => b.State == 2) is > 0 and var sd ? $" · {sd} brillante(s)" : "")
                + $"\nPaquet : {(pack is { } pk ? Kamas(pk) : "prix inconnu")}"
                + (missing > 0 ? $"\nCartes manquantes : {Kamas(missing)}" : "");
            return new BookFamily(f.Id, f.Name, f.ItemId, done, ownedCount, normal.Count,
                shiny.Count(b => b.State == 2), normal, shiny, pack, missing, tip);
        }).Where(f => f.Total > 0).ToList();
        var normals = all.Where(x => !x.c.Shiny).Select(x => x.b).ToList();
        var shinies = all.Where(x => x.c.Shiny).Select(x => x.b).ToList();
        return new TtgBook(character ?? "tous vos personnages",
            normals.Count(b => b.State == 2), normals.Count(b => b.State > 0), normals.Count,
            shinies.Count(b => b.State == 2), shinies.Count,
            families.Count(f => f.Done == f.Total), families.Count,
            normals.GroupBy(b => b.Rarity).OrderBy(g => g.Key).Select(g => new BookRarity(g.Key, g.Count(b => b.State == 2), g.Count())).ToList(),
            families);
    }

    /// <summary>Prix d'une carte ou d'un paquet : HDV, sinon meilleur marchand de la période, sinon dernier prix marchand.</summary>
    double? BookCardPrice(int item)
    {
        var m = _merchants?.Prices.GetValueOrDefault(item);
        return UnitPrice(item) ?? m?.BestRecent(TtgDays) ?? m?.Last?.Price;
    }

}
