using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace DofusRetroManager.Net;

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

    /// <summary>
    /// Port local de chaque connexion TCP du client Dofus Retro -> processus qui l'a ouverte (table de Windows, comme
    /// netstat -o). Sert à retrouver la fenêtre de jeu d'une connexion quand l'application démarre après le jeu.
    /// </summary>
    public static Dictionary<int, uint> LocalPortOwners()
    {
        var result = new Dictionary<int, uint>();
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
                int port = (int)(((row.LocalPort & 0xFF) << 8) | ((row.LocalPort >> 8) & 0xFF));
                result[port] = row.OwningPid;
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return result;
    }

    /// <summary>Processus parent de chaque processus (liste des processus de Windows ; rien n'est lu dans leur mémoire).</summary>
    public static Dictionary<uint, uint> ParentProcesses()
    {
        var result = new Dictionary<uint, uint>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return result;
        try
        {
            var e = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                result[e.th32ProcessID] = e.th32ParentProcessID;
        }
        finally { CloseHandle(snap); }
        return result;
    }

    const uint TH32CS_SNAPPROCESS = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref ProcessEntry32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref ProcessEntry32 e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}
