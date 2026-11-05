using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DofusRetroManager.Net;

public sealed class Snapshot
{
    public string Character { get; set; } = "";
    /// <summary>Identifiant du personnage en jeu : sert à retrouver sa ligne dans les résultats de combat (GE).</summary>
    public long CharacterId { get; set; }
    public long BankKamas { get; set; }
    public DateTime BankUpdatedAt { get; set; }
    /// <summary>uid de l'objet -> (template, quantité)</summary>
    public Dictionary<long, OwnedItem> Bank { get; set; } = new();
    public Dictionary<long, OwnedItem> Inventory { get; set; } = new();
    /// <summary>Cartes rangées dans le classeur TTG (identifiants de carte du fichier de langue « ttg »).</summary>
    public HashSet<int> Binder { get; set; } = new();
    public DateTime BinderUpdatedAt { get; set; }
}

/// <summary>Effects : effets de l'objet au format du protocole ("7e#5#0#0#0d0+5,…", identifiants et valeurs en hexa).</summary>
public sealed record OwnedItem(int TemplateId, int Quantity, string Effects = "");

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
///   ECK4|id + EL{uid;qté;tpl;effets;prix}|…   boutique d'un marchand (cartes TTG relevées)
///   EV                            fin de l'échange
///   OAKO...  / OR uid / OQ uid|qté   modifications de l'inventaire
///   OCO...                        objet de l'inventaire modifié (nouvelles caractéristiques, ex. après une rune)
///   ECK3                          ouverture d'un atelier (craft / forgemagie)
///   EmKO+uid|qté|tpl|effets       objet de l'atelier mis à jour (décimal)
///   Ec...                         résultat d'une action d'atelier
///
/// Onglet Chasse (lus, jamais journalisés : ils contiennent aussi des noms de joueurs, qui sont ignorés) :
///   GDM|map|…                     changement de map
///   GM|+…;-3;…|-id                sprites de la map : seuls les groupes de monstres (type -3) sont retenus
///   As…                           caractéristiques du personnage (sagesse, prospection)
///   GE…                           fin de combat : seule la ligne du personnage (XP, kamas, drops) et les monstres sont lus
///   PM+…|PM~…|PM-id / PV          membres du groupe : seuls le niveau et la prospection sont lus
///   SL… / GJK / GA;300;… / GA;303;…   sorts du personnage, entrée en combat, sorts lancés et attaques à l'arme (partie Économe)
///   GA300{sort};{case} / GA303{case}  envoyés par le client : action du personnage de cette fenêtre (son identifiant)
///   GTS{id}|…                     début du tour d'un combattant (multi-compte : affiche le personnage qui joue)
///   GTM|id;0;pv;PA;PM;case;;pvmax|…   état des combattants : seuls les points de vie du personnage sont lus
///   Gd{id};… / GdO{id} / GdK{id}  challenge du combat annoncé, réussi, raté (onglet Arène) : seul son identifiant est lu
///
/// Onglet Switcheur (notifications ; lus, jamais journalisés ni conservés) :
///   ERK{de}|{à}|{type}            demande d'échange : seul l'identifiant du destinataire est lu
///   PIK{invitant}|{invité}        invitation de groupe : seul le nom de l'invité est lu (comparé au personnage de la
///                                 fenêtre), celui de l'invitant est ignoré
/// </summary>
public sealed class DofusTracker
{
    const int BankExchangeType = 5;
    const int CraftExchangeType = 3;
    /// <summary>Boutique d'un joueur en mode marchand.</summary>
    const int ShopExchangeType = 4;
    int _exchangeType = -1;

    public Snapshot State { get; private set; } = new();

    public bool InCraft => _exchangeType == CraftExchangeType;

    /// <summary>
    /// Vrai une fois l'inventaire complet reçu (message ASK, envoyé seulement à la sélection du personnage).
    /// Si l'application démarre en cours de partie, l'inventaire n'est connu qu'en partie.
    /// </summary>
    public bool InventoryComplete { get; private set; }

