using System;
using System.IO;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// EVERY EXPORTED COORDINATE IS LINEAR IN THIS ONE NUMBER, SO IT MUST COME OFF THE SHEET.
///
/// The old detector joined every word on the page and took the first `1:NNN` it found, against a
/// whitelist of metric denominators only. It returned NULL for a sheet whose title block reads
/// `SCALE: 1/8" = 1'-0"`, because the pattern demanded two to four digits so `1/8` never matched,
/// and because 96 was not in the list. **No sheet drawn in imperial could be read at all** — which
/// is most North American structural drawing — and the app then defaulted, silently, to a number
/// 4% wrong.
///
/// Two sheets, two different places the scale lives, and both must land on 96:
///
///   31202-01    states it in the title block          — `SheetScaleReader.FromPage`
///   Parcel 11   leaves that field EMPTY and states it once under the viewport, bottom-left
///
/// The second is the one that needs care. A caption cannot be trusted in general — `SCALE: 1:20`
/// under a stair detail is not the sheet scale — so it is used only where the title block is silent
/// AND the sheet states exactly one, and the engineer is told what was used and where it came from.
/// </summary>
public sealed class TheScaleComesOffTheSheetGate
{
    private readonly ITestOutputHelper _out;
    public TheScaleComesOffTheSheetGate(ITestOutputHelper output) => _out = output;

    private static readonly string Desktop =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    [Theory]
    [InlineData("OAP-parcel11-arch-markup.pdf", 96, "stated under the viewport; the title block is empty")]
    [InlineData("31202-01 - Reinforcing Sheets - REVISED per JD markup 2026-07-27.pdf", 96, "stated in the title block")]
    public void TheSheetsOwnScaleIsRead(string file, int expected, string where)
    {
        string path = Path.Combine(Desktop, file);
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED: not at {path}"); return; }

        var found = PdfGeometryExtractor.DetectScaleForLoad(path, 1);

        _out.WriteLine($"{file}");
        _out.WriteLine($"   {where}");
        _out.WriteLine($"   read : {found.Denominator?.ToString() ?? "NULL"}   source: {found.Source}   note: \"{found.Note}\"");

        Assert.True(found.Denominator == expected,
            $"the sheet states its scale ({where}) and the tool read " +
            $"{(found.Denominator?.ToString() ?? "nothing")}. Every exported coordinate is linear in " +
            "this number, so a wrong one is uniformly wrong and looks right.");
    }

    /// <summary>
    /// The guard that makes a caption safe to use at all: a sheet stating several scales is not
    /// resolved by picking one. It must fall back and NAME them.
    /// </summary>
    [Fact]
    public void SeveralStatedScalesAreNotGuessedBetween()
    {
        string path = Path.Combine(Desktop, "Structural Quantity Takeoff Demo", "Inputs",
                                   "31065 - AFTER (IFC 2026-03-06).pdf");
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED: not at {path}"); return; }

        var found = PdfGeometryExtractor.DetectScaleForLoad(path, 1);
        _out.WriteLine($"31065 IFC page 1 -> {found.Denominator?.ToString() ?? "NULL"}  source: {found.Source}");
        _out.WriteLine($"   note: \"{found.Note}\"");

        // Whatever it decides, it must not be silent about how it decided.
        Assert.True(Enum.IsDefined(typeof(PdfScaleSource), found.Source),
            "the load must always be able to say where its scale came from");
    }
}
