#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// The facts the engineer banked against a job — which storeys to join on a match line, how many
/// slabs a storey carries — are matched on the job the drawings belong to, and the job is what the
/// inputs say it is: given, or the five-digit number in the stick file's, DXF folder's, reference's
/// or output's name, the first found. Read from the output's name alone (Codex 31, F1) a run named
/// out.e2k matched no row and joined every split plan, LEVEL 1 of 31168 included.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the number in a file's name, in a folder on the path, in the reference's name;
/// the order of precedence; no number anywhere. WHAT IT DOES NOT: the service's own use of it
/// against banked rows (the five-set and Revit builds are the measurement); a job numbered
/// otherwise than five digits.
/// </remarks>
public sealed class TheJobIsWhatTheInputsSayItIsTests
{
    [Theory]
    [InlineData(@"C:\drawings\31168-01.pdf", "31168")]
    [InlineData(@"C:\Temp\kor-drawings\harness\pdf-only-31065-01\dxf", "31065")]
    [InlineData(@"C:\Temp\converge-31168-p14\31168-reference.e2k", "31168")]
    [InlineData("out.e2k", null)]
    [InlineData(@"C:\jobs\2026\123456\out.e2k", null)]
    public void TheFiveDigitNumberOnThePathIsTheJob(string path, string? expected)
        => Assert.Equal(expected, DxfToEtabsService.JobNumberIn(path));

    [Fact]
    public void TheStickFileOutranksTheFolderWhichOutranksTheOutput()
    {
        Assert.Equal("31168", DxfToEtabsService.JobNumberIn(@"C:\x\31168-01.pdf", @"C:\pdf-only-31065-01\dxf", "31202-reference.e2k", "31130.e2k"));
        Assert.Equal("31065", DxfToEtabsService.JobNumberIn(null, @"C:\pdf-only-31065-01\dxf", "31202-reference.e2k", "31130.e2k"));
        Assert.Equal("31202", DxfToEtabsService.JobNumberIn(null, @"C:\dxf", "31202-reference.e2k", "31130.e2k"));
        Assert.Equal("31130", DxfToEtabsService.JobNumberIn(null, @"C:\dxf", "", "31130.e2k"));
        Assert.Null(DxfToEtabsService.JobNumberIn(null, @"C:\dxf", "", "out.e2k"));
    }
}