    public event Action? BankChanged;
    public event Action? InventoryChanged;
    public event Action<string>? Log;

    /// <summary>
    /// Objet vu pendant un atelier, avec ses caractéristiques : (uid, template, effets).
    /// En forgemagie, Retro recrée l'objet à chaque rune (nouvel uid) : c'est au suivi FM
    /// de comparer avec l'état précédent du même objet.
    /// </summary>
    public event Action<long, int, string>? CraftItemSeen;
    public event Action? CraftOpened;
    /// <summary>Objet consommé pendant un atelier (rune, potion…) : (uid, template, quantité consommée).</summary>
    public event Action<long, int, int>? CraftItemConsumed;
    /// <summary>Résultat brut d'une action d'atelier (message Ec sans le préfixe).</summary>
    public event Action<string>? CraftResult;
    /// <summary>Message d'information du jeu reçu pendant un atelier, par code (ex. 183 = échec de forgemagie).</summary>
    public event Action<int>? CraftInfo;

    /// <summary>Offres d'un objet consulté à l'HDV (message EHl sans le préfixe).</summary>
    public event Action<string>? HdvOffers;
    /// <summary>Prix moyen d'un objet à l'HDV (message EHP sans le préfixe).</summary>
    public event Action<string>? HdvAverage;
    /// <summary>Offre ajoutée/retirée pendant la consultation de l'HDV (message EHm sans le préfixe).</summary>
    public event Action<string>? HdvOfferMove;
    /// <summary>Contenu de la boutique d'un marchand consultée (message EL sans le préfixe).</summary>
    public event Action<string>? ShopList;
    /// <summary>Boutique d'un marchand ouverte : identifiant de son sprite sur la map.</summary>
    public event Action<long>? ShopOpened;

    /// <summary>Nouvelle map (identifiant).</summary>
    public event Action<string>? MapChanged;
    /// <summary>Nouvelle map, avec la version de son fichier et sa clé de lecture : « id|date|clé » (onglet Récolte).</summary>
    public event Action<string>? MapLoaded;
    /// <summary>État des ressources de la map (message GDF sans le préfixe : « case;état;interactif|… »).</summary>
    public event Action<string>? MapResourceStates;
    /// <summary>Sprites ajoutés/retirés de la map (message GM sans le préfixe).</summary>
    public event Action<string>? MapSprites;
    /// <summary>Caractéristiques du personnage (message As sans le préfixe).</summary>
    public event Action<string>? CharacterStats;
    /// <summary>Fin de combat (message GE sans le préfixe).</summary>
    public event Action<string>? FightEnded;
    /// <summary>Membres du groupe ajoutés/modifiés/retirés (message PM sans le préfixe) ; null : groupe quitté.</summary>
    public event Action<string?>? PartyChanged;
    /// <summary>Personnage sélectionné sur cette connexion : (identifiant, nom, niveau, classe et sexe).</summary>
    public event Action<long, string, int, Data.CharacterLook?>? CharacterSelected;
    /// <summary>Nouveau niveau du personnage (message AN).</summary>
    public event Action<int>? LevelUp;
    /// <summary>Sorts du personnage (message SL sans le préfixe) : "sort~niveau~position;…".</summary>
    public event Action<string>? SpellList;
    /// <summary>Entrée en combat (message GJK).</summary>
    public event Action? FightJoined;
    /// <summary>Début du tour d'un combattant (message GTS{id}|durée).</summary>
    /// <summary>Début d'un tour (GTS) : combattant et durée du tour en millisecondes.</summary>
    public event Action<long, int>? TurnStarted;
    /// <summary>Régénération des points de vie hors combat (ILS) : un point toutes les N millisecondes.</summary>
    public event Action<int>? LifeRegen;
    /// <summary>État des combattants (message GTM| sans le préfixe) : points de vie du personnage.</summary>
    public event Action<string>? FightersUpdate;
    /// <summary>Challenge du combat (message Gd sans le préfixe) : annonce "{id};…", résultat "O{id}" ou "K{id}".</summary>
    public event Action<string>? Challenge;
    /// <summary>Action de combat (message GA sans le préfixe) : "id;type;acteur;paramètres".</summary>
    public event Action<string>? GameAction;
    /// <summary>Demande d'échange (message ERK) : identifiant du personnage à qui elle est adressée.</summary>
    public event Action<long>? ExchangeRequested;
    /// <summary>Invitation de groupe (message PIK) : nom du personnage invité (celui de l'invitant n'est pas lu).</summary>
    public event Action<string>? PartyInvited;

