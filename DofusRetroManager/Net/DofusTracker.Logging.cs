using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DofusRetroManager.Net;

public sealed partial class DofusTracker
{
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
}
