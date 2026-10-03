#nullable enable
using Kor.Operations.NetworkOps.Core.Smbios;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The port of ConvertFrom-KorSmbios, held to the same fixture and the same assertions as the
// PowerShell suite (tools/WorkstationOps/Tests). The blob is a real capture from KOR-SPARE100
// on 2026-08-13; the expected values are what that machine physically is, cross-checked
// against the ASUS board and the Corsair kit part number.
//
// WHAT IT COVERS: header/version, system/board/chassis strings, processor socket/cores/threads,
// per-DIMM size/type/speed/maker/part, slot and capacity arithmetic, refusal of short or
// corrupt input, and type-specific structures (Type 3, Type 17) whose formatted area passes the
// flen>=4 walk guard but is too short for the type's own fields (must degrade, not throw).
// WHAT IT DOES NOT: laptops (SODIMM, chassis 9/10), the 0x7FFF extended-size
// and KB-granularity DIMM branches, and the 0x80000000 extended max-capacity branch -- no
// fixture exercises them. A SAME-CLASS FAULT IT WOULD NOT CATCH: a board that reports a Type 16
// slot count different from its Type 17 structure count would read wrong in SlotsFree and no
// fixture here disagrees with itself that way.
public sealed class SmbiosParserTests
{
    private static byte[] Fixture(string name)
    {
        var hex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)).Trim();
        return Convert.FromHexString(hex);
    }

    private static readonly SmbiosInfo Spare100 = SmbiosParser.Parse(Fixture("smbios-KOR-SPARE100.hex"));

    [Fact]
    public void Board_and_chassis_decode()
    {
        Assert.Equal("3.0", Spare100.Version);
        Assert.Equal("ASUSTeK COMPUTER INC.", Spare100.Board!.Manufacturer);
        Assert.Equal("STRIX Z270H GAMING", Spare100.Board.Product);
        Assert.Equal("Desktop", Spare100.Chassis!.Type);
        Assert.Equal(3, Spare100.Chassis.TypeCode);
    }

    [Fact]
    public void Processor_decodes()
    {
        Assert.Equal("LGA1151", Spare100.Processor!.Socket);
        Assert.Equal(4, Spare100.Processor.Cores);
        Assert.Equal(8, Spare100.Processor.Threads);
        Assert.Contains("i7-7700K", Spare100.Processor.Version);
    }

    [Fact]
    public void Memory_numbers_an_upgrade_decision_turns_on()
    {
        var m = Spare100.Memory;
        Assert.Equal(4, m.Slots);
        Assert.Equal(4, m.SlotsPopulated);
        Assert.Equal(0, m.SlotsFree);          // cannot add, only replace
        Assert.Equal(32, m.InstalledGB);
        Assert.Equal(64, m.MaxCapacityGB);     // Z270
        Assert.Equal(4, m.Dimms.Count);
        Assert.All(m.Dimms, d =>
        {
            Assert.Equal(8192, d.SizeMB);
            Assert.Equal("DDR4", d.Type);
            Assert.Equal(3000, d.RatedMTs);
            Assert.Equal("Corsair", d.Manufacturer);
            Assert.Equal("CMK16GX4M2B3000C15", d.PartNumber);
            Assert.True(d.Populated);
        });
    }

    [Fact]
    public void A_blob_too_short_for_a_header_is_refused()
        => Assert.Throws<ArgumentException>(() => SmbiosParser.Parse(new byte[] { 0, 3, 0, 0 }));

    [Fact]
    public void An_overstated_length_parses_without_running_off_the_buffer()
    {
        var bytes = Fixture("smbios-KOR-SPARE100.hex");
        BitConverter.GetBytes((uint)(bytes.Length * 4)).CopyTo(bytes, 4);
        Assert.NotNull(SmbiosParser.Parse(bytes).Board);
    }

    [Fact]
    public void A_corrupt_structure_length_halts_the_walk_instead_of_inventing_dimms()
    {
        var bytes = Fixture("smbios-KOR-SPARE100.hex");
        bytes[9] = 1;   // first structure claims a 1-byte formatted area, shorter than its own header
        Assert.Empty(SmbiosParser.Parse(bytes).Memory.Dimms);
    }

    // An 8-byte SMBIOS header (version 3.0) wrapping one or more raw structures (each including its own string terminator).
    private static byte[] Blob(params byte[][] structs)
    {
        var body = structs.SelectMany(s => s).ToArray();
        var blob = new byte[8 + body.Length];
        blob[1] = 3;   // version major -> "3.0"
        BitConverter.GetBytes((uint)body.Length).CopyTo(blob, 4);
        body.CopyTo(blob, 8);
        return blob;
    }

    [Fact]
    public void A_memory_device_too_short_for_its_size_field_is_an_empty_slot_not_a_crash()
    {
        // Type 17, formatted area 4 bytes: passes flen>=4 but has no size field at 0x0C. Must not throw out of the whole
        // fleet's hardware run (Cli calls Parse with no catch).
        byte[] mem = [0x11, 0x04, 0x00, 0x00, 0x00, 0x00];   // type 17, flen 4, handle, empty string set (00 00)
        byte[] eot = [0x7F, 0x04, 0x00, 0x00, 0x00, 0x00];   // end-of-table
        var info = SmbiosParser.Parse(Blob(mem, eot));
        Assert.Single(info.Memory.Dimms);
        Assert.Equal(0, info.Memory.Dimms[0].SizeMB);
    }

    [Fact]
    public void A_chassis_too_short_for_its_type_byte_is_unknown_not_a_crash()
    {
        // Type 3, formatted area 4 bytes: passes flen>=4 but has no type byte at 0x05.
        byte[] chassis = [0x03, 0x04, 0x00, 0x00, 0x00, 0x00];
        byte[] eot = [0x7F, 0x04, 0x00, 0x00, 0x00, 0x00];
        var info = SmbiosParser.Parse(Blob(chassis, eot));
        Assert.Equal(0, info.Chassis!.TypeCode);
    }
}
