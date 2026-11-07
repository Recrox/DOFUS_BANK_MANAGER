using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Interop;
using DofusRetroManager.Data;
using DofusRetroManager.Hunt;

namespace DofusRetroManager.Multi;

/// <summary>Réglages de l'onglet Switcheur (organizer.json).</summary>
public sealed class OrganizerSettings
{
    /// <summary>Switcheur actif : raccourcis, notifications et bascules automatiques. Désactivé, l'onglet ne fait que lister.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Anti-rebond des raccourcis : une nouvelle pression plus rapprochée que ce délai (ms) est ignorée.</summary>
    public int HotkeyDebounceMs { get; set; } = 160;
    /// <summary>Début du tour d'un de vos personnages : sa fenêtre passe au premier plan.</summary>
    public bool AutoSwitchTurn { get; set; } = true;
    /// <summary>Mode split (fenêtres côte à côte) : au début du tour, pas de bascule ; la ligne du personnage clignote dans la superposition, avec le temps du tour.</summary>
    public bool SplitMode { get; set; }
    /// <summary>Une nouvelle fenêtre du jeu est agrandie (plein écran fenêtré) dès qu'elle apparaît.</summary>
    public bool MaximizeOnStart { get; set; }
    /// <summary>Entrée en groupe : l'ordre de jeu actuel est enregistré (et complété pour les nouveaux personnages), puis ne bouge plus.</summary>
    public bool LockOrderOnParty { get; set; } = true;
    /// <summary>Personnages exclus de la rotation (ex. compte métier), par nom.</summary>
    public List<string> Excluded { get; set; } = new();
    public string NextKey { get; set; } = "Ctrl+Alt+Right";
    public string PreviousKey { get; set; } = "Ctrl+Alt+Left";
    /// <summary>Ordre de jeu choisi par glisser-déposer (noms) ; vide : par initiative.</summary>
    public List<string> Order { get; set; } = new();
    /// <summary>Personnage principal (étoile) : un seul, affiché par son propre raccourci.</summary>
    public string? MainCharacter { get; set; }
    public string MainKey { get; set; } = "Ctrl+Alt+Up";
    /// <summary>
    /// Mode Sadida : ce raccourci affiche le Sadida en arbre (Puissance Sylvestre) dont le tour se termine le plus tôt ;
    /// le joueur passe lui-même son tour, puis la fenêtre d'avant revient (SadidaReturn).
    /// </summary>
    public string SadidaKey { get; set; } = "F6";
    public bool SadidaReturn { get; set; } = true;
    /// <summary>Touche + clic dans le jeu : au relâchement du clic, la fenêtre suivante de l'ordre de jeu passe au premier plan.</summary>
    public bool ClickSwitch { get; set; }
    /// <summary>Touche à garder enfoncée pendant le clic : Ctrl, Alt ou Maj.</summary>
    public string ClickSwitchKey { get; set; } = "Alt";
    /// <summary>Boutons qui déclenchent le clic + touche : gauche, droit, ou les deux.</summary>
    public bool ClickSwitchLeft { get; set; } = true;
    public bool ClickSwitchRight { get; set; }
    /// <summary>
    /// Anti-rebond de fenêtre : si Windows rend aussitôt le premier plan à la fenêtre quittée, la fenêtre demandée est
    /// réaffichée (gestion de fenêtre seulement : aucun clic ni aucune touche n'est envoyé au jeu).
    /// </summary>
    public bool FixWindowBounce { get; set; } = true;
    public bool NotifyFight { get; set; } = true;
    public bool NotifyTurn { get; set; } = true;
    public bool NotifyExchange { get; set; } = true;
    public bool NotifyParty { get; set; } = true;
    public bool ShowToast { get; set; } = true;
    public bool FlashTaskbar { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    /// <summary>Invitation de groupe ou demande d'échange : la fenêtre du personnage concerné passe au premier plan.</summary>
    public bool AutoSwitch { get; set; } = true;

    /// <summary>Superposition en jeu : activée, blocs affichés, opacité et position (null : en haut à droite).</summary>
    public bool OverlayEnabled { get; set; }
    public bool OverlayEconome { get; set; } = true;
    public bool OverlayGroups { get; set; } = true;
    public bool OverlayHarvest { get; set; } = true;
    public bool OverlayRotation { get; set; } = true;
    public double OverlayOpacity { get; set; } = 0.92;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }

    static string Path => System.IO.Path.Combine(GameData.AppDir, "organizer.json");

    public static OrganizerSettings Load()
    {
        try
        {
            if (File.Exists(Path) && JsonSerializer.Deserialize<OrganizerSettings>(File.ReadAllText(Path)) is { } s)
            {
                // Ancien raccourci Sadida par défaut (1.49) : remplacé par la nouvelle touche par défaut, F6
                if (s.SadidaKey == "Ctrl+Alt+Down") s.SadidaKey = "F6";
                return s;
            }
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(GameData.AppDir);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public bool Notify(GameNotice notice) => notice switch
    {
        GameNotice.Fight => NotifyFight,
        GameNotice.Turn => NotifyTurn,
        GameNotice.Exchange => NotifyExchange,
        _ => NotifyParty,
    };
}

/// <summary>
/// Raccourcis clavier globaux (RegisterHotKey) : ils marchent même quand le jeu a le focus. La touche est prise par
/// l'application et n'est pas transmise au jeu ; elle ne fait que changer de fenêtre (README, règle 3).
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    readonly HwndSource _source;
    readonly Dictionary<int, Action> _actions = new();
    int _nextId = 1;

    public GlobalHotkeys(HwndSource source)
    {
        _source = source;
        _source.AddHook(Hook);
    }

    /// <summary>Enregistre un raccourci (« Ctrl+Alt+Right ») ; faux s'il est invalide ou déjà pris par une autre application.</summary>
    public bool Register(string gesture, Action action)
    {
        if (!TryParse(gesture, out var mods, out var key)) return false;
        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, mods | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key))) return false;
        _actions[id] = action;
        return true;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(Hook);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Texte d'un raccourci saisi au clavier : « Ctrl+Alt+Right » ; vide pour une touche de modification seule.</summary>
    public static string Format(ModifierKeys mods, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None) return "";
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    static bool TryParse(string gesture, out uint mods, out Key key)
    {
        mods = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        foreach (var part in gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": mods |= MOD_WIN; break;
                default: if (!Enum.TryParse(part, true, out key)) return false; break;
            }
        }
        return key != Key.None;
    }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