    /// <summary>Codes des messages de forgemagie (fichier lang : INFOS_117, INFOS_183, INFOS_194).</summary>
    public static readonly int[] FmInfoCodes = [117, 183, 194];

    public static string BanksDir => Path.Combine(Data.GameData.AppDir, "banks");

    /// <summary>
    /// Messages de banque, d'inventaire et d'atelier (forgemagie), les seuls autorisés dans le
    /// journal de diagnostic. Ils ne contiennent que des objets et leurs caractéristiques.
    /// ASK est exclu : il contient le nom du personnage. Tout le reste (connexion, ticket,
    /// messages privés…) ne doit jamais être écrit sur disque. Les messages de l'onglet Chasse (GDM, GM, As, GE, PM)
    /// contiennent des noms de joueurs : ils n'y figurent pas non plus.
    /// </summary>
    public static bool IsLoggable(string msg) =>
        msg.StartsWith("ECK") || msg.StartsWith("EL") || msg.StartsWith("EsK") || msg == "EV"
        || msg.StartsWith("OAKO") || msg.StartsWith("OCO")
        || msg.StartsWith("Ec") || msg.StartsWith("EmK") || msg.StartsWith("EMK")
        || msg.StartsWith("EH") // hôtel de vente : objets et prix, sans nom de vendeur
        || (msg.StartsWith("OR") && long.TryParse(msg.AsSpan(2), out _))
        || (msg.StartsWith("OQ") && msg.Length > 2 && char.IsDigit(msg[2]))
        || (InfoCodes(msg).Any() && InfoCodes(msg).All(c => FmInfoCodes.Contains(c))); // uniquement des messages de forgemagie

    /// <summary>
    /// Codes d'un message d'information « Im » : "Im0183", "Im0183;param" ou plusieurs séparés par '|'.
    /// Les autres messages Im (connexion d'amis…) contiennent des noms : ils ne sont jamais journalisés.
    /// </summary>
    static IEnumerable<int> InfoCodes(string msg)
    {
        if (!msg.StartsWith("Im")) yield break;
        foreach (var part in msg[2..].Split('|'))
        {
            var head = part.Split(';')[0];
            if (head.Length >= 2 && int.TryParse(head.AsSpan(1), out var code)) yield return code;
        }
    }

    // --- Multi-compte : chaque fenêtre de jeu (connexion) a son personnage, sa banque, son inventaire et son échange ---

    /// <summary>Personnage de chaque connexion, appris par ASK (même objet que State quand c'est le personnage suivi).</summary>
    readonly Dictionary<string, Snapshot> _byStream = new();
    readonly Dictionary<string, int> _exchanges = new();
    string _stream = "";

    /// <summary>
    /// Personnage de la connexion du message en cours : c'est lui que modifient les messages de banque et d'inventaire.
    /// Connexion inconnue (application lancée après la connexion) : le personnage suivi, comme avant.
    /// </summary>
    Snapshot Cur => _byStream.TryGetValue(_stream, out var s) ? s : State;

    /// <summary>Message reçu sur une connexion : il ne touche que le personnage de cette fenêtre de jeu.</summary>
    public void Handle(string stream, string msg)
    {
        _stream = stream;
        _exchangeType = _exchanges.GetValueOrDefault(stream, -1);
        try { Handle(msg); }
        finally { _exchanges[stream] = _exchangeType; }
    }

