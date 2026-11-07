using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DofusRetroManager.Data;
using DofusRetroManager.Net;

namespace DofusRetroManager;

public partial class MainWindow
{
    // --- Mise à jour ---

    UpdateInfo? _update;
    /// <summary>Version repoussée avec « Plus tard » : la vérification périodique ne la signale plus (une plus récente, si).</summary>
    Version? _updateDismissed;

    /// <summary>Après un échec (GitHub injoignable ou limite de requêtes atteinte), pause des vérifications périodiques.</summary>
    DateTime _updateRetryAt;
    bool _updateChecking;

    /// <summary>
    /// Vérification au lancement, puis toutes les 30 secondes tant que l'application reste ouverte : une version publiée
    /// pendant la session s'affiche sans relancer. Les vérifications périodiques ne lisent que le numéro de la dernière
    /// version (UpdateChecker.LatestVersionAsync, hors API GitHub) ; l'API n'est appelée que pour une version nouvelle.
    /// En cas d'échec, pause de 15 minutes.
    /// </summary>
    void StartUpdateChecks()
    {
        _ = CheckUpdatesAsync();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => { if (DateTime.Now >= _updateRetryAt) _ = CheckUpdatesAsync(periodic: true); };
        timer.Start();
    }

    async Task CheckUpdatesAsync(bool periodic = false)
    {
        // Téléchargement ou installation en cours : le bandeau ne change pas ; une seule vérification à la fois
        if (_download != null || _updateChecking) return;
        _updateChecking = true;
        try
        {
            if (periodic)
            {
                // Rien de nouveau (ou version déjà signalée, ou repoussée) : pas d'appel à l'API
                var latest = await UpdateChecker.LatestVersionAsync();
                if (latest == null || latest <= UpdateChecker.Current || latest <= _update?.Latest || latest <= _updateDismissed) return;
            }
            var update = await UpdateChecker.CheckAsync();
            // Release tout juste créée, installateur pas encore joint : on la reprendra à la vérification suivante
            if (periodic && update is { InstallerUrl: null }) return;
            if (update == null)
            {
                if (!periodic) AddLog($"Version {UpdateChecker.CurrentText} : à jour.");
                return;
            }
            // Déjà signalée, ou repoussée avec « Plus tard » : rien de nouveau
            if (_download != null || update.Latest <= _update?.Latest || update.Latest <= _updateDismissed) return;

            // Nouvelle version (éventuellement plus récente que celle déjà signalée) : bandeau remis à zéro
            _update = update;
            _installerPath = null;
            UpdateText.Text = "L'application télécharge l'installateur puis le lance : il remplace cette version en gardant vos banques et vos prix.";
            UpdateActionButton.Content = "Télécharger et installer";
            UpdateTitle.Text = $"Nouvelle version {_update.Latest} disponible (vous avez la {UpdateChecker.CurrentText})";
            if (_update.InstallerUrl == null)
            {
                // Release sans installateur joint : il ne reste que la page GitHub
                UpdateText.Text = "L'installateur n'est pas joint à cette version : téléchargez-le depuis la page GitHub.";
                UpdateActionButton.Content = "Ouvrir la page";
            }
            else if (UpdateChecker.CanUpdateSilently)
            {
                UpdateText.Text = "L'application télécharge la mise à jour, se ferme le temps de l'installer, puis se rouvre d'elle-même. "
                    + "Rien à accepter ; vos banques et vos prix sont conservés.";
                UpdateActionButton.Content = "Mettre à jour";
            }
            UpdateBanner.Visibility = Visibility.Visible;
            AddLog($"Mise à jour disponible : {_update.Latest} ({_update.PageUrl}).");
        }
        catch (Exception ex)
        {
            // Une ligne par échec, pas toutes les 30 secondes : on réessaie dans 15 minutes
            AddLog($"Vérification de mise à jour impossible : {ex.Message}");
            _updateRetryAt = DateTime.Now.AddMinutes(15);
        }
        finally { _updateChecking = false; }
    }

    /// <summary>Installateur téléchargé et vérifié, prêt à être lancé.</summary>
    string? _installerPath;
    CancellationTokenSource? _download;

