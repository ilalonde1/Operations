#nullable enable
using System.Text;

namespace Kor.Operations.NetworkOps.Core.Smbios;

// Parses the raw SMBIOS blob Windows mirrors into the registry at
// HKLM\SYSTEM\CurrentControlSet\Services\mssmbios\Data\SMBiosData.
//
// WMI would hand most of this over in one call, but WMI needs WinRM or RPC, both blocked to
// workstations here. The registry blob carries the same firmware data -- including per-DIMM
// part numbers and populated-slot counts, which is the difference between "32 GB installed"
// and "32 GB in 4 of 4 slots, nowhere left to grow". PSU wattage is NOT in SMBIOS.
//
// Ported from ConvertFrom-KorSmbios (tools/WorkstationOps) and held by the same fixture: a
// real blob captured from KOR-SPARE100 on 2026-08-13.
public static class SmbiosParser
{
    private static readonly IReadOnlyDictionary<int, string> ChassisTypes = new Dictionary<int, string>
    {
        [1] = "Other", [2] = "Unknown", [3] = "Desktop", [4] = "Low Profile Desktop", [5] = "Pizza Box",
        [6] = "Mini Tower", [7] = "Tower", [8] = "Portable", [9] = "Laptop", [10] = "Notebook",
        [11] = "Hand Held", [12] = "Docking Station", [13] = "All in One", [14] = "Sub Notebook",
        [15] = "Space-saving", [16] = "Lunch Box", [17] = "Main Server Chassis", [18] = "Expansion Chassis",
        [23] = "Rack Mount Chassis", [24] = "Sealed-case PC", [25] = "Multi-system", [30] = "Tablet",
        [31] = "Convertible", [32] = "Detachable", [34] = "Mini PC", [35] = "Stick PC",
    };

    private static readonly IReadOnlyDictionary<int, string> MemoryTypes = new Dictionary<int, string>
    {
        [1] = "Other", [2] = "Unknown", [3] = "DRAM", [17] = "SDRAM", [18] = "SGRAM", [20] = "DDR",
        [21] = "DDR2", [24] = "DDR3", [26] = "DDR4", [34] = "DDR5", [35] = "LPDDR5",
    };

    private static readonly IReadOnlyDictionary<int, string> FormFactors = new Dictionary<int, string>
    {
        [1] = "Other", [2] = "Unknown", [8] = "DIMM", [9] = "TSOP", [12] = "SODIMM", [13] = "RIMM", [15] = "FB-DIMM",
    };

    /// <summary>
    /// Input: the registry value's bytes -- an 8-byte header (major/minor at [1]/[2], table
    /// length DWORD at [4]) followed by the SMBIOS table. Each structure is type/length/handle
    /// + a formatted area + a double-null-terminated string table.
    /// </summary>
    public static SmbiosInfo Parse(byte[] bytes)
    {
        if (bytes.Length < 12)
            throw new ArgumentException("SMBIOS blob is too short to contain a header and one structure.", nameof(bytes));

        var declared = BitConverter.ToUInt32(bytes, 4);
        var end = (int)Math.Min(8L + declared, bytes.Length);
        var p = 8;
        var structs = new List<RawStructure>();

        while (p + 4 <= end)
        {
            var type = bytes[p];
            var flen = bytes[p + 1];
            // A formatted area shorter than its own 4-byte header means the table is corrupt:
            // stop rather than walk off into arbitrary bytes and report confident nonsense.
            if (flen < 4 || p + flen > end) break;

            var data = bytes.AsSpan(p, flen).ToArray();
            var sp = p + flen;
            var strings = new List<string>();
            if (sp + 1 < end && bytes[sp] == 0 && bytes[sp + 1] == 0)
            {
                sp += 2;
            }
            else
            {
                while (sp < end)
                {
                    var start = sp;
                    while (sp < end && bytes[sp] != 0) sp++;
                    strings.Add(Encoding.ASCII.GetString(bytes, start, sp - start));
                    sp++;
                    if (sp < end && bytes[sp] == 0) { sp++; break; }
                }
            }

            structs.Add(new RawStructure(type, data, strings));
            p = sp;
            if (type == 127) break;   // end-of-table
        }

        RawStructure? First(int t) => structs.FirstOrDefault(s => s.Type == t);
        var sys = First(1);
        var board = First(2);
        var chassis = First(3);
        var cpu = First(4);
        var array = First(16);

        var dimms = structs.Where(s => s.Type == 17).Select(ParseDimm).ToList();

        return new SmbiosInfo(
            Version: $"{bytes[1]}.{bytes[2]}",
            System: sys is null ? null : new SmbiosSystem(
                sys.String(0x04), sys.String(0x05), sys.Data.Length > 0x1A ? sys.String(0x1A) : null),
            Board: board is null ? null : new SmbiosBoard(board.String(0x04), board.String(0x05)),
            Chassis: chassis is null ? null : ParseChassis(chassis),
            Processor: cpu is null ? null : new SmbiosProcessor(
                Version: cpu.String(0x10),
                Socket: cpu.String(0x04),
                MaxSpeedMHz: cpu.Data.Length >= 0x16 ? BitConverter.ToUInt16(cpu.Data, 0x14) : null,
                Cores: cpu.Data.Length > 0x23 ? cpu.Data[0x23] : null,
                Threads: cpu.Data.Length > 0x25 ? cpu.Data[0x25] : null),
            Memory: ParseMemory(array, dimms));
    }