    /// <summary>
    /// Personnage d'une connexion retrouvé sans l'avoir vu à la sélection (application lancée après le jeu) : sa banque
    /// et son inventaire sauvegardés sont repris, et les messages de cette connexion ne touchent plus que lui.
    /// </summary>
    public void AssignStream(string stream, string name)
    {
        if (_byStream.ContainsKey(stream) || name.Length == 0) return;
        _byStream[stream] = State.Character == name ? State
            : _byStream.Values.FirstOrDefault(s => s.Character == name) ?? LoadSnapshot(name) ?? new Snapshot { Character = name };
    }

    /// <summary>Connexion fermée : son personnage n'est plus en jeu (il reste dans les banques sauvegardées).</summary>
    public void OnStreamClosed(string stream)
    {
        _byStream.Remove(stream);
        _exchanges.Remove(stream);
    }

    void Handle(string msg)
    {
        if (msg.StartsWith("ASK|")) OnCharacter(msg);
        else if (msg.StartsWith("ECK")) OnExchangeCreate(msg);
        else if (msg == "EV" || msg.StartsWith("EVa")) _exchangeType = -1;
        else if (msg.StartsWith("EL") && _exchangeType == BankExchangeType) OnBankList(msg[2..]);
        else if (msg.StartsWith("EL") && _exchangeType == ShopExchangeType) ShopList?.Invoke(msg[2..]);
        else if (msg.StartsWith("EsK") && _exchangeType == BankExchangeType) OnBankMove(msg[3..]);
        else if (msg.StartsWith("OAKO")) OnInventoryAdd(msg[4..]);
        else if (msg.StartsWith("OCO")) OnInventoryAdd(msg[3..]);
        else if (msg.StartsWith("OR") && long.TryParse(msg.AsSpan(2), out var removed)) OnInventoryRemove(removed);
        else if (msg.StartsWith("OQ")) OnInventoryQuantity(msg[2..]);
        else if (InCraft && (msg.StartsWith("EmKO+") || msg.StartsWith("EMKO+"))) OnCraftItem(msg[5..]);
        else if (msg.StartsWith("EHl")) HdvOffers?.Invoke(msg[3..]);
        else if (msg.StartsWith("EHP")) HdvAverage?.Invoke(msg[3..]);
        else if (msg.StartsWith("EHm")) HdvOfferMove?.Invoke(msg[3..]);
        else if (InCraft && msg.StartsWith("Ec")) CraftResult?.Invoke(msg[2..]);
        else if (InCraft && msg.StartsWith("Im"))
            foreach (var code in InfoCodes(msg)) CraftInfo?.Invoke(code);
        // Chasse
        else if (msg.StartsWith("GDM|"))
        {
            MapChanged?.Invoke(msg.Split('|')[1]);
            MapLoaded?.Invoke(msg[4..]);
        }
        // Récolte : état des ressources de la map (cases et états seulement)
        else if (msg.StartsWith("GDF|")) MapResourceStates?.Invoke(msg[4..]);
        else if (msg.StartsWith("GM|")) MapSprites?.Invoke(msg[3..]);
        else if (msg.StartsWith("As") && !msg.StartsWith("ASK")) CharacterStats?.Invoke(msg[2..]);
        else if (msg.StartsWith("GE") && msg.Length > 2 && char.IsDigit(msg[2])) FightEnded?.Invoke(msg[2..]);
        else if (msg.StartsWith("PM") && msg.Length > 2) PartyChanged?.Invoke(msg[2..]);
        else if (msg == "PV" || msg.StartsWith("PV|")) PartyChanged?.Invoke(null);
        else if (msg.StartsWith("AN") && int.TryParse(msg.AsSpan(2), out var level)) LevelUp?.Invoke(level);
        // Économe : sorts du personnage, entrée en combat, sorts lancés
        else if (msg.StartsWith("SL") && msg.Length > 2 && char.IsDigit(msg[2])) SpellList?.Invoke(msg[2..]);
        else if (msg.StartsWith("GJK")) FightJoined?.Invoke();
        else if (msg.StartsWith("GTS") && msg[3..].Split('|') is var turn && long.TryParse(turn[0], out var fighter))
            TurnStarted?.Invoke(fighter, turn.Length > 1 && int.TryParse(turn[1], out var turnMs) ? turnMs : 0);
        else if (msg.StartsWith("ILS") && int.TryParse(msg.AsSpan(3), out var regen)) LifeRegen?.Invoke(regen);
        else if (msg.StartsWith("GA") && (msg.Contains(';') || msg.StartsWith("GA303"))) GameAction?.Invoke(msg[2..]);
        else if (msg.StartsWith("GTM|")) FightersUpdate?.Invoke(msg[4..]);
        else if (msg.StartsWith("Gd") && msg.Length > 2 && (char.IsDigit(msg[2]) || msg[2] is 'O' or 'K')) Challenge?.Invoke(msg[2..]);
        // Switcheur : notifications d'échange et d'invitation de groupe
        else if (msg.StartsWith("ERK") && msg[3..].Split('|') is [_, var to, ..] && long.TryParse(to, out var target))
            ExchangeRequested?.Invoke(target);
        else if (msg.StartsWith("PIK") && msg[3..].Split('|') is [_, var invited, ..]) PartyInvited?.Invoke(invited);
        // Classeur TTG : contenu à l'ouverture (« td »), carte ajoutée (« ta »)
        else if (msg.StartsWith("td") && msg.Length > 2 && char.IsAsciiDigit(msg[2])) OnBinder(msg[2..]);
        else if (msg.StartsWith("ta") && int.TryParse(msg.AsSpan(2), out var added)) OnBinderAdd(added);
    }

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
        Cur.Inventory[uid] = item;
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
        if (!Cur.Inventory.Remove(uid, out var old)) return;
        if (InCraft) CraftItemConsumed?.Invoke(uid, old.TemplateId, old.Quantity);
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

