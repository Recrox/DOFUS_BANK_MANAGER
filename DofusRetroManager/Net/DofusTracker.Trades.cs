using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DofusRetroManager.Net;

public sealed partial class DofusTracker
{
    // --- Achats et ventes du personnage (onglet HDV, « Mes échanges ») : objets, quantités et prix seulement ---

    /// <summary>Échange ouvert ou fermé (-1) sur la connexion : (personnage, type, paramètres « ECK10|… »).</summary>
    public event Action<string, int, string>? ExchangeChanged;
    /// <summary>Vos offres en cours, listées à l'ouverture du mode vente de l'HDV (message EL sans le préfixe).</summary>
    public event Action<string, string>? HdvSellList;
    /// <summary>Offre mise en vente ou retirée en mode vente de l'HDV (message EmK sans le préfixe : « +id|qté|objet|effets|prix|heures », « -id »).</summary>
    public event Action<string, string>? HdvSellMove;
    /// <summary>
    /// Vente à l'HDV annoncée par le jeu (Mes échanges) : personnage, code (65 vendue, 73 vendue hors jeu, 67 expirée) et
    /// paramètres (kamas, objet, quantité). Ces messages ne contiennent aucun nom de joueur.
    /// </summary>
    public event Action<string, int, string[]>? HdvSaleInfo;
    /// <summary>Quantité d'un objet gagnée (positive) ou perdue (négative) dans l'inventaire : (personnage, objet, quantité).</summary>
    public event Action<string, int, int>? InventoryDelta;

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
    /// <summary>
    /// Le client de la fenêtre joue son tour sans lancer de sort : déplacement (« GA001… », envoyé par le client) ou fin de
    /// tour (« Gt »). Seul le fait est lu, pas le chemin : il dit quelle fenêtre joue (Switcheur, temps du tour).
    /// </summary>
    public event Action? OwnTurnAction;
    /// <summary>Demande d'échange (message ERK) : identifiant du personnage à qui elle est adressée.</summary>
    public event Action<long>? ExchangeRequested;
    /// <summary>Invitation de groupe (message PIK) : nom du personnage invité (celui de l'invitant n'est pas lu).</summary>
    public event Action<string>? PartyInvited;

    /// <summary>Codes des messages de forgemagie (fichier lang : INFOS_117, INFOS_183, INFOS_194).</summary>
    public static readonly int[] FmInfoCodes = [117, 183, 194];
    /// <summary>Codes des messages de vente à l'HDV (INFOS_65 vendue, INFOS_73 vendue hors jeu, INFOS_67 vente expirée).</summary>
    public static readonly int[] HdvSaleCodes = [65, 67, 73];

    /// <summary>Messages de vente à l'HDV d'un message « Im » : code et paramètres (séparés par '~').</summary>
    static List<(int Code, string[] Args)> SaleInfos(string msg)
    {
        var list = new List<(int, string[])>();
        foreach (var part in msg[2..].Split('|'))
        {
            var semi = part.IndexOf(';');
            var head = semi < 0 ? part : part[..semi];
            if (head.Length >= 2 && int.TryParse(head.AsSpan(1), out var code) && HdvSaleCodes.Contains(code))
                list.Add((code, semi < 0 ? [] : part[(semi + 1)..].Split('~')));
        }
        return list;
    }
}