    private static SmbiosChassis ParseChassis(RawStructure c)
    {
        // A Type-3 whose formatted area is only 4-5 bytes passes the flen>=4 walk guard but has no type byte: read it
        // only when present (0 -> Unknown), rather than throw and take the whole machine's hardware profile down.
        var code = c.Data.Length > 0x05 ? c.Data[0x05] & 0x7F : 0;   // bit 7 is "lock present", not part of the type
        return new SmbiosChassis(c.String(0x04), code, ChassisTypes.TryGetValue(code, out var n) ? n : $"Unknown({code})");
    }

    private static SmbiosDimm ParseDimm(RawStructure d)
    {
        // A Type-17 with a formatted area shorter than 14 bytes passes the flen>=4 walk guard but has no size field:
        // treat it as an empty slot (every other field below is already length-guarded), rather than throw out of the
        // whole fleet's hardware run (Cli hardware calls Parse with no catch).
        var raw = d.Data.Length >= 0x0E ? BitConverter.ToUInt16(d.Data, 0x0C) : (ushort)0;
        // 0 = slot empty. 0x7FFF = "see the extended DWORD". Bit 15 set = the value is KB, not MB.
        double sizeMb = raw == 0 ? 0
            : raw == 0x7FFF && d.Data.Length >= 0x20 ? BitConverter.ToUInt32(d.Data, 0x1C)
            : (raw & 0x8000) != 0 ? (raw & 0x7FFF) / 1024.0
            : raw;
        return new SmbiosDimm(
            Locator: d.String(0x10),
            Bank: d.String(0x11),
            SizeMB: sizeMb,
            Type: d.Data.Length > 0x12 && MemoryTypes.TryGetValue(d.Data[0x12], out var mt) ? mt : "Unknown",
            FormFactor: d.Data.Length > 0x0E && FormFactors.TryGetValue(d.Data[0x0E], out var ff) ? ff : "Unknown",
            RatedMTs: d.Data.Length >= 0x17 ? BitConverter.ToUInt16(d.Data, 0x15) : null,
            ConfiguredMTs: d.Data.Length >= 0x22 ? BitConverter.ToUInt16(d.Data, 0x20) : null,
            Manufacturer: d.String(0x17),
            PartNumber: d.String(0x1A));
    }

    private static SmbiosMemory ParseMemory(RawStructure? array, IReadOnlyList<SmbiosDimm> dimms)
    {
        var populated = dimms.Count(x => x.Populated);
        // Slots comes from the Type 16 array when present, but the count of Type 17 structures is
        // the real physical slot count, and it is what an upgrade decision hangs on.
        var slots = array is not null && array.Data.Length >= 0x0F ? BitConverter.ToUInt16(array.Data, 0x0D) : dimms.Count;
        int? maxGb = null;
        if (array is not null && array.Data.Length >= 0x0B)
        {
            var kb = BitConverter.ToUInt32(array.Data, 0x07);
            maxGb = kb == 0x80000000 && array.Data.Length >= 0x17
                ? (int)Math.Round(BitConverter.ToUInt64(array.Data, 0x0F) / (1024.0 * 1024 * 1024))
                : (int)Math.Round(kb / (1024.0 * 1024));
        }
        return new SmbiosMemory(
            Slots: slots,
            SlotsPopulated: populated,
            SlotsFree: dimms.Count - populated,
            // A mangled table must degrade to zero, not take the whole profile down with it.
            InstalledGB: Math.Round(dimms.Sum(x => x.SizeMB) / 1024.0, 1),
            MaxCapacityGB: maxGb,
            Dimms: dimms);
    }

    private sealed record RawStructure(byte Type, byte[] Data, IReadOnlyList<string> Strings)
    {
        // SMBIOS strings are 1-based indexes into the structure's own string table; 0 means absent.
        public string? String(int offset)
        {
            if (offset >= Data.Length) return null;
            var i = Data[offset];
            if (i == 0 || i > Strings.Count) return null;
            return Strings[i - 1].Trim();
        }
    }
}

public sealed record SmbiosInfo(
    string Version, SmbiosSystem? System, SmbiosBoard? Board, SmbiosChassis? Chassis,
    SmbiosProcessor? Processor, SmbiosMemory Memory);

public sealed record SmbiosSystem(string? Manufacturer, string? Product, string? Family);

public sealed record SmbiosBoard(string? Manufacturer, string? Product);

public sealed record SmbiosChassis(string? Manufacturer, int TypeCode, string Type);

public sealed record SmbiosProcessor(string? Version, string? Socket, int? MaxSpeedMHz, int? Cores, int? Threads);

public sealed record SmbiosMemory(
    int Slots, int SlotsPopulated, int SlotsFree, double InstalledGB, int? MaxCapacityGB, IReadOnlyList<SmbiosDimm> Dimms);

public sealed record SmbiosDimm(
    string? Locator, string? Bank, double SizeMB, string Type, string FormFactor,
    int? RatedMTs, int? ConfiguredMTs, string? Manufacturer, string? PartNumber)
{
    public bool Populated => SizeMB > 0;
}
