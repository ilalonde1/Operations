using Kor.Operations.EngineeringTools.Dxf;
using System.Globalization;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public class MemberPlanStoreyMultisetPreservedTests
{
    [Fact]
    public void RenameOnlyChangePasses()
    {
        var before = Model(
            Points(("P1", 10, 20)),
            "$ LINE CONNECTIVITIES",
            "  LINE \"KC1\"  COLUMN  \"P1\"  \"P1\"  1",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC1\"  \"LEVEL 1\"  SECTION \"24x24\"");

        var after = Model(
            Points(("P1", 10, 20)),
            "$ LINE CONNECTIVITIES",
            "  LINE \"KC99\"  COLUMN  \"P1\"  \"P1\"  1",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC99\"  \"LEVEL 1\"  SECTION \"24x24\"");

        MemberPlanStoreyMultisetPreserved.Assert(before, after);
    }

    [Fact]
    public void AddedAssignFailsAndNamesStorey()
    {
        var before = ColumnModel(("KC1", "P1", 0, 0, new[] { "LEVEL 1" }));
        var after = ColumnModel(("KC1", "P1", 0, 0, new[] { "LEVEL 1", "LEVEL 2" }));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MemberPlanStoreyMultisetPreserved.Assert(before, after));

        Assert.Contains("LEVEL 2", ex.Message);
        Assert.Contains("columns 0 -> 1", ex.Message);
        Assert.Contains("gained", ex.Message);
    }

    [Fact]
    public void DroppedAssignFails()
    {
        var before = ColumnModel(("KC1", "P1", 0, 0, new[] { "LEVEL 1", "LEVEL 2" }));
        var after = ColumnModel(("KC1", "P1", 0, 0, new[] { "LEVEL 1" }));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MemberPlanStoreyMultisetPreserved.Assert(before, after));

        Assert.Contains("LEVEL 2", ex.Message);
        Assert.Contains("columns 1 -> 0", ex.Message);
        Assert.Contains("lost", ex.Message);
    }

    [Fact]
    public void MovedMemberFails()
    {
        var before = ColumnModel(("KC1", "P1", 0, 0, new[] { "LEVEL 1" }));
        var after = ColumnModel(("KC1", "P1", 0.001, 0, new[] { "LEVEL 1" }));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MemberPlanStoreyMultisetPreserved.Assert(before, after));

        Assert.Contains("LEVEL 1", ex.Message);
        Assert.Contains("columns 1 -> 1", ex.Message);
        Assert.Contains("lost", ex.Message);
        Assert.Contains("gained", ex.Message);
    }

    [Fact]
    public void SectionChangeUpStackPasses()
    {
        var before = ColumnModel(
            ("KC1", "P1", 0, 0, new[] { "LEVEL 1" }),
            ("KC2", "P2", 0, 0, new[] { "LEVEL 2" }));

        var after = Model(
            Points(("P1", 0, 0)),
            "$ LINE CONNECTIVITIES",
            "  LINE \"KC9\"  COLUMN  \"P1\"  \"P1\"  1",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC9\"  \"LEVEL 1\"  SECTION \"24x24\"",
            "  LINEASSIGN  \"KC9\"  \"LEVEL 2\"  SECTION \"18x18\"");

        MemberPlanStoreyMultisetPreserved.Assert(before, after);
    }

    [Fact]
    public void ObjectCountCanFallWhenAssignmentsStayTheSame()
    {
        var before = ColumnModel(
            ("KC1", "P1", 0, 0, new[] { "LEVEL 1" }),
            ("KC2", "P2", 0, 0, new[] { "LEVEL 2" }),
            ("KC3", "P3", 0, 0, new[] { "LEVEL 3" }));

        var after = Model(
            Points(("P1", 0, 0)),
            "$ LINE CONNECTIVITIES",
            "  LINE \"KC9\"  COLUMN  \"P1\"  \"P1\"  1",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC9\"  \"LEVEL 1\"  SECTION \"24x24\"",
            "  LINEASSIGN  \"KC9\"  \"LEVEL 2\"  SECTION \"24x24\"",
            "  LINEASSIGN  \"KC9\"  \"LEVEL 3\"  SECTION \"24x24\"");

        var comparison = MemberPlanStoreyMultisetPreserved.Compare(before, after);

        Assert.True(comparison.Preserved);
        Assert.Equal(3, MemberPlanStoreyMultisetPreserved.Capture(before).ObjectCount);
        Assert.Equal(1, MemberPlanStoreyMultisetPreserved.Capture(after).ObjectCount);
    }

    [Fact]
    public void WallFootprintEndpointOrderIsNormalised()
    {
        var before = Model(
            Points(("A", 0, 0), ("B", 100, 0), ("C", 100, 12), ("D", 0, 12)),
            "$ AREA CONNECTIVITIES",
            "  AREA \"KW1\"  PANEL  4  \"A\"  \"B\"  \"C\"  \"D\"  1 1 0 0",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KW1\"  \"LEVEL 1\"  SECTION \"W12\"");

        var after = Model(
            Points(("A", 0, 0), ("B", 100, 0), ("C", 100, 12), ("D", 0, 12)),
            "$ AREA CONNECTIVITIES",
            "  AREA \"KW9\"  PANEL  4  \"D\"  \"C\"  \"B\"  \"A\"  1 1 0 0",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KW9\"  \"LEVEL 1\"  SECTION \"W12\"");

        MemberPlanStoreyMultisetPreserved.Assert(before, after);
    }

    [Fact]
    public void FinishedFilesCanBeCompared()
    {
        string beforePath = Path.Combine(Path.GetTempPath(), "kor-stack-gate-before-" + Guid.NewGuid().ToString("N") + ".e2k");
        string afterPath = Path.Combine(Path.GetTempPath(), "kor-stack-gate-after-" + Guid.NewGuid().ToString("N") + ".e2k");

        ColumnModel(("KC1", "P1", 0, 0, new[] { "LEVEL 1" })).Save(beforePath);
        ColumnModel(("KC99", "P9", 0, 0, new[] { "LEVEL 1" })).Save(afterPath);

        var comparison = MemberPlanStoreyMultisetPreserved.Compare(beforePath, afterPath);

        Assert.True(comparison.Preserved);
    }

    private static E2kDocument ColumnModel(params (string Object, string Point, double X, double Y, string[] Storeys)[] columns)
    {
        var lines = new List<string>();
        lines.AddRange(Points(columns.Select(c => (c.Point, c.X, c.Y)).ToArray()));
        lines.Add("$ LINE CONNECTIVITIES");
        lines.AddRange(columns.Select(c => $"  LINE \"{c.Object}\"  COLUMN  \"{c.Point}\"  \"{c.Point}\"  1"));
        lines.Add("$ LINE ASSIGNS");
        foreach (var column in columns)
        foreach (string storey in column.Storeys)
            lines.Add($"  LINEASSIGN  \"{column.Object}\"  \"{storey}\"  SECTION \"24x24\"");
        return Model(lines.ToArray());
    }

    private static string[] Points(params (string Name, double X, double Y)[] points)
        => new[] { "$ POINT COORDINATES" }
            .Concat(points.Select(p =>
                $"  POINT \"{p.Name}\"  {p.X.ToString("R", CultureInfo.InvariantCulture)} {p.Y.ToString("R", CultureInfo.InvariantCulture)}"))
            .ToArray();

    // Points first, then the rest: a params array cannot take an array in the first position
    // alongside further arguments, and every fixture builds its points with Points(...) and then
    // appends literal connectivity and assign lines. ColumnModel passes one array and no body.
    private static E2kDocument Model(string[] points, params string[] body)
        => E2kDocument.Parse(new[]
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 3\"  HEIGHT 120",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"LEVEL 1\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
        }.Concat(points).Concat(body));
}
