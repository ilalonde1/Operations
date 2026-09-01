using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Asked what a section is doing in her model, the answer must name every storey it is on.
/// </summary>
/// <remarks>
/// `takeoff e2k-ask … sections` reported the storey of an object's FIRST assign, which was the same
/// answer only while one object meant one member on one storey. Once a member carries one label its
/// whole height — the engineer's own convention — a section used on twelve floors was reported as
/// used on the lowest, and she is told a twelfth of it.
///
/// Ninth of the same class. The row already carried its storey; the code reached past it.
/// </remarks>
public class SectionsReportEveryStoreyTests
{
    private static E2kDocument Model() => E2kDocument.Parse(new[]
    {
        "$ STORIES - IN SEQUENCE FROM TOP",
        "  STORY \"LEVEL 3\"  HEIGHT 120",
        "  STORY \"LEVEL 2\"  HEIGHT 120",
        "  STORY \"LEVEL 1\"  HEIGHT 120",
        "  STORY \"Base\"  HEIGHT 0",
        "$ FRAME SECTIONS",
        "  FRAMESECTION  \"KOR-C24x24\"  MATERIAL \"30 MPa\"  SHAPE \"Concrete Rectangular\"  D 24 B 24",
        "  FRAMESECTION  \"KOR-C18x18\"  MATERIAL \"30 MPa\"  SHAPE \"Concrete Rectangular\"  D 18 B 18",
        "$ POINT COORDINATES",
        "  POINT \"KP1\"  0  0",
        "$ LINE CONNECTIVITIES",
        "  LINE  \"KC1\"  COLUMN  \"KP1\"  \"KP1\"  1",
        "$ LINE ASSIGNS",
        // One label, an assign per storey — and it changes size as it rises.
        "  LINEASSIGN  \"KC1\"  \"LEVEL 1\"  SECTION \"KOR-C24x24\"",
        "  LINEASSIGN  \"KC1\"  \"LEVEL 2\"  SECTION \"KOR-C24x24\"",
        "  LINEASSIGN  \"KC1\"  \"LEVEL 3\"  SECTION \"KOR-C18x18\"",
    });

    [Fact]
    public void ASectionUsedOnSeveralStoreysNamesThemAll()
    {
        var sections = E2kModelQuery.Sections(Model());

        var big = Assert.Single(sections, s => s.Section == "KOR-C24x24");
        Assert.Equal(2, big.Used);
        Assert.Contains("LEVEL 1", big.Storeys, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEVEL 2", big.Storeys, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And the section a member steps UP to is reported on the storey it is actually on — not on
    /// the bottom one, which is where the object's first assign happens to be.
    /// </summary>
    [Fact]
    public void TheSectionAMemberStepsUpToIsReportedOnItsOwnStorey()
    {
        var sections = E2kModelQuery.Sections(Model());

        var small = Assert.Single(sections, s => s.Section == "KOR-C18x18");
        Assert.Equal(1, small.Used);
        Assert.Contains("LEVEL 3", small.Storeys, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LEVEL 1", small.Storeys, StringComparison.OrdinalIgnoreCase);
    }
}
