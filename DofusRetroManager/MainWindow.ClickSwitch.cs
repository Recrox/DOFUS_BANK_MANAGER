using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DofusRetroManager.Multi;

namespace DofusRetroManager;

/// <summary>
/// Switcheur, « touche + clic » : quand le joueur clique dans une fenêtre du jeu en gardant une touche enfoncée (Ctrl par
/// défaut ; bouton gauche, droit ou les deux), la fenêtre suivante de l'ordre de jeu passe au premier plan au relâchement du clic, comme avec le raccourci
/// « Personnage suivant ». Le clic du joueur arrive au jeu normalement : il n'est ni bloqué, ni modifié, ni rejoué.
/// Pas de hook : l'application relit seulement l'état du bouton de la souris et de la touche (GetAsyncKeyState).
/// Aucun clic ni aucune touche n'est envoyé au jeu (REGLES.md, règles 2 et 3).
/// </summary>
public partial class MainWindow
{
    /// <summary>Touches proposées : nom affiché et code de touche virtuelle Windows.</summary>
    static readonly (string Name, int Vk)[] ClickSwitchKeys = [("Ctrl", 0x11), ("Alt", 0x12), ("Maj", 0x10)];

    readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(10) };
    /// <summary>État de chaque bouton (gauche, droit) au dernier passage, et clic « armé » (touche enfoncée dans le jeu).</summary>
    readonly bool[] _clickDown = new bool[2], _clickArmed = new bool[2];

    void InitClickSwitch()
    {
        _organizerLoading = true;
        OrganizerClickSwitch.IsChecked = _organizer.ClickSwitch;
        OrganizerClickLeft.IsChecked = _organizer.ClickSwitchLeft;
        OrganizerClickRight.IsChecked = _organizer.ClickSwitchRight;
        OrganizerFixBounce.IsChecked = _organizer.FixWindowBounce;
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
        if (on && !_clickTimer.IsEnabled)
        {
            for (int b = 0; b < 2; b++) _clickDown[b] = (GetAsyncKeyState(MouseButton(b)) & 0x8000) != 0;
            _clickTimer.Start();
        }
        else if (!on) _clickTimer.Stop();
    }

    void OrganizerClickKey_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_organizerLoading || OrganizerClickKey.SelectedIndex < 0) return;
        _organizer.ClickSwitchKey = ClickSwitchKeys[OrganizerClickKey.SelectedIndex].Name;
        _organizer.Save();
    }

    /// <summary>
    /// Bouton gauche (0) ou droit (1) tel que le voit le joueur : GetAsyncKeyState lit le bouton physique, inversé si les
    /// boutons le sont dans Windows.
    /// </summary>
    static int MouseButton(int button) => (button == 0) == (GetSystemMetrics(SM_SWAPBUTTON) == 0) ? 0x01 : 0x02;

    void PollClick()
    {
        for (int b = 0; b < 2; b++) PollButton(b, b == 0 ? _organizer.ClickSwitchLeft : _organizer.ClickSwitchRight);
    }

    void PollButton(int b, bool enabled)
    {
        bool down = (GetAsyncKeyState(MouseButton(b)) & 0x8000) != 0;
        if (down == _clickDown[b]) return;
        _clickDown[b] = down;
        if (down)
        {
            // Clic avec la touche, dans une fenêtre du jeu de la rotation
            int vk = ClickSwitchKeys.FirstOrDefault(k => k.Name == _organizer.ClickSwitchKey, ClickSwitchKeys[0]).Vk;
            var fg = GameWindows.Foreground;
            _clickArmed[b] = enabled && (GetAsyncKeyState(vk) & 0x8000) != 0 && Rotation().Any(w => w.Handle == fg);
            return;
        }
        if (!_clickArmed[b]) return;
        _clickArmed[b] = false;
        // Clic relâché : le jeu l'a reçu ; la fenêtre suivante passe au premier plan juste après
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Debounced(() => Cycle(1)));
    }

    const int SM_SWAPBUTTON = 23;
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
}
