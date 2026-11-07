using System.IO;
using System.Text.Json;
using DofusRetroManager.Data;

namespace DofusRetroManager.Hunt;

public sealed partial class HuntTracker
{
    // --- Mémoire des personnages : l'application relancée pendant que le jeu est ouvert les retrouve sans reconnexion ---

    DateTime _knownSaved;
    bool _knownDirty;

    /// <summary>Dernier état connu d'un personnage, par son nom (celui du titre de sa fenêtre).</summary>
    public KnownCharacter? KnownOf(string name) => name.Length == 0 ? null
        : _store.Known.GetValueOrDefault(name) ?? _store.Known.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Retient l'état du personnage ; le fichier est réécrit au plus toutes les 30 s (et à la fermeture).</summary>
    void Remember(HuntCharacter c)
    {
        if (c.Name.Length == 0) return;
        if (!_store.Known.TryGetValue(c.Name, out var k)) _store.Known[c.Name] = k = new KnownCharacter();
        if (c.Id != 0) k.Id = c.Id;
        k.Level = c.Level ?? k.Level;
        k.Initiative = c.Initiative ?? k.Initiative;
        k.Wisdom = c.Wisdom ?? k.Wisdom;
        k.Prospection = c.Prospection ?? k.Prospection;
        if (c.Life != null && !c.InFight)
        {
            k.Life = c.Life;
            k.MaxLife = c.MaxLife;
            k.LifeAt = c.LifeAt;
            k.RegenMs = c.RegenMs;
        }
        _knownDirty = true;
        if (DateTime.Now - _knownSaved > TimeSpan.FromSeconds(30)) SaveIfDirty();
    }

    /// <summary>Écrit ce qui reste à enregistrer (appelé à la fermeture de l'application).</summary>
    public void SaveIfDirty()
    {
        if (!_knownDirty) return;
        _knownDirty = false;
        _knownSaved = DateTime.Now;
        Save();
    }

    /// <summary>Connexion dont le personnage n'est pas encore connu (application lancée après la connexion au jeu).</summary>
    public bool NeedsName(string stream) => _characters.TryGetValue(stream, out var c) && c.Name.Length == 0;

    /// <summary>
    /// Personnage de la connexion retrouvé sans le voir à la sélection (sa fenêtre, ou son identifiant) : son dernier état
    /// connu est repris (identifiant, niveau, initiative, vie…), puis remplacé par ce que le jeu envoie ensuite.
    /// </summary>
    public bool Identify(string stream, string name)
    {
        if (!_characters.TryGetValue(stream, out var c) || c.Name.Length > 0) return false;
        // Le personnage est déjà suivi sur une autre connexion encore active : pas de doublon
        if (_characters.Values.Any(x => x != c && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return false;
        c.Name = name;
        if (KnownOf(name) is { } k)
        {
            if (c.Id == 0) c.Id = k.Id;
            c.Level ??= k.Level;
            c.Initiative ??= k.Initiative;
            c.Wisdom ??= k.Wisdom;
            c.Prospection ??= k.Prospection;
            if (c.Life == null && k.Life != null)
            {
                c.Life = k.Life;
                c.MaxLife = k.MaxLife;
                c.LifeAt = k.LifeAt;
                c.RegenMs = k.RegenMs;
            }
        }
        Log?.Invoke($"Fenêtre reconnue : {name}.");
        Identified?.Invoke(stream, name);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Identifiant appris en jeu (action, fin de combat) : s'il est celui d'un de vos personnages connus, il est reconnu.</summary>
    void IdentifyById(HuntCharacter c)
    {
        if (c.Name.Length > 0 || c.Id == 0) return;
        var known = _store.Known.FirstOrDefault(kv => kv.Value.Id == c.Id).Key;
        if (known != null) Identify(c.Stream, known);
    }

    public void OnCharacterSelected(long id, string name, int level, CharacterLook? look)
    {
        var c = Current;
        c.Id = id;
        c.Name = name;
        if (level > 0) c.Level = level;
        if (look != null && !look.Equals(_store.Looks.GetValueOrDefault(name)))
        {
            _store.Looks[name] = look;
            Save();
        }
        c.Party.Clear();
        SaveSpells(c);
        // Reconnexion : le même personnage sur une ancienne connexion (fermeture non vue) est retiré
        foreach (var old in _characters.Where(kv => kv.Value != c && kv.Value.Id == id).Select(kv => kv.Key).ToList())
        {
            _characters.Remove(old);
            if (Pinned == old) Pinned = c.Stream;
        }
        Remember(c);
        Changed?.Invoke();
    }

    public void OnLevelUp(int level)
    {
        Current.Level = level;
        Remember(Current);
        Changed?.Invoke();
    }

    public void OnMapChanged(string mapId)
    {
        var c = Current;
        c.MapId = mapId;
        c.Groups.Clear();
        c.LastMove = DateTime.Now;
        Changed?.Invoke();
    }
}
