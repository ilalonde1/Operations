#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The "This PC" tiles on a PC's page (Ian, 2026-10-02: "something more graphical representing the key components of the
// PC"), from KOR-208-N's real probe-v10 check of that afternoon and the findings open on it then.
//
// WHAT IT COVERS: which parts get a tile and in what order; the words on each (CPU and GPU names shortened, memory
// layout, drive role/letters/kind/fill); that a finding colours exactly the part it is about -- the failing D: drive and
// nothing else, the BIOS update on the BIOS tile -- and that a finding about no part colours nothing; the per-letter
// fallback for a check from before probe v10.
// WHAT IT DOES NOT: how the tiles look (NetworkOpsWindowsRenderTests draws the window), or rack devices (no tiles).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a new finding family that IS about a part but is not listed in PcComponents
// -- it shows in the list and colours no tile; the list of families is the thing to extend.
public sealed class PcComponentsTests
{
    private static HealthSnapshot Kor208N()
        => HealthSnapshot.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "health", "KOR-208-N-v10-2026-10-02.json")));

    private static readonly (string, Severity)[] Open208N =
    [
        ("disk-errors:st2000dm006-2dm164", Severity.Critical),
        ("crash-loop:opushutil.exe", Severity.Warning),
        ("unbacked-data", Severity.Warning),
        ("bios-behind", Severity.Info),
        ("memory-layout", Severity.Info),
    ];

    [Fact]
    public void KOR_208_N_shows_its_parts_in_order_with_their_words()
    {
        var c = PcComponents.Of(Kor208N(), Open208N);
        Assert.Equal(["cpu", "memory", "gpu", "drive", "drive", "windows", "bios"], c.Select(x => x.Kind));
        Assert.Equal("Core i7-13700", c[0].Line1);
        Assert.Equal("96 GB", c[1].Line1);
        Assert.Equal("NVIDIA T400 4GB", c[2].Line1);                    // the discrete card, not the Intel iGPU or the RDP adapter
        Assert.Equal(("C:  System", "512 GB NVMe SSD"), (c[3].Title, c[3].Line1));
        Assert.True(c[3].IsSystem);
        Assert.Equal(("D:  Data", "2 TB hard drive", "ST2000DM006-2DM164"), (c[4].Title, c[4].Line1, c[4].Line2));
        Assert.InRange(c[4].FillPct!.Value, 0, 100);
        Assert.Equal("11 25H2", c[5].Line1);
        Assert.Equal("S0IKT7CA", c[6].Line1);
    }

    [Fact]
    public void A_finding_colours_only_the_part_it_is_about()
    {
        var c = PcComponents.Of(Kor208N(), Open208N).ToList();
        var d = c.Single(x => x.Title.StartsWith("D:", StringComparison.Ordinal));
        Assert.Equal((Severity?)Severity.Critical, d.Worst);
        Assert.Equal(["disk-errors:st2000dm006-2dm164"], d.RuleKeys);
        Assert.Null(c.Single(x => x.IsSystem).Worst);                                    // the healthy SSD stays clear
        Assert.Equal((Severity?)Severity.Info, c.Single(x => x.Kind == "bios").Worst);
        Assert.Equal((Severity?)Severity.Info, c.Single(x => x.Kind == "memory").Worst);
        Assert.Null(c.Single(x => x.Kind == "gpu").Worst);
        // A crashing app and unbacked data are about no part: they colour nothing.
        Assert.DoesNotContain(c, x => x.RuleKeys.Contains("crash-loop:opushutil.exe") || x.RuleKeys.Contains("unbacked-data"));
    }

    [Fact]
    public void A_full_drive_colours_the_drive_its_letter_is_on()
    {
        var c = PcComponents.Of(Kor208N(), [("low-disk:d", Severity.Critical)]);
        Assert.Equal(["low-disk:d"], c.Single(x => x.Title.StartsWith("D:", StringComparison.Ordinal)).RuleKeys);
    }

    [Fact]
    public void Before_probe_v10_each_letter_is_a_tile_and_no_drive_finding_is_guessed_onto_one()
    {
        var old = HealthSnapshot.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "health", "KOR-216.json")));
        var drives = PcComponents.Of(old, [("disk-errors:anything", Severity.Critical)]).Where(x => x.Kind == "drive").ToList();
        Assert.NotEmpty(drives);
        Assert.All(drives, d => Assert.Null(d.Worst));
        Assert.Contains(drives, d => d.Title == "C:" && d.IsSystem);
    }

    [Theory]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700", "Core i7-13700")]
    [InlineData("Intel(R) Xeon(R) W-2245 CPU @ 3.90GHz", "Xeon W-2245")]
    [InlineData("Intel(R) Core(TM) i9-10900K CPU @ 3.70GHz", "Core i9-10900K")]
    [InlineData("AMD Ryzen 9 5950X 16-Core Processor", "Ryzen 9 5950X 16-Core")]
    public void A_CPU_name_is_shortened_to_what_people_call_it(string raw, string expected) => Assert.Equal(expected, PcComponents.ShortCpu(raw));
}
