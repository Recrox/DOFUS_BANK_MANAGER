using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DofusRetroManager.Net;

public sealed partial class DofusTracker
{
    // --- Multi-compte : chaque fenêtre de jeu (connexion) a son personnage, sa banque, son inventaire et son échange ---

    /// <summary>Personnage de chaque connexion, appris par ASK (même objet que State quand c'est le personnage suivi).</summary>
    readonly Dictionary<string, Snapshot> _byStream = new();
    readonly Dictionary<string, int> _exchanges = new();
    string _stream = "";

    /// <summary>
    /// Connexions dont le personnage n'est pas encore reconnu (application lancée après le jeu) : leurs changements sont
    /// mis de côté, jamais écrits dans un autre personnage, puis repris quand la fenêtre est reconnue.
    /// </summary>
    readonly Dictionary<string, (Snapshot Snap, HashSet<long> Removed)> _pending = new();

    /// <summary>
    /// Personnage de la connexion du message en cours : c'est lui que modifient les messages de banque et d'inventaire.
    /// Connexion inconnue : un personnage provisoire, sans nom, qui n'est pas enregistré.
    /// </summary>
    Snapshot Cur
    {
        get
        {
            if (_byStream.TryGetValue(_stream, out var s)) return s;
            if (!_pending.TryGetValue(_stream, out var p)) _pending[_stream] = p = (new Snapshot(), new HashSet<long>());
            return p.Snap;
        }
    }

    /// <summary>Connexion dont des objets ont été vus sans savoir à quel personnage ils appartiennent.</summary>
    public bool NeedsName(string stream) => !_byStream.ContainsKey(stream) && _pending.ContainsKey(stream);

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
        var snap = State.Character == name ? State
            : _byStream.Values.FirstOrDefault(s => s.Character == name) ?? LoadSnapshot(name) ?? new Snapshot { Character = name };
        _byStream[stream] = snap;
        if (!_pending.Remove(stream, out var p)) return;
        // Ce qui a été vu avant la reconnaissance de la fenêtre revient à son personnage
        foreach (var uid in p.Removed) snap.Inventory.Remove(uid);
        foreach (var (uid, item) in p.Snap.Inventory) snap.Inventory[uid] = item;
        if (p.Snap.BankUpdatedAt != default)
        {
            snap.Bank = p.Snap.Bank;
            snap.BankKamas = p.Snap.BankKamas;
            snap.BankUpdatedAt = p.Snap.BankUpdatedAt;
        }
        if (p.Snap.BinderUpdatedAt != default)
        {
            snap.Binder = p.Snap.Binder;
            snap.BinderUpdatedAt = p.Snap.BinderUpdatedAt;
        }
        SaveSnapshot(snap);
        if (State == p.Snap) State = snap;
        BankChanged?.Invoke();
        InventoryChanged?.Invoke();
    }

    /// <summary>Connexion fermée : son personnage n'est plus en jeu (il reste dans les banques sauvegardées).</summary>
    public void OnStreamClosed(string stream)
    {
        _byStream.Remove(stream);
        _pending.Remove(stream);
        _exchanges.Remove(stream);
    }
}
