#nullable enable
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The printers (Ian, 2026-10-02: "Firmware on those?"), on what the five answered over SNMP that night
// (Fixtures/rack/printers-2026-10-02.json, SNMP v1 "public", read-only).
//
// WHAT IT COVERS: model and firmware as each maker reports them (Canon after the model, Brother "Firmware Ver.", HP
// "EEPROM V."); page count; each supply's percentage left; a supply at 0 is a finding worded for what it is (ink empty,
// drum at end of life, waste full); "-3 some remains" / "-2 unknown" are no finding; the printer's own error flags.
// The night's facts: the plotter's four inks at 0 and its low-ink flag; the HL-5450DN's drum at end of life; the C5840
// healthy with 11 supplies; the HL-L6200DW and the LaserJet with toner levels not reported, so nothing raised.
// WHAT IT DOES NOT: SNMP itself (SnmpChannel.GetV1Async), or a printer that answers nothing (Unreachable, tested by shape).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a maker that reports a waste bin's level as "used" instead of "room left" -- its
// full bin would read as empty and raise nothing.
public sealed class PrinterRulesTests
{
    private static Dictionary<string, Dictionary<string, string>> All()
        => JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rack", "printers-2026-10-02.json")))!;

    private static RackResult Read(string ip) => PrinterRules.Evaluate(All()[ip]);

    [Fact]
    public void The_plotters_inks_at_zero_are_empty_and_its_own_flag_says_low_ink()
    {
        var r = Read("192.168.1.5");
        Assert.Equal("Canon TZ-30000", r.Facts["hw.model"]);
        Assert.Equal(4, r.Findings.Count(f => f.RuleKey.StartsWith("printer.supply:", StringComparison.Ordinal) && f.Severity == Severity.Warning));
        Assert.Contains(r.Findings, f => f.Title == "CANON Cyan Ink Tank is empty");
        Assert.Contains(r.Findings, f => f.RuleKey == "printer.low-toner" && f.Severity == Severity.Info);
        Assert.DoesNotContain(r.Findings, f => f.Title.Contains("Waste", StringComparison.Ordinal));      // 8000 of 10000 room left
        Assert.Equal(60, r.Metrics.Single(m => m.Metric == "supply.pct" && m.Subject == "CANON Black Ink Tank").Value);
    }

    [Fact]
    public void A_drum_at_zero_is_at_end_of_life_and_old_firmware_is_read()
    {
        var r = Read("192.168.1.156");
        Assert.Equal("1.17 (15.12.01)", r.Facts["fw.version"]);
        var drum = Assert.Single(r.Findings);
        Assert.Equal(("printer.supply:Drum Unit", "Drum Unit is at end of life", Severity.Warning), (drum.RuleKey, drum.Title, drum.Severity));
    }

    [Fact]
    public void The_copier_is_healthy_and_toner_not_reported_is_not_a_finding()
    {
        var c5840 = Read("192.168.1.8");
        Assert.Equal(("Canon iR-ADV C5840", "39.13"), (c5840.Facts["hw.model"], c5840.Facts["fw.version"]));
        Assert.Empty(c5840.Findings);
        Assert.Equal(11, c5840.Metrics.Count(m => m.Metric == "supply.pct"));
        Assert.Equal(63323, c5840.Metrics.Single(m => m.Metric == "pages.total").Value);

        Assert.Empty(Read("192.168.1.14").Findings);                      // Brother: toner -3, "some remains"
        var hp = Read("192.168.1.220");
        Assert.Empty(hp.Findings);                                       // HP: toner -2, "unknown"
        Assert.Equal("33.30", hp.Facts["fw.version"]);
    }

    // The rack rules' explanation gate scans for Raise("literal"); the flags are raised from a table, so they are held here.
    [Fact]
    public void Every_printer_flag_and_the_supplies_have_a_written_explanation()
    {
        Assert.All(PrinterRules.Flags, f => Assert.NotNull(Kor.Operations.NetworkOps.Core.Learning.Knowledge.For(f.Key)));
        Assert.NotNull(Kor.Operations.NetworkOps.Core.Learning.Knowledge.For("printer.supply:Drum Unit"));
    }

    [Fact]
    public void Each_error_flag_is_read_from_the_bits_the_printer_sends()
    {
        var v = new Dictionary<string, string> { [PrinterRules.SysDescr] = "x", [PrinterRules.ErrorState] = "0c00" };   // jammed + door open
        var keys = PrinterRules.Evaluate(v).Findings.Select(f => f.RuleKey).Order().ToList();
        Assert.Equal(["printer.door-open", "printer.jammed"], keys);
        Assert.False(PrinterRules.Evaluate(new Dictionary<string, string>()).Reachable);
    }
}
