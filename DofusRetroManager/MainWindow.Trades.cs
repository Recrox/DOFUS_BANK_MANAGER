using System.Text.Json;
using DofusRetroManager.Data;
using DofusRetroManager.Hdv;

namespace DofusRetroManager;

/// <summary>
/// Onglet HDV, « Mes échanges » : tableau de bord des achats et ventes de vos personnages (HDV et marchands). Les
/// données viennent de TradeTracker ; la page fait les totaux et les filtres (personnage, période).
/// Simple affichage : c'est toujours le joueur qui achète et qui vend (README, règle 3).
/// </summary>
public partial class MainWindow
{
    TradeTracker? _trades;

    void InitTrades(GameData data)
    {
        _trades = new TradeTracker(data);
        _trades.Log += AddLog;
        _trades.Changed += () => _webForce = true;
        _tracker.ExchangeChanged += (c, type, args) => Safe(() => _trades.OnExchange(c, type, args));
        _tracker.HdvOffers += body => Safe(() => _trades.OnOffers(body));
        _tracker.HdvOfferMove += body => Safe(() => _trades.OnOfferMove(body));
        _tracker.ShopList += body => Safe(() => _trades.OnShopList(body));
        _tracker.InventoryDelta += (c, tpl, qty) => Safe(() => _trades.OnInventoryDelta(c, tpl, qty));
        _tracker.HdvSellList += (c, body) => Safe(() => _trades.OnSellList(c, body));
        _tracker.HdvSellMove += (c, body) => Safe(() => _trades.OnSellMove(c, body));
        _tracker.HdvSaleInfo += (c, code, args) => Safe(() => _trades.OnSaleInfo(c, code, args));
        _sniffer.StreamClosed += stream => Ui(() => _trades.OnStreamClosed(stream));
        _windowTimer.Tick += (_, _) => _trades.Tick();
    }

    /// <summary>
    /// Échanges pour la page : une ligne par achat ou offre (date en ms, personnage, objet, quantité, prix, taxe, état),
    /// la plus récente en premier. i : position dans la liste complète (pour « Oublier »).
    /// </summary>
    string TradesJson()
    {
        if (_trades == null || _data == null) return "null";
        static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();
        var list = _trades.Trades.Select((t, i) => new
        {
            i, k = t.Kind, s = t.Source, t = Ms(t.Time), c = t.Character, id = t.ItemId, n = _data.GetItem(t.ItemId).Name,
            q = t.Quantity, p = t.Price, tax = t.Tax, st = t.State, ct = t.ClosedAt is { } c ? Ms(c) : (long?)null, u = t.SoldUnseen,
            hdv = UnitPrice(t.ItemId),
        }).Reverse();
        return JsonSerializer.Serialize(list);
    }

    void ForgetTrade(int index)
    {
        if (_trades != null && index >= 0 && index < _trades.Trades.Count) _trades.Remove(_trades.Trades[index]);
    }
}
