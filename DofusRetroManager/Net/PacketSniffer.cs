using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace DofusRetroManager.Net;

/// <summary>
/// Écoute passive du trafic IPv4 via des sockets brutes Windows (SIO_RCVALL).
/// Aucun paquet n'est envoyé ni modifié : on lit uniquement ce que la carte réseau reçoit.
/// Nécessite les droits administrateur.
/// Les flux TCP sont réassemblés puis découpés en messages Dofus (terminés par '\0').
/// </summary>
public sealed class PacketSniffer : IDisposable
{
    readonly List<Socket> _sockets = new();
    readonly Dictionary<string, TcpStream> _streams = new();
    readonly object _lock = new();
    CancellationTokenSource? _cts;

    /// <summary>Message texte complet reçu (clé du flux, contenu).</summary>
    public event Action<string, string>? MessageReceived;
    /// <summary>Flux TCP fermé (FIN ou RST) : clé du flux.</summary>
    public event Action<string>? StreamClosed;
    public event Action<string>? Log;
    /// <summary>Premier segment d'un flux entrant analysé : (serveur ip:port, flux texte ?, premier octet).</summary>
    public event Action<string, bool, byte>? StreamClassified;

    readonly HashSet<IPAddress> _localIps = new();

    public bool IsRunning => _cts != null;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();

        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToList();
        _localIps.Clear();
        _localIps.UnionWith(addresses);

