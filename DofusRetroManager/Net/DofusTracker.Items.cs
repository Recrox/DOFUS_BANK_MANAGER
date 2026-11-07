using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DofusRetroManager.Net;

public sealed partial class DofusTracker
{
    /// <summary>
    /// Objet posé ou mis à jour dans l'atelier : "uid|qté|tpl|effets" (décimal),
    /// ou seulement "uid|qté" : on reprend alors l'objet connu dans l'inventaire.
    /// </summary>
    void OnCraftItem(string data)
    {
        var p = data.Split('|');
        if (p.Length < 1 || !long.TryParse(p[0], out var uid)) return;
        if (p.Length >= 4 && int.TryParse(p[1], out var qty) && int.TryParse(p[2], out var tpl))
        {
            var item = new OwnedItem(tpl, qty, p[3]);
            if (Cur.Inventory.ContainsKey(uid)) UpdateItem(uid, item);
            else if (item.Effects.Length > 0) CraftItemSeen?.Invoke(uid, tpl, item.Effects);
        }
        else if (Cur.Inventory.TryGetValue(uid, out var known) && known.Effects.Length > 0)
            CraftItemSeen?.Invoke(uid, known.TemplateId, known.Effects);
    }

    /// <summary>Met à jour un objet de l'inventaire et le signale s'il bouge pendant un atelier.</summary>
    void UpdateItem(long uid, OwnedItem item)
    {
        if (InCraft && Cur.Inventory.TryGetValue(uid, out var old) && item.Quantity < old.Quantity)
            CraftItemConsumed?.Invoke(uid, item.TemplateId, old.Quantity - item.Quantity);
        int before = Cur.Inventory.TryGetValue(uid, out var known) && known.TemplateId == item.TemplateId ? known.Quantity : 0;
        Cur.Inventory[uid] = item;
        if (item.Quantity != before) InventoryDelta?.Invoke(Cur.Character, item.TemplateId, item.Quantity - before);
        if (InCraft && item.Effects.Length > 0)
            CraftItemSeen?.Invoke(uid, item.TemplateId, item.Effects);
    }

    void OnCharacter(string msg)
    {
        var parts = msg.Split('|');
        if (parts.Length < 3) return;
        var name = parts[2];
        // Personnage de cette fenêtre : le même objet s'il est déjà suivi, sinon sa dernière banque connue
        var snap = State.Character == name ? State
            : _byStream.Values.FirstOrDefault(s => s.Character == name) ?? LoadSnapshot(name) ?? new Snapshot { Character = name };
        _byStream[_stream] = snap;
        _pending.Remove(_stream);
        if (snap != State)
        {
            State = snap;
            BankChanged?.Invoke();
        }
        if (long.TryParse(parts[1], out var id))
        {
            snap.CharacterId = id;
            // "ASK|id|nom|niveau|classe|sexe|apparence|couleurs…|objets" : seules la classe et le sexe sont gardés (icône du Switcheur)
            var look = parts.Length > 5 && int.TryParse(parts[4], out var breed) && int.TryParse(parts[5], out var sex)
                ? new Data.CharacterLook(breed, sex) : null;
            CharacterSelected?.Invoke(id, name, parts.Length > 3 && int.TryParse(parts[3], out var level) ? level : 0,
                look is { IsKnown: true } ? look : null);
        }
        Cur.Inventory.Clear();
        var items = parts.LastOrDefault(p => p.Contains('~')) ?? "";
        foreach (var s in items.Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (TryParseHexItem(s, out var uid, out var item)) Cur.Inventory[uid] = item;
        InventoryComplete = true;
        Log?.Invoke($"Personnage détecté : {name} ({Cur.Inventory.Count} objets en inventaire).");
        // Inventaire enregistré tout de suite : les onglets PDA, Banque, TTG et Ocre le voient même sans ouvrir de banque
        SaveSnapshot(snap);
        InventoryChanged?.Invoke();
    }

    void OnExchangeCreate(string msg)
    {
        var body = msg[3..];
        int end = body.IndexOf('|');
        if (end >= 0) body = body[..end];
        _exchangeType = int.TryParse(body, out var t) ? t : -1;
        // Banque ouverte dans une autre fenêtre : son personnage devient celui affiché
        if (_exchangeType == BankExchangeType && Cur != State)
        {
            State = Cur;
            BankChanged?.Invoke();
        }
        // Boutique d'un marchand : "ECK4|id du marchand sur la map"
        if (_exchangeType == ShopExchangeType && end >= 0 && long.TryParse(msg.AsSpan(3 + end + 1), out var seller))
            ShopOpened?.Invoke(seller);
        Log?.Invoke($"Échange ouvert (type {_exchangeType}){(_exchangeType == BankExchangeType ? " : banque" : "")}.");
        ExchangeChanged?.Invoke(Cur.Character, _exchangeType, end >= 0 ? msg[(3 + end + 1)..] : "");
        if (InCraft) CraftOpened?.Invoke();
    }

    void OnBankList(string data)
    {
        Cur.Bank.Clear();
        foreach (var entry in data.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry[0] == 'O' && TryParseHexItem(entry[1..], out var uid, out var item)) Cur.Bank[uid] = item;
            else if (entry[0] == 'G' && long.TryParse(entry.AsSpan(1), out var k)) Cur.BankKamas = k;
        }
        Log?.Invoke($"Banque reçue : {Cur.Bank.Count} objets, {Cur.BankKamas:N0} kamas.");
        CommitBank();
    }

