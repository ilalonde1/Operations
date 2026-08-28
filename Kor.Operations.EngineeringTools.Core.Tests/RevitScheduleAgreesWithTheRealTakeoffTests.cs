using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The importer against the firm's own verified takeoff.
///
/// 31065 was taken off by hand and reconciled — "31065 - REAL Concrete Delta (ALL elements).xlsx" is
/// that answer, per level and element. Reading the four raw Revit exports must reproduce it. This is
/// the doctrine's three-set gate applied to the one set that has a key: a change that quietly moves
/// a number fails here rather than in a bid.
///
/// Skipped when the Desktop sources are not on this machine, like every other test that needs files
/// it does not own.
/// </summary>
public sealed class RevitScheduleAgreesWithTheRealTakeoffTests
{
    private readonly ITestOutputHelper _out;
    public RevitScheduleAgreesWithTheRealTakeoffTests(ITestOutputHelper output) => _out = output;

    private static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string Sources => Path.Combine(Desktop, "Rory", "_source-csv");
    private static string Key => Path.Combine(Desktop, "Rory", "_archive", "31065 - REAL Concrete Delta (ALL elements).xlsx");

    [Fact]
    public void ReadingTheRawRevitExportsReproducesTheVerified31065Takeoff()
    {
        var exports = Directory.Exists(Sources)
            ? Directory.EnumerateFiles(Sources, "*-after.csv").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();

        if (exports.Count == 0 || !File.Exists(Key)) { _out.WriteLine("SKIPPED: 31065 sources or answer key not on this machine."); return; }

        var got = RevitScheduleImporter.Import(exports.Select(f => (Path.GetFileName(f), File.ReadAllText(f))))
            .Inputs
            .GroupBy(i => (Level: i.Level.ToUpperInvariant(), i.Element))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.ConcreteVolume));

        // The key's columns: Level, Element, …, IFC (current) concrete in column 7.
        var want = new Dictionary<(string Level, TakeoffElementType Element), double>();
        using (var wb = new XLWorkbook(Key))
        {
            var ws = wb.Worksheet("Delta");
            foreach (var row in ws.RowsUsed())
            {
                string level = row.Cell(1).GetString().Trim();
                if (level.Length == 0) continue;
                if (!Enum.TryParse<TakeoffElementType>(row.Cell(2).GetString().Trim(), ignoreCase: true, out var element)) continue;
                if (!row.Cell(7).TryGetValue(out double ifc)) continue;
                want[(level.ToUpperInvariant(), element)] = ifc;
            }
        }

        Assert.True(want.Count > 20, $"the answer key parsed as only {want.Count} rows — its shape has changed.");

        var wrong = new List<string>();
        foreach (var (k, expected) in want.OrderBy(k => k.Key.Level, StringComparer.OrdinalIgnoreCase))
        {
            if (!got.TryGetValue(k, out double actual))
            {
                if (expected > 0.05) wrong.Add($"{k.Level} {k.Element}: the key has {expected:N1} and we produced no row at all.");
                continue;
            }
            if (Math.Abs(actual - expected) > 0.15)
                wrong.Add($"{k.Level} {k.Element}: key {expected:N1}, ours {actual:N1}, out by {Math.Abs(actual - expected):N1}.");
        }

        double compared = want.Keys.Where(got.ContainsKey).Sum(k => want[k]);
        _out.WriteLine($"{want.Count} answer-key rows, {got.Count} produced, {want.Keys.Count(got.ContainsKey)} comparable, {compared:N1} m³ matched.");

        // Rows we find that the key does not carry — roofs and the slab-on-grade walls. Reported so
        // the extra concrete is a decision rather than a surprise; the key is scoped, not wrong.
        foreach (var extra in got.Keys.Where(k => !want.ContainsKey(k)).OrderByDescending(k => got[k]))
            _out.WriteLine($"   not in the key: {extra.Level} {extra.Element} {got[extra]:N1} m³");

        Assert.True(wrong.Count == 0,
            "Reading the raw Revit exports no longer reproduces the verified 31065 takeoff:\n  " +
            string.Join("\n  ", wrong));
    }
}
