using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DofusBankManager.Net;

public sealed class Snapshot
{
    public string Character { get; set; } = "";
    public long BankKamas { get; set; }
    public DateTime BankUpdatedAt { get; set; }
    /// <summary>uid de l'objet -> (template, quantité)</summary>
    public Dictionary<long, OwnedItem> Bank { get; set; } = new();
    public Dictionary<long, OwnedItem> Inventory { get; set; } = new();
}

public sealed record OwnedItem(int TemplateId, int Quantity);

/// <summary>
/// Interprète les messages du protocole Dofus Retro (1.29) pour suivre le contenu
/// de la banque et de l'inventaire du personnage.
///
///   ASK|id|nom|...|objets         infos personnage + inventaire (objets "uid~tpl~qté~pos~effets" en hexa, séparés par ';')
///   ECK5                          ouverture d'un échange de type 5 = banque
///   ELO...;O...;G123              contenu de la banque à l'ouverture
///   EsKO+uid|qté|tpl|effets       objet ajouté/modifié dans la banque (décimal)
///   EsKO-uid                      objet retiré de la banque
///   EsKG123                       kamas en banque
///   EV                            fin de l'échange
///   OAKO...  / OR uid / OQ uid|qté   modifications de l'inventaire
/// </summary>
public sealed class DofusTracker
{
    const int BankExchangeType = 5;
    int _exchangeType = -1;

    public Snapshot State { get; private set; } = new();

    public event Action? BankChanged;
    public event Action? InventoryChanged;
    public event Action<string>? Log;

    public static string BanksDir => Path.Combine(Data.GameData.AppDir, "banks");

    /// <summary>
    /// Messages de banque/inventaire, les seuls autorisés dans le journal de diagnostic.
    /// ASK est exclu : il contient le nom du personnage. Tout le reste (connexion, ticket,
    /// messages privés…) ne doit jamais être écrit sur disque.
    /// </summary>
    public static bool IsLoggable(string msg) =>
        msg.StartsWith("ECK") || msg.StartsWith("EL") || msg.StartsWith("EsK") || msg == "EV"
        || msg.StartsWith("OAKO")
        || (msg.StartsWith("OR") && long.TryParse(msg.AsSpan(2), out _))
        || (msg.StartsWith("OQ") && msg.Length > 2 && char.IsDigit(msg[2]));

    public void Handle(string msg)
    {
        if (msg.StartsWith("ASK|")) OnCharacter(msg);
        else if (msg.StartsWith("ECK")) OnExchangeCreate(msg);
        else if (msg == "EV" || msg.StartsWith("EVa")) _exchangeType = -1;
        else if (msg.StartsWith("EL") && _exchangeType == BankExchangeType) OnBankList(msg[2..]);
        else if (msg.StartsWith("EsK") && _exchangeType == BankExchangeType) OnBankMove(msg[3..]);
        else if (msg.StartsWith("OAKO")) OnInventoryAdd(msg[4..]);
        else if (msg.StartsWith("OR") && long.TryParse(msg.AsSpan(2), out var removed)) OnInventoryRemove(removed);
        else if (msg.StartsWith("OQ")) OnInventoryQuantity(msg[2..]);
    }

    void OnCharacter(string msg)
    {
        var parts = msg.Split('|');
        if (parts.Length < 3) return;
        var name = parts[2];
        if (name != State.Character)
        {
            // Changement de personnage : on recharge sa dernière banque connue
            State = LoadSnapshot(name) ?? new Snapshot { Character = name };
            BankChanged?.Invoke();
        }
        State.Inventory.Clear();
        var items = parts.LastOrDefault(p => p.Contains('~')) ?? "";
        foreach (var s in items.Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (TryParseHexItem(s, out var uid, out var item)) State.Inventory[uid] = item;
        Log?.Invoke($"Personnage détecté : {name} ({State.Inventory.Count} objets en inventaire).");
        InventoryChanged?.Invoke();
    }

    void OnExchangeCreate(string msg)
    {
        var body = msg[3..];
        int end = body.IndexOf('|');
        if (end >= 0) body = body[..end];
        _exchangeType = int.TryParse(body, out var t) ? t : -1;
    }

    void OnBankList(string data)
    {
        State.Bank.Clear();
        foreach (var entry in data.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry[0] == 'O' && TryParseHexItem(entry[1..], out var uid, out var item)) State.Bank[uid] = item;
            else if (entry[0] == 'G' && long.TryParse(entry.AsSpan(1), out var k)) State.BankKamas = k;
        }
        Log?.Invoke($"Banque reçue : {State.Bank.Count} objets, {State.BankKamas:N0} kamas.");
        CommitBank();
    }