    async void UpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null) return;
        if (_update.InstallerUrl == null) { UpdateNotes_Click(sender, e); return; }

        if (_installerPath != null) { InstallUpdate(); return; }

        _download = new CancellationTokenSource();
        UpdateActionButton.IsEnabled = false;
        UpdateLaterButton.Content = "Annuler";
        UpdateText.Text = "Téléchargement de l'installateur…";
        var progress = new Progress<double>(p =>
        {
            UpdateText.Text = $"Téléchargement de l'installateur… {p:P0}";
        });
        try
        {
            _installerPath = await UpdateChecker.DownloadAsync(_update, progress, _download.Token);
            AddLog($"Installateur {_update.Latest} téléchargé : {_installerPath}");
            if (UpdateChecker.CanUpdateSilently) { InstallUpdate(); return; }
            // Application lancée hors installation (ex. dossier de développement) : l'assistant demande où installer
            UpdateText.Text = "Téléchargement terminé et vérifié. L'application se fermera pendant l'installation ; "
                + "à la fin, laissez cochée « Lancer Dofus Retro Manager » pour la rouvrir.";
            UpdateActionButton.Content = "Installer maintenant";
        }
        catch (OperationCanceledException)
        {
            UpdateText.Text = "Téléchargement annulé.";
        }
        catch (Exception ex)
        {
            UpdateText.Text = "Téléchargement impossible : " + ex.Message + " Réessayez, ou passez par « Nouveautés » pour le télécharger depuis GitHub.";
            AddLog("Téléchargement de la mise à jour impossible : " + ex.Message);
        }
        finally
        {
            _download = null;
            UpdateActionButton.IsEnabled = true;
            UpdateLaterButton.Content = "Plus tard";
        }
    }

    void UpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        try { UpdateChecker.OpenInBrowser(_update?.PageUrl ?? UpdateChecker.ReleasesPage); }
        catch (Exception ex) { AddLog("Ouverture du navigateur impossible : " + ex.Message); }
    }

    /// <summary>
    /// Installe l'installateur téléchargé. Application installée : sans assistant, puis relance automatique
    /// (UpdateChecker.StartSilentUpdate) ; sinon, l'assistant habituel. Dans les deux cas l'application se ferme.
    /// </summary>
    void InstallUpdate()
    {
        try
        {
            if (UpdateChecker.CanUpdateSilently)
            {
                UpdateText.Text = "Installation de la mise à jour… L'application va se fermer puis se rouvrir d'elle-même.";
                UpdateChecker.StartSilentUpdate(_installerPath!);
            }
            else UpdateChecker.StartInstaller(_installerPath!);
            AddLog($"Installation de la version {_update!.Latest} lancée.");
            Application.Current.Shutdown();
        }
        catch (Exception ex) { UpdateText.Text = "Impossible de lancer l'installation : " + ex.Message; }
    }

    /// <summary>Relancée après une mise à jour automatique : nouveautés si elle a réussi, explication sinon.</summary>
    void ShowUpdateResult()
    {
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(UpdateChecker.UpdateResultArg));
        if (arg == null) return;
        int.TryParse(arg[UpdateChecker.UpdateResultArg.Length..], out var code);
        if (arg.Length > UpdateChecker.UpdateResultArg.Length && code is 0 or 3010)
        {
            AddLog($"Mise à jour installée : version {UpdateChecker.CurrentText}.");
            UpdateChecker.CleanDownloads();
            ShowChangelog();
            return;
        }
        var reason = code switch
        {
            1602 => "La mise à jour a été annulée.",
            1618 => "Une autre installation Windows était en cours : la mise à jour n'a pas pu se faire.",
            _ => $"La mise à jour a échoué (code {code}). Détails : {UpdateChecker.UpdateLogPath}",
        };
        AddLog(reason);
        MessageBox.Show(this, reason + "\n\nVous gardez la version " + UpdateChecker.CurrentText
            + ". Réessayez depuis le bandeau de mise à jour, ou installez-la depuis la page GitHub.",
            "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Demandes d'ouverture des nouveautés : la page ouvre sa fenêtre « Nouveautés » à chaque nouvelle demande.</summary>
    int _changelogAsk;

    void ShowChangelog() { _changelogAsk++; _webForce = true; }

    void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        if (_download != null) { _download.Cancel(); return; } // « Annuler » pendant le téléchargement
        UpdateBanner.Visibility = Visibility.Collapsed;
        _updateDismissed = _update?.Latest;
    }
}
