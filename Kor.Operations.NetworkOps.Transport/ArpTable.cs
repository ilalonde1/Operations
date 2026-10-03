#nullable enable
using System.Net;
using System.Runtime.InteropServices;

namespace Kor.Operations.NetworkOps.Transport;

/// <summary>
/// This machine's ARP table (IP Helper GetIpNetTable): which MAC answered for which IPv4 address. Read on APP01, where the
/// service runs and talks to every server, NAS, host and switch every 5 minutes -- so the rack's own devices are always in
/// it. Used to name what the core switch has learned on each port (Ian, 2026-10-02: "which IP / device name is attached to
/// each port"). Read-only; nothing is sent on the network.
/// </summary>
public static class ArpTable
{
    /// <summary>MAC ("aa:bb:cc:dd:ee:ff") -> IPv4 address, for every dynamic or static entry with a real MAC.</summary>
    public static IReadOnlyDictionary<string, string> Read()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return map;
        var size = 0;
        _ = GetIpNetTable(IntPtr.Zero, ref size, false);
        if (size <= 0) return map;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetIpNetTable(buffer, ref size, true) != 0) return map;
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibIpNetRow>();
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibIpNetRow>(buffer + 4 + i * rowSize);
                if (row.PhysAddrLen != 6 || row.Type is not (3 or 4)) continue;          // 3 dynamic, 4 static (2 invalid)
                var mac = string.Join(":", row.PhysAddr.Take(6).Select(x => x.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
                if (mac is "ff:ff:ff:ff:ff:ff" or "00:00:00:00:00:00" || mac.StartsWith("01:00:5e", StringComparison.Ordinal)) continue;   // broadcast / multicast
                map[mac] = new IPAddress(BitConverter.GetBytes(row.Addr)).ToString();
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return map;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibIpNetRow
    {
        public int Index;
        public int PhysAddrLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] PhysAddr;
        public uint Addr;
        public int Type;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetIpNetTable(IntPtr table, ref int size, bool order);
}
