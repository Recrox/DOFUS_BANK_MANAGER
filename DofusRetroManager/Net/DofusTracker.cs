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
///   GA001… / Gt                   envoyés par le client : déplacement, fin de tour (seul le fait est lu : qui joue)
///   GTS{id}|…                     début du tour d'un combattant (multi-compte : affiche le personnage qui joue)
///   GTM|id;0;pv;PA;PM;case;;pvmax|…   état des combattants : seuls les points de vie du personnage sont lus
///   Gd{id};… / GdO{id} / GdK{id}  challenge du combat annoncé, réussi, raté (onglet Combat) : seul son identifiant est lu
///
/// Onglet Switcheur (notifications ; lus, jamais journalisés ni conservés) :
///   ERK{de}|{à}|{type}            demande d'échange : seul l'identifiant du destinataire est lu
///   PIK{invitant}|{invité}        invitation de groupe : seul le nom de l'invité est lu (comparé au personnage de la
///                                 fenêtre), celui de l'invitant est ignoré
/// </summary>
public sealed partial class DofusTracker
{
    const int BankExchangeType = 5;
    const int CraftExchangeType = 3;
    /// <summary>Boutique d'un joueur en mode marchand.</summary>
    const int ShopExchangeType = 4;
    /// <summary>Hôtel de vente : mode vente (vos offres), mode achat.</summary>
    const int HdvSellExchangeType = 10, HdvBuyExchangeType = 11;
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

    public static string BanksDir => Path.Combine(Data.GameData.AppDir, "banks");

    void Handle(string msg)
    {
        if (msg.StartsWith("ASK|")) OnCharacter(msg);
        else if (msg.StartsWith("ECK")) OnExchangeCreate(msg);
        else if (msg == "EV" || msg.StartsWith("EVa")) { _exchangeType = -1; ExchangeChanged?.Invoke(Cur.Character, -1, ""); }
        else if (msg.StartsWith("EL") && _exchangeType == HdvSellExchangeType) HdvSellList?.Invoke(Cur.Character, msg[2..]);
        else if (msg.StartsWith("EmK") && _exchangeType == HdvSellExchangeType) HdvSellMove?.Invoke(Cur.Character, msg[3..]);
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
        else if (msg.StartsWith("Im") && SaleInfos(msg) is { Count: > 0 } sales)
            foreach (var (code, args) in sales) HdvSaleInfo?.Invoke(Cur.Character, code, args);
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
        else if (msg.StartsWith("GA001") || msg == "Gt") OwnTurnAction?.Invoke();
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
}
