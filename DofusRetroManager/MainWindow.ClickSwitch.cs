using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DofusRetroManager.Multi;

namespace DofusRetroManager;

/// <summary>
/// Switcheur, « touche + clic » : quand le joueur clique dans une fenêtre du jeu en gardant une touche enfoncée (Ctrl par
/// défaut), la fenêtre suivante de l'ordre de jeu passe au premier plan au relâchement du clic, comme avec le raccourci
/// « Personnage suivant ». Le clic du joueur arrive au jeu normalement : il n'est ni bloqué, ni modifié, ni rejoué.
/// Pas de hook : l'application relit seulement l'état du bouton de la souris et de la touche (GetAsyncKeyState).
/// Aucun clic ni aucune touche n'est envoyé au jeu (README, règles 2 et 3).
/// </summary>
public partial class MainWindow
{
    /// <summary>Touches proposées : nom affiché et code de touche virtuelle Windows.</summary>
    static readonly (string Name, int Vk)[] ClickSwitchKeys = [("Ctrl", 0x11), ("Alt", 0x12), ("Maj", 0x10)];

    readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(10) };
    bool _clickDown, _clickArmed;

    void InitClickSwitch()
    {
        _organizerLoading = true;
        OrganizerClickSwitch.IsChecked = _organizer.ClickSwitch;
        OrganizerClickKey.ItemsSource = ClickSwitchKeys.Select(k => k.Name).ToList();
        OrganizerClickKey.SelectedIndex = Math.Max(0, Array.FindIndex(ClickSwitchKeys, k => k.Name == _organizer.ClickSwitchKey));
        _organizerLoading = false;
        _clickTimer.Tick += (_, _) => PollClick();
        UpdateClickSwitch();
    }

    /// <summary>La surveillance ne tourne que si l'option et le Switcheur sont activés.</summary>
    void UpdateClickSwitch()
    {
        bool on = _organizer.Enabled && _organizer.ClickSwitch;
        if (on && !_clickTimer.IsEnabled) { _clickDown = (GetAsyncKeyState(MouseButton) & 0x8000) != 0; _clickTimer.Start(); }
        else if (!on) _clickTimer.Stop();
    }

    void OrganizerClickKey_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_organizerLoading || OrganizerClickKey.SelectedIndex < 0) return;
        _organizer.ClickSwitchKey = ClickSwitchKeys[OrganizerClickKey.SelectedIndex].Name;
        _organizer.Save();
    }

    /// <summary>Bouton gauche (le droit si les boutons sont inversés dans Windows : GetAsyncKeyState lit le bouton physique).</summary>
    static int MouseButton => GetSystemMetrics(SM_SWAPBUTTON) != 0 ? 0x02 : 0x01;

    void PollClick()
    {
        bool down = (GetAsyncKeyState(MouseButton) & 0x8000) != 0;
        if (down == _clickDown) return;
        _clickDown = down;
        if (down)
        {
            // Clic avec la touche, dans une fenêtre du jeu de la rotation
            int vk = ClickSwitchKeys.FirstOrDefault(k => k.Name == _organizer.ClickSwitchKey, ClickSwitchKeys[0]).Vk;
            var fg = GameWindows.Foreground;
            _clickArmed = (GetAsyncKeyState(vk) & 0x8000) != 0 && Rotation().Any(w => w.Handle == fg);
            return;
        }
        if (!_clickArmed) return;
        _clickArmed = false;
        // Clic relâché : le jeu l'a reçu ; la fenêtre suivante passe au premier plan juste après
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Debounced(() => Cycle(1)));
    }

    const int SM_SWAPBUTTON = 23;
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
}
