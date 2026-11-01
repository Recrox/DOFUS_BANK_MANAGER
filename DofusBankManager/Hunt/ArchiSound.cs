using System.IO;
using System.Media;

namespace DofusBankManager.Hunt;

/// <summary>
/// Alerte archi-monstre : arpège cristallin montant (mi, si, mi, sol dièse), au timbre de cloche, synthétisé par
/// l'application (aucun fichier son à distribuer), et son plus court des notifications de l'onglet Fenêtres.
/// Le volume est appliqué aux échantillons (0 à 100).
/// </summary>
public static class ArchiSound
{
    const int Rate = 44100;
    /// <summary>Notes de l'arpège (Hz) et leur départ (s).</summary>
    static readonly (double Freq, double Start)[] Notes =
        [(659.25, 0), (987.77, 0.085), (1318.51, 0.17), (1661.22, 0.255), (2637.02, 0.34)];
    /// <summary>Notification (combat, tour, échange, groupe) : deux notes, plus courte et plus douce.</summary>
    static readonly (double Freq, double Start)[] NoticeNotes = [(1318.51, 0), (1975.53, 0.09)];
    /// <summary>Partiels d'une cloche : rapport de fréquence, amplitude, vitesse d'extinction.</summary>
    static readonly (double Ratio, double Amp, double Decay)[] Partials =
        [(1, 1, 3.2), (2, 0.32, 5), (3, 0.12, 7), (4.17, 0.07, 9), (5.43, 0.035, 12)];

    static SoundPlayer? _player; // gardé le temps de la lecture

    public static void Play(int volume) => Play(Wav(Notes, 1.6, volume));

    /// <summary>Son des notifications de l'onglet Fenêtres, un peu moins fort que l'alerte archi-monstre.</summary>
    public static void PlayNotice(int volume) => Play(Wav(NoticeNotes, 0.9, volume * 0.8));

    static void Play(byte[]? wav)
    {
        if (wav == null) return;
        _player?.Stop();
        _player = new SoundPlayer(new MemoryStream(wav));
        _player.Play();
    }

    static byte[]? Wav((double Freq, double Start)[] notes, double seconds, double volume)
    {
        if (volume <= 0) return null;
        int n = (int)(Rate * seconds);
        var samples = new double[n];
        for (int k = 0; k < notes.Length; k++)
        {
            var (freq, start) = notes[k];
            double gain = k == notes.Length - 1 && notes.Length > 2 ? 0.45 : 1; // dernière note de l'arpège : étincelle plus douce
            for (int i = (int)(start * Rate); i < n; i++)
            {
                double t = i / (double)Rate - start;
                double attack = Math.Min(1, t / 0.004);
                double s = 0;
                foreach (var (ratio, amp, decay) in Partials)
                    s += amp * Math.Exp(-decay * t) * Math.Sin(2 * Math.PI * freq * ratio * t);
                samples[i] += gain * attack * s;
            }
        }
        // Écho léger : un peu d'espace autour du son
        int delay = (int)(0.11 * Rate);
        for (int i = n - 1; i >= delay; i--) samples[i] += 0.28 * samples[i - delay];

        // Normalisation, puis volume (courbe quadratique : le curseur paraît plus régulier à l'oreille)
        double peak = samples.Max(Math.Abs);
        double scale = peak > 0 ? 0.9 * Math.Pow(Math.Clamp(volume, 0, 100) / 100.0, 2) / peak : 0;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2);
        w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        for (int i = 0; i < n; i++)
        {
            double fade = i > n - 2000 ? (n - i) / 2000.0 : 1; // fin sans clic
            w.Write((short)Math.Round(samples[i] * scale * fade * short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }
}
