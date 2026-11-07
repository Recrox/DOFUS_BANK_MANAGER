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
    // --- Capture réseau ---

    void OnMessage(string stream, string msg)
    {
        // Seuls les messages de banque/inventaire sont journalisés (voir REGLES.md, règle 4)
        if (LogPacketsEnabled && DofusTracker.IsLoggable(msg))
        {
            try { File.AppendAllText(PacketLogPath, $"{DateTime.Now:HH:mm:ss.fff} {(msg.Length > 2000 ? msg[..2000] + "…" : msg)}\n"); }
            catch { }
        }
        // Le suivi de l'état se fait sur le thread de l'interface pour éviter tout accès concurrent
        Ui(() =>
        {
            if (_streamsWithMessages.Add(stream))
                AddLog($"Messages texte reçus sur {stream}.");
            // Multi-compte : chasse et marchands sont suivis par fenêtre de jeu
            if (_hunt != null) _hunt.Stream = stream;
            if (_merchants != null) _merchants.Stream = stream;
            if (_trades != null) _trades.Stream = stream;
            if (_harvest != null) _harvest.Stream = stream;
            try { _tracker.Handle(stream, msg); TryIdentify(stream); }
            catch (Exception ex) { AddLog($"Paquet ignoré ({ex.Message}) : {msg[..Math.Min(80, msg.Length)]}"); }
        });
    }

    /// <summary>Diagnostic : indique si les connexions du client Dofus sont lisibles (texte) ou chiffrées.</summary>
    void OnStreamClassified(string server, bool isText, byte firstByte)
    {
        if (!DofusConnections.RemoteEndpoints().Contains(server)) return;
        Ui(() => AddLog(isText
            ? $"Connexion Dofus {server} : flux texte lisible."
            : $"Connexion Dofus {server} : flux illisible (premier octet 0x{firstByte:X2}{(firstByte == 0x16 || firstByte == 0x17 ? ", chiffrement TLS" : "")})."));
    }

    void LogDofusConnections()
    {
        var eps = DofusConnections.RemoteEndpoints();
        AddLog(eps.Count == 0
            ? "Client Dofus Retro introuvable (aucune connexion)."
            : "Connexions du client Dofus Retro : " + string.Join(", ", eps));
    }

    readonly HashSet<string> _streamsWithMessages = new();

    volatile bool _logPackets;
    bool LogPacketsEnabled => _logPackets;

    void StartCapture()
    {
        try
        {
            AddLog(FirewallRule.Ensure());
            _sniffer.Start();
            AddLog("Capture démarrée. Ouvrez votre banque en jeu pour la synchroniser.");
            LogDofusConnections();
        }
        catch (Exception ex)
        {
            AddLog("Capture impossible : " + ex.Message);
        }
        UpdateCaptureUi();
    }

    void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sniffer.IsRunning) { _sniffer.Stop(); AddLog("Capture arrêtée."); UpdateCaptureUi(); }
        else StartCapture();
    }

    void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Exception ex) { AddLog("Élévation annulée : " + ex.Message); }
    }

    void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Sans shell, le nouveau processus hérite des droits actuels (pas de nouvelle demande administrateur)
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
            Application.Current.Shutdown();
        }
        catch (Exception ex) { AddLog("Relance impossible : " + ex.Message); }
    }

    void UpdateCaptureUi()
    {
        bool running = _sniffer.IsRunning;
        CaptureDot.Fill = running ? Brushes.LimeGreen : (_isAdmin ? Brushes.Gray : Brushes.OrangeRed);
        CaptureStatus.Text = running ? "Capture active — en écoute du trafic Dofus"
            : _isAdmin ? "Capture arrêtée" : "Droits administrateur requis pour lire le trafic réseau";
        CaptureButton.Content = running ? "Arrêter la capture" : "Démarrer la capture";
        CaptureButton.IsEnabled = _isAdmin;
        AdminButton.Visibility = _isAdmin ? Visibility.Collapsed : Visibility.Visible;
    }

    void LogPacketsBox_Changed(object sender, RoutedEventArgs e)
    {
        _logPackets = LogPacketsBox.IsChecked == true;
        if (_logPackets) AddLog($"Journalisation des paquets dans {PacketLogPath}");
    }
}
