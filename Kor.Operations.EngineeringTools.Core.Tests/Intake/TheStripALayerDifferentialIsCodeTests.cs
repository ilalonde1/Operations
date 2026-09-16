#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The strip-a-layer differential's first half is code (<see cref="DxfLayerStrip"/>, `dxf-strip`; 2026-09-15 it was
/// a scratch script that settled 31138's lost columns in three minutes). WHAT THIS COVERS: entities on a matching
/// layer move to X-STRIPPED, other layers and the TABLES section are untouched, CRLF survives, the count is right,
/// and a folder copy strips only the sheets the glob names. WHAT IT DOES NOT: composing the two folders (that is
/// dxf-to-etabs, gated by the six sets); a layer named on a non-entity group code.
/// </summary>
public sealed class TheStripALayerDifferentialIsCodeTests
{
    private const string Dxf =
        "0\r\nSECTION\r\n2\r\nTABLES\r\n0\r\nTABLE\r\n2\r\nLAYER\r\n0\r\nLAYER\r\n2\r\nKOR_C_SLABEDG\r\n0\r\nENDTAB\r\n0\r\nENDSEC\r\n" +
        "0\r\nSECTION\r\n2\r\nENTITIES\r\n" +
        "0\r\nLINE\r\n8\r\nKOR_C_SLABEDG\r\n10\r\n0\r\n20\r\n0\r\n11\r\n100\r\n21\r\n0\r\n" +
        "0\r\nLINE\r\n8\r\nKOR_C_WALL\r\n10\r\n0\r\n20\r\n0\r\n11\r\n0\r\n21\r\n100\r\n" +
        "0\r\nLWPOLYLINE\r\n8\r\nKOR_C_SLABEDG_EXT\r\n90\r\n2\r\n10\r\n0\r\n20\r\n0\r\n10\r\n1\r\n20\r\n1\r\n" +
        "0\r\nENDSEC\r\n0\r\nEOF\r\n";

    [Fact]
    public void OnlyEntitiesOnTheNamedLayerMoveAndTheTablesSectionStays()
    {
        var (text, n) = DxfLayerStrip.StripText(Dxf, ["SLABEDG"]);

        Assert.Equal(2, n);
        Assert.Contains("8\r\nX-STRIPPED\r\n10\r\n0\r\n20\r\n0\r\n11\r\n100", text, StringComparison.Ordinal);      // the slab edge line
        Assert.Contains("8\r\nKOR_C_WALL\r\n", text, StringComparison.Ordinal);                                     // the wall untouched
        Assert.Contains("2\r\nLAYER\r\n0\r\nLAYER\r\n2\r\nKOR_C_SLABEDG\r\n", text, StringComparison.Ordinal);    // the layer table untouched
        Assert.DoesNotContain("8\r\nKOR_C_SLABEDG", text, StringComparison.Ordinal);
        Assert.Equal(Dxf.Length - 2 * "KOR_C_SLABEDG".Length - "_EXT".Length + 2 * "X-STRIPPED".Length, text.Length);   // nothing else changed
    }

    [Fact]
    public void AFolderCopyStripsTheSheetsTheGlobNamesAndCopiesTheRest()
    {
        string src = Path.Combine(Path.GetTempPath(), "kor-tests", "strip-src-" + Guid.NewGuid().ToString("N")[..8]);
        string dst = src + "-out";
        Directory.CreateDirectory(src);
        try
        {
            File.WriteAllText(Path.Combine(src, "S2.01_1_LEVEL 1.dxf"), Dxf);
            File.WriteAllText(Path.Combine(src, "S2.02_1_LEVEL 2.dxf"), Dxf);
            File.WriteAllText(Path.Combine(src, "levels.csv"), "storey,elev\nL1,0\n");

            var rows = DxfLayerStrip.Strip(src, dst, ["SLABEDG"], "S2.02*.dxf");

            Assert.Equal(2, rows.Count);
            Assert.Equal(0, rows.Single(r => r.File.StartsWith("S2.01", StringComparison.Ordinal)).Stripped);
            Assert.Equal(2, rows.Single(r => r.File.StartsWith("S2.02", StringComparison.Ordinal)).Stripped);
            Assert.Equal(Dxf, File.ReadAllText(Path.Combine(dst, "S2.01_1_LEVEL 1.dxf")));
            Assert.True(File.Exists(Path.Combine(dst, "levels.csv")));
        }
        finally
        {
            Directory.Delete(src, recursive: true);
            if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
        }
    }
}