    void OnBankMove(string data)
    {
        if (data.Length < 2) return;
        if (data[0] == 'G')
        {
            if (long.TryParse(data.AsSpan(1), out var k)) Cur.BankKamas = k;
        }
        else if (data[0] == 'O' && data[1] == '+')
        {
            var p = data[2..].Split('|');
            if (p.Length >= 3 && long.TryParse(p[0], out var uid) && int.TryParse(p[1], out var qty) && int.TryParse(p[2], out var tpl))
                Cur.Bank[uid] = new OwnedItem(tpl, qty, p.Length > 3 ? p[3] : "");
        }
        else if (data[0] == 'O' && data[1] == '-')
        {
            if (long.TryParse(data.AsSpan(2), out var uid)) Cur.Bank.Remove(uid);
        }
        else return;
        CommitBank();
    }

    void OnInventoryAdd(string data)
    {
        foreach (var s in data.Split('*', ';', StringSplitOptions.RemoveEmptyEntries))
            if (TryParseHexItem(s, out var uid, out var item)) UpdateItem(uid, item);
        InventoryEdited();
    }

    void OnInventoryRemove(long uid)
    {
        // Fenêtre pas encore reconnue : le retrait sera appliqué à son personnage
        if (!_byStream.ContainsKey(_stream)) { _ = Cur; _pending[_stream].Removed.Add(uid); }
        if (!Cur.Inventory.Remove(uid, out var old)) return;
        if (InCraft) CraftItemConsumed?.Invoke(uid, old.TemplateId, old.Quantity);
        InventoryDelta?.Invoke(Cur.Character, old.TemplateId, -old.Quantity);
        InventoryEdited();
    }

    void OnInventoryQuantity(string data)
    {
        var p = data.Split('|');
        if (p.Length >= 2 && long.TryParse(p[0], out var uid) && int.TryParse(p[1], out var qty)
            && Cur.Inventory.TryGetValue(uid, out var it))
        {
            UpdateItem(uid, it with { Quantity = qty });
            InventoryEdited();
        }
    }

    /// <summary>Format "uid~template~quantité~position~effets", nombres en hexadécimal.</summary>
    static bool TryParseHexItem(string s, out long uid, out OwnedItem item)
    {
        item = null!;
        uid = 0;
        var p = s.Split('~');
        if (p.Length < 3
            || !long.TryParse(p[0], NumberStyles.HexNumber, null, out uid)
            || !int.TryParse(p[1], NumberStyles.HexNumber, null, out var tpl)
            || !int.TryParse(p[2], NumberStyles.HexNumber, null, out var qty))
            return false;
        item = new OwnedItem(tpl, qty, p.Length > 4 ? p[4] : "");
        return true;
    }

    void CommitBank()
    {
        Cur.BankUpdatedAt = DateTime.Now;
        SaveSnapshot(Cur);
        BankChanged?.Invoke();
    }

    /// <summary>Inventaire modifié : enregistré aussitôt, pour que les autres onglets le voient sans ouvrir de banque.</summary>
    void InventoryEdited()
    {
        if (Cur.Character.Length > 0) SaveSnapshot(Cur);
        InventoryChanged?.Invoke();
    }

    /// <summary>
    /// Contenu du classeur TTG, envoyé à son ouverture : « idPersonnage|carte,date;carte,date… » (dates en millisecondes).
    /// Seuls les identifiants des cartes sont gardés, pour le personnage concerné (celui de la fenêtre, sinon sa banque sauvegardée).
    /// </summary>
    void OnBinder(string data)
    {
        var p = data.Split('|');
        if (!long.TryParse(p[0], out var owner)) return;
        var cards = (p.Length > 1 ? p[1] : "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => int.TryParse(e.Split(',')[0], out var c) ? c : 0).Where(c => c > 0).ToHashSet();
        var target = Cur.CharacterId == 0 || Cur.CharacterId == owner ? Cur
            : AllSnapshots().FirstOrDefault(s => s.CharacterId == owner && s.Character.Length > 0);
        if (target == null) return;
        target.Binder = cards;
        target.BinderUpdatedAt = DateTime.Now;
        SaveSnapshot(target);
        BankChanged?.Invoke();
    }

    /// <summary>Carte rangée dans le classeur (« ta » + carte), par le personnage de la fenêtre.</summary>
    void OnBinderAdd(int card)
    {
        if (!Cur.Binder.Add(card)) return;
        Cur.BinderUpdatedAt = DateTime.Now;
        SaveSnapshot(Cur);
        BankChanged?.Invoke();
    }
}
