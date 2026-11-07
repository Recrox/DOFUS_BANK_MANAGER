using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DofusRetroManager.Data;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DofusRetroManager;

/// <summary>
/// Interface HTML/CSS (WebView2). Elle ne recalcule rien : elle affiche l'état des contrôles WPF (tableaux, textes,
/// filtres), qui restent la seule source de vérité, et leur renvoie les actions (saisie, sélection, clic).
/// La page est servie depuis l'exe sous https://app.local/ : aucune requête ne sort de la machine (REGLES.md, règle 4).
/// </summary>
public partial class MainWindow
{
    const string WebOrigin = "https://app.local/";

    WebView2? _web;
    readonly DispatcherTimer _webTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    /// <summary>Dernier état envoyé : compteur de changements des contrôles, heure du dernier envoi complet.</summary>
    long _webVersion = -1;
    DateTime _webFullAt;
    /// <summary>Envoi complet demandé (thème changé, liste suivie changée) même si aucun contrôle n'a bougé.</summary>
    bool _webForce;
    /// <summary>Contrôles WPF suivis par la page (noms x:Name), envoyés quand leur état change.</summary>
    HashSet<string> _webWatched = new();
    readonly Dictionary<string, string> _webSent = new();
    /// <summary>Lignes déjà converties en JSON, par contrôle : une liste n'est reconvertie que si elle a été remplacée.</summary>
    readonly Dictionary<string, (object Source, string Json)> _webRows = new();
    readonly ConcurrentDictionary<string, byte[]?> _webIcons = new(); // rempli par le fil de rendu des images
    /// <summary>Gestionnaires de clic sur une ligne que la page peut déclencher (le reste passe par les boutons nommés).</summary>
    static readonly HashSet<string> WebRowHandlers =
        ["HuntDropsToggle_Click", "HuntAlertToggle_Click", "HuntAlertDelete_Click", "BankRecipes_Click", "OrganizerShow_Click", "OrganizerExclude_Click", "OrganizerMain_Click", "FmWeightReset_Click"];

    async Task ShowWebUiAsync()
    {
        try
        {
            // Le contrôle WebView2 ne s'initialise qu'une fois affiché : le conteneur est visible dès le départ
            if (_web == null)
            {
                var dataDir = System.IO.Path.Combine(GameData.AppDir, "WebView2");
#if DEBUG
                // Tests : un dossier à part, pour tourner à côté de l'application installée déjà ouverte
                if (Environment.GetEnvironmentVariable("DRM_WEBVIEW2_DIR") is { Length: > 0 } testDir) dataDir = testDir;
#endif
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
                _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 11, 10, 24) };
                WebHost.Content = _web;
                await _web.EnsureCoreWebView2Async(env);
                var core = _web.CoreWebView2;
                var settings = core.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsPasswordAutosaveEnabled = false;
                settings.IsGeneralAutofillEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;
#if !DEBUG
                settings.AreDevToolsEnabled = false;
#endif
                // Tout est servi depuis l'exe ; aucune autre adresse n'est chargée ni ouverte
                core.AddWebResourceRequestedFilter(WebOrigin + "*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnWebResource;
                core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(WebOrigin)) e.Cancel = true; };
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.WebMessageReceived += OnWebMessage;
                // Page chargée (ou rechargée) : tout son état lui est renvoyé. Un message envoyé pendant son chargement
                // peut être perdu, et seuls les changements sont envoyés ensuite : sans cela, l'en-tête restait vide.
                core.NavigationCompleted += (_, _) => { _webSent.Clear(); _webForce = true; };
                _webTimer.Tick += (_, _) => PushWebState();
                core.Navigate(WebOrigin + "index.html");
            }
            _webSent.Clear(); // la page reçoit tout l'état à nouveau
            _webForce = true;
            ThemeManager.Changed += () => _webForce = true;
            _webTimer.Start();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // Sans WebView2, l'application n'a pas d'interface : le joueur est invité à l'installer
            if (MessageBox.Show(this, "Dofus Retro Manager a besoin de Microsoft Edge WebView2 Runtime, absent de ce PC.\n"
                + "Il est fourni avec Windows 11 ; sous Windows 10, installez-le depuis le site de Microsoft, puis relancez l'application.\n\n"
                + "Ouvrir la page de téléchargement ?",
                "Dofus Retro Manager", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                OpenUrl(WebView2DownloadUrl);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "L'interface n'a pas pu s'ouvrir : " + ex.Message, "Dofus Retro Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}
