using DofusRetroManager.Multi;
using DofusRetroManager.Net;

namespace DofusRetroManager;

/// <summary>
/// Application lancée (ou mise à jour) pendant que le jeu est ouvert : le jeu n'envoie le personnage qu'à sa sélection.
/// Chaque connexion est rattachée à sa fenêtre grâce à la table des connexions de Windows (processus propriétaire,
/// comme netstat -o) et à la liste des processus : la connexion est ouverte par un processus enfant de celui de la
/// fenêtre, dont le titre donne le nom du personnage. Son dernier état connu (chasse.json) est alors repris.
/// Rien n'est lu dans la mémoire du jeu ni envoyé au jeu (README, règles 1 et 2).
/// </summary>
public partial class MainWindow
{
    /// <summary>Dernière tentative par connexion : la table n'est relue qu'une fois toutes les 10 s pour une même connexion.</summary>
    readonly Dictionary<string, DateTime> _identifyTries = new();

    void InitReconnect()
    {
        _hunt!.Identified += (stream, name) => _tracker.AssignStream(stream, name);
        Closed += (_, _) => _hunt?.SaveIfDirty();
    }

    /// <summary>Après chaque message : connexion dont le personnage est inconnu, on cherche sa fenêtre.</summary>
    void TryIdentify(string stream)
    {
        if (_hunt == null || !_hunt.NeedsName(stream)) return;
        var now = DateTime.Now;
        if (_identifyTries.TryGetValue(stream, out var last) && now - last < TimeSpan.FromSeconds(10)) return;
        _identifyTries[stream] = now;
        try
        {
            if (WindowNameOf(stream) is { } name) _hunt.Identify(stream, name);
        }
        catch (Exception ex) { AddLog($"Fenêtre de la connexion introuvable : {ex.Message}"); }
    }

    /// <summary>
    /// Nom du personnage de la fenêtre qui a ouvert cette connexion (« serveur:port > local:port », reçue du serveur),
    /// ou null si elle est introuvable ou ambiguë.
    /// </summary>
    static string? WindowNameOf(string stream)
    {
        var sides = stream.Split(" > ");
        if (sides.Length != 2 || !int.TryParse(sides[1][(sides[1].LastIndexOf(':') + 1)..], out var localPort)) return null;
        if (!DofusConnections.LocalPortOwners().TryGetValue(localPort, out var pid)) return null;
        var windows = GameWindows.Find().Where(w => w.Name.Length > 0)
            .GroupBy(w => GameWindows.ProcessOf(w.Handle)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().Name);
        if (windows.Count == 0) return null;
        var parents = DofusConnections.ParentProcesses();
        // Processus de la connexion, puis ses parents : le premier qui porte une fenêtre de jeu
        for (int depth = 0; depth < 8; depth++)
        {
            if (windows.TryGetValue(pid, out var name)) return name;
            if (!parents.TryGetValue(pid, out var parent) || parent == pid || parent == 0) break;
            pid = parent;
        }
        return null;
    }
}
