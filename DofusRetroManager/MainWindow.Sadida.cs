using DofusRetroManager.Hunt;
using DofusRetroManager.Multi;

namespace DofusRetroManager;

/// <summary>
/// Onglet Switcheur, mode Sadida : un raccourci affiche le Sadida transformé en arbre (Puissance Sylvestre, lue dans ses
/// sorts lancés) dont c'est le tour et qui a le moins de temps restant ; le joueur passe lui-même son tour, puis, à la fin
/// de ce tour (annoncée par le jeu), la fenêtre affichée avant le raccourci revient. Ce n'est que de la gestion de
/// fenêtre : rien n'est envoyé au jeu, ni touche ni clic (REGLES.md, règles 1 et 3).
/// </summary>
public partial class MainWindow
{
    /// <summary>Bascule en cours : Sadida affiché, sa fenêtre, fenêtre à réafficher ensuite, heure du raccourci.</summary>
    (HuntCharacter Character, IntPtr Target, IntPtr Previous, DateTime Time)? _sadidaJump;

    void InitSadida() => _hunt!.TurnEnded += c => Safe(() => OnSadidaTurnEnded(c));

    void SadidaJump()
    {
        if (_hunt == null) return;
        _windows = GameWindows.Find();
        var foreground = GameWindows.Foreground;
        // Nouvel appui sur la fenêtre du Sadida affiché : retour immédiat à la fenêtre d'avant
        if (_sadidaJump is { } jump && foreground == jump.Target && DateTime.Now - jump.Time < TimeSpan.FromMinutes(2))
        {
            _sadidaJump = null;
            if (_windows.Any(w => w.Handle == jump.Previous)) FocusWindow(jump.Previous);
            RefreshWindows();
            return;
        }
        // Sadida en arbre dont c'est le tour, celui dont le tour se termine le plus tôt ; aucun : rien ne se passe
        var now = DateTime.Now;
        var best = _hunt.Characters
            .Where(c => c.IsTree && c.TurnStart != null)
            .Select(c => (Character: c, Window: WindowOf(c), Left: c.TurnStart!.Value.AddMilliseconds(c.TurnMs > 0 ? c.TurnMs : 30_000) - now))
            .Where(x => x.Window != null)
            .OrderBy(x => x.Left)
            .FirstOrDefault();
        if (best.Window is not { } target) return;
        _sadidaJump = (best.Character, target, target == foreground ? IntPtr.Zero : foreground, now);
        if (target != foreground) FocusWindow(target);
        RefreshWindows();
    }

    /// <summary>Le joueur a passé le tour du Sadida affiché par le raccourci : la fenêtre d'avant revient au premier plan.</summary>
    void OnSadidaTurnEnded(HuntCharacter c)
    {
        if (_sadidaJump is not { } jump || jump.Character != c) return;
        _sadidaJump = null;
        if (!_organizer.Enabled || !_organizer.SadidaReturn || jump.Previous == IntPtr.Zero) return;
        if (DateTime.Now - jump.Time > TimeSpan.FromMinutes(2)) return;
        // Le joueur a déjà changé de fenêtre (ou une bascule de début de tour l'a fait) : on ne la lui reprend pas
        if (GameWindows.Foreground != jump.Target) return;
        if (!GameWindows.Find().Any(w => w.Handle == jump.Previous)) return;
        FocusWindow(jump.Previous);
        RefreshWindows();
    }
}