        foreach (var ip in addresses)
        {
            try
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                s.Bind(new IPEndPoint(ip, 0));
                s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
                s.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), new byte[4]);
                s.ReceiveBufferSize = 4 * 1024 * 1024;
                _sockets.Add(s);
                var token = _cts.Token;
                _ = Task.Run(() => ReceiveLoop(s, token));
                Log?.Invoke($"Capture active sur {ip}");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Impossible d'écouter sur {ip} : {ex.Message}");
            }
        }

        if (_sockets.Count == 0)
        {
            Stop();
            throw new InvalidOperationException("Aucune interface réseau n'a pu être écoutée (droits administrateur ?).");
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        foreach (var s in _sockets) { try { s.Close(); } catch { } }
        _sockets.Clear();
        lock (_lock) _streams.Clear();
    }

    public void Dispose() => Stop();

    void ReceiveLoop(Socket s, CancellationToken token)
    {
        var buf = new byte[65535];
        while (!token.IsCancellationRequested)
        {
            int n;
            try { n = s.Receive(buf); }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { if (token.IsCancellationRequested) return; continue; }
            try { HandleIpPacket(buf, n); }
            catch (Exception ex) { Log?.Invoke($"Erreur de décodage : {ex.Message}"); }
        }
    }

    void HandleIpPacket(byte[] b, int n)
    {
        if (n < 20 || (b[0] >> 4) != 4 || b[9] != 6) return; // IPv4 + TCP uniquement
        int ihl = (b[0] & 0x0F) * 4;
        int total = (b[2] << 8) | b[3];
        if (total == 0 || total > n) total = n; // segments coalescés (LRO)
        if (total < ihl + 20) return;

        int t = ihl;
        int srcPort = (b[t] << 8) | b[t + 1], dstPort = (b[t + 2] << 8) | b[t + 3];
        uint seq = (uint)((b[t + 4] << 24) | (b[t + 5] << 16) | (b[t + 6] << 8) | b[t + 7]);
        int dataOffset = (b[t + 12] >> 4) * 4;
        byte flags = b[t + 13];
        bool syn = (flags & 0x02) != 0, fin = (flags & 0x01) != 0, rst = (flags & 0x04) != 0;

        var key = $"{b[12]}.{b[13]}.{b[14]}.{b[15]}:{srcPort} > {b[16]}.{b[17]}.{b[18]}.{b[19]}:{dstPort}";
        int payloadStart = t + dataOffset, payloadLen = total - payloadStart;

        // Fermeture (dans un sens ou l'autre) : la fenêtre de jeu correspondante est fermée ou déconnectée
        if (fin || rst)
        {
            var reverse = $"{b[16]}.{b[17]}.{b[18]}.{b[19]}:{dstPort} > {b[12]}.{b[13]}.{b[14]}.{b[15]}:{srcPort}";
            StreamClosed?.Invoke(key);
            StreamClosed?.Invoke(reverse);
        }

        List<string>? messages = null;
        bool classified = false;
        TcpStream? current;
        lock (_lock)
        {
            if (rst) { _streams.Remove(key); return; }
            if (!_streams.TryGetValue(key, out current))
            {
                if (payloadLen <= 0 && !syn) return;
                _streams[key] = current = new TcpStream(syn ? seq + 1 : seq);
            }
            else if (syn) current.Reset(seq + 1);

            // Un flux ignoré (binaire/chiffré) reste mémorisé pour ne pas être réanalysé à chaque segment
            if (payloadLen > 0 && !current.Ignored)
            {
                bool wasChecked = current.Checked;
                messages = current.Push(seq, b.AsSpan(payloadStart, payloadLen));
                classified = !wasChecked && current.Checked;
            }

            if (fin) _streams.Remove(key);
            if (_streams.Count > 2000) PruneStreams();
        }

        // Diagnostic : uniquement l'adresse du serveur et le type de flux, jamais le contenu
        if (classified && !_localIps.Contains(new IPAddress(b.AsSpan(12, 4))))
            StreamClassified?.Invoke($"{b[12]}.{b[13]}.{b[14]}.{b[15]}:{srcPort}", !current.Ignored, b[payloadStart]);

        if (messages != null)
            foreach (var m in messages) MessageReceived?.Invoke(key, m);
    }

    void PruneStreams()
    {
        var limit = DateTime.UtcNow.AddMinutes(-10);
        foreach (var k in _streams.Where(kv => kv.Value.LastSeen < limit).Select(kv => kv.Key).ToList())
            _streams.Remove(k);
    }

    /// <summary>Réassemblage d'un sens d'une connexion TCP, découpé sur '\0'.</summary>
    sealed class TcpStream
    {
        const int MaxBuffer = 8 * 1024 * 1024;
        uint _next;
        readonly List<byte> _buffer = new();
        readonly SortedDictionary<uint, byte[]> _pending = new(Comparer<uint>.Create((a, b) => (int)(a - b)));
        bool _checked;

        public bool Checked => _checked;
        public bool Ignored { get; private set; }
        public DateTime LastSeen { get; private set; } = DateTime.UtcNow;

        public TcpStream(uint next) => _next = next;

        public void Reset(uint next)
        {
            _next = next; _buffer.Clear(); _pending.Clear(); _checked = false; Ignored = false;
        }

        public List<string>? Push(uint seq, ReadOnlySpan<byte> data)
        {
            LastSeen = DateTime.UtcNow;
            if (!_checked)
            {
                _checked = true;
                // Les flux binaires (TLS, etc.) ne sont pas du protocole Dofus texte.
                // On juge sur tout le segment : on peut arriver en plein milieu d'un flux déjà ouvert.
                if (!LooksLikeText(data)) { Ignored = true; return null; }
            }

            int diff = (int)(seq - _next);
            if (diff > 0)
            {
                // Segment en avance : on le garde en attendant les trous
                _pending[seq] = data.ToArray();
                if (_pending.Count > 256) { var firstKey = _pending.Keys.First(); _buffer.Clear(); _next = firstKey; }
                else return null;
            }
            else
            {
                if (-diff >= data.Length) return null; // retransmission déjà reçue
                Append(data[(-diff)..]);
            }

            while (_pending.Count > 0)
            {
                var (s, d) = _pending.First();
                int dd = (int)(s - _next);
                if (dd > 0) break;
                _pending.Remove(s);
                if (-dd < d.Length) Append(d.AsSpan(-dd));
            }

            if (_buffer.Count > MaxBuffer) { Ignored = true; return null; }
            return Extract();
        }

        static bool LooksLikeText(ReadOnlySpan<byte> data)
        {
            if (data[0] is 0x14 or 0x15 or 0x16 or 0x17) return false; // en-tête d'enregistrement TLS
            int control = 0;
            foreach (var x in data)
                if (x < 0x20 && x != 0 && x != (byte)'\n' && x != (byte)'\r' && x != (byte)'\t') control++;
            // Des données chiffrées contiennent ~12 % d'octets de contrôle ; du texte Dofus quasiment aucun
            return control * 50 <= data.Length;
        }

        void Append(ReadOnlySpan<byte> d)
        {
            foreach (var x in d) _buffer.Add(x);
            _next += (uint)d.Length;
        }

        List<string>? Extract()
        {
            List<string>? result = null;
            int start = 0;
            for (int i = 0; i < _buffer.Count; i++)
            {
                if (_buffer[i] != 0) continue;
                var msg = Encoding.UTF8.GetString(_buffer.GetRange(start, i - start).ToArray()).TrimEnd('\n', '\r');
                if (msg.Length > 0) (result ??= new()).Add(msg);
                start = i + 1;
            }
            if (start > 0) _buffer.RemoveRange(0, start);
            return result;
        }
    }
}