    void OnBankMove(string data)
    {
        if (data.Length < 2) return;
        if (data[0] == 'G')
        {
            if (long.TryParse(data.AsSpan(1), out var k)) State.BankKamas = k;
        }
        else if (data[0] == 'O' && data[1] == '+')
        {
            var p = data[2..].Split('|');
            if (p.Length >= 3 && long.TryParse(p[0], out var uid) && int.TryParse(p[1], out var qty) && int.TryParse(p[2], out var tpl))
                State.Bank[uid] = new OwnedItem(tpl, qty);
        }
        else if (data[0] == 'O' && data[1] == '-')
        {
            if (long.TryParse(data.AsSpan(2), out var uid)) State.Bank.Remove(uid);
        }
        else return;
        CommitBank();
    }

    void OnInventoryAdd(string data)
    {
        foreach (var s in data.Split('*', StringSplitOptions.RemoveEmptyEntries))
            if (TryParseHexItem(s, out var uid, out var item)) State.Inventory[uid] = item;
        InventoryChanged?.Invoke();
    }

    void OnInventoryRemove(long uid)
    {
        if (State.Inventory.Remove(uid)) InventoryChanged?.Invoke();
    }

    void OnInventoryQuantity(string data)
    {
        var p = data.Split('|');
        if (p.Length >= 2 && long.TryParse(p[0], out var uid) && int.TryParse(p[1], out var qty)
            && State.Inventory.TryGetValue(uid, out var it))
        {
            State.Inventory[uid] = it with { Quantity = qty };
            InventoryChanged?.Invoke();
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
        item = new OwnedItem(tpl, qty);
        return true;
    }

    void CommitBank()
    {
        State.BankUpdatedAt = DateTime.Now;
        SaveSnapshot();
        BankChanged?.Invoke();
    }

    // --- Persistance : la dernière banque connue est conservée entre deux lancements ---

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    static string SnapshotPath(string character) =>
        Path.Combine(BanksDir, string.Concat((character.Length > 0 ? character : "_inconnu").Split(Path.GetInvalidFileNameChars())) + ".json");

    void SaveSnapshot()
    {
        try
        {
            Directory.CreateDirectory(BanksDir);
            File.WriteAllText(SnapshotPath(State.Character), JsonSerializer.Serialize(State, JsonOpts));
        }
        catch (Exception ex) { Log?.Invoke($"Sauvegarde impossible : {ex.Message}"); }
    }

    static Snapshot? LoadSnapshot(string character)
    {
        try
        {
            var path = SnapshotPath(character);
            return File.Exists(path) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path)) : null;
        }
        catch { return null; }
    }

    /// <summary>Recharge la banque la plus récemment sauvegardée (au démarrage).</summary>
    public void LoadLatest()
    {
        if (!Directory.Exists(BanksDir)) return;
        var latest = new DirectoryInfo(BanksDir).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
        if (latest == null) return;
        try
        {
            var s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(latest.FullName));
            if (s == null) return;
            State = s;
            Log?.Invoke($"Dernière banque connue chargée : {s.Character} ({s.Bank.Count} objets, {s.BankUpdatedAt:g}).");
            BankChanged?.Invoke();
        }
        catch { }
    }
}
