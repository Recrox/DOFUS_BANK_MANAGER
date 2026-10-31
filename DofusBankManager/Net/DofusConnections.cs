using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace DofusBankManager.Net;

/// <summary>
/// Liste les connexions TCP ouvertes par le client Dofus Retro, en lisant la table
/// des connexions de Windows (comme netstat). Le client n'est jamais touché.
/// </summary>
public static class DofusConnections
{
    const int AF_INET = 2;
    const int TCP_TABLE_OWNER_PID_ALL = 5;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    struct MibTcpRowOwnerPid
    {
        public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort, OwningPid;
    }

    /// <summary>Points de connexion distants ("ip:port") des processus Dofus Retro.</summary>
    public static HashSet<string> RemoteEndpoints()
    {
        var result = new HashSet<string>();
        var pids = Process.GetProcessesByName("Dofus Retro").Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return result;

        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0) return result;
            int count = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(buf + 4 + i * rowSize);
                if (!pids.Contains(row.OwningPid)) continue;
                int port = (int)(((row.RemotePort & 0xFF) << 8) | ((row.RemotePort >> 8) & 0xFF));
                result.Add($"{new IPAddress(row.RemoteAddr)}:{port}");
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return result;
    }
}
