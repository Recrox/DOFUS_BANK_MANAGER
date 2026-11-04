using System.Diagnostics;

namespace DofusRetroManager.Net;

/// <summary>
/// Le pare-feu Windows filtre les paquets entrants livrés aux sockets brutes : sans règle
/// d'autorisation, seul le trafic sortant est visible. On ajoute donc une règle entrante
/// limitée à l'exécutable de l'application (qui n'ouvre aucun port en écoute).
/// </summary>
public static class FirewallRule
{
    const string RuleName = "Dofus Retro Manager";
    const string LegacyRuleName = "Dofus Bank Manager";

    /// <summary>Crée la règle si elle n'existe pas pour cet exécutable. Retourne un message pour le journal.</summary>
    public static string Ensure()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return "Pare-feu : chemin de l'exécutable inconnu.";

        // Règle créée sous l'ancien nom de l'application (jusqu'à la 1.32) : elle visait l'ancien exécutable
        if (Netsh($"advfirewall firewall show rule name=\"{LegacyRuleName}\"").Item1 == 0)
            Netsh($"advfirewall firewall delete rule name=\"{LegacyRuleName}\"");

        var (code, output) = Netsh($"advfirewall firewall show rule name=\"{RuleName}\" verbose");
        if (code == 0 && output.Contains(exe, StringComparison.OrdinalIgnoreCase))
            return "Pare-feu : règle d'autorisation déjà présente.";

        (code, output) = Netsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exe}\" enable=yes profile=any");
        return code == 0
            ? $"Pare-feu : règle entrante ajoutée pour {exe}."
            : $"Pare-feu : impossible d'ajouter la règle ({output.Trim()}).";
    }

    static (int, string) Netsh(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, output);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}