    // --- Persistance : la dernière banque connue est conservée entre deux lancements ---

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    static string SnapshotPath(string character) =>
        Path.Combine(BanksDir, string.Concat((character.Length > 0 ? character : "_inconnu").Split(Path.GetInvalidFileNameChars())) + ".json");

    void SaveSnapshot(Snapshot s)
    {
        try
        {
            Directory.CreateDirectory(BanksDir);
            File.WriteAllText(SnapshotPath(s.Character), JsonSerializer.Serialize(s, JsonOpts));
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

    readonly Dictionary<string, (DateTime Time, Snapshot? Snapshot)> _savedCache = new();

    /// <summary>
    /// Banques et inventaires de tous les personnages : celui suivi (état en cours), ceux des autres fenêtres de jeu
    /// ouvertes (état en cours), puis les autres, tels que sauvegardés dans <see cref="BanksDir"/>.
    /// Les fichiers ne sont relus que s'ils ont changé.
    /// </summary>
    public List<Snapshot> AllSnapshots()
    {
        var list = new List<Snapshot> { State };
        foreach (var s in _byStream.Values.Distinct())
            if (s.Character.Length > 0 && list.All(x => x.Character != s.Character)) list.Add(s);
        if (!Directory.Exists(BanksDir)) return list;
        foreach (var f in new DirectoryInfo(BanksDir).GetFiles("*.json"))
        {
            if (!_savedCache.TryGetValue(f.FullName, out var c) || c.Time != f.LastWriteTimeUtc)
            {
                Snapshot? s = null;
                try { s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(f.FullName)); } catch { }
                _savedCache[f.FullName] = c = (f.LastWriteTimeUtc, s);
            }
            if (c.Snapshot is { Character.Length: > 0 } saved && list.All(x => x.Character != saved.Character)) list.Add(saved);
        }
        return list;
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
