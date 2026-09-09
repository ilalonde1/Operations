#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Reissue Impact, the sheet comparison (foundation §3.1): what changed on one sheet between two
/// issues, as objects, with the new issue set on the old one's grid by name first.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a column moved past two inches, one within it, one added, one removed, one
/// resized; a wall lengthened and one gone; a footing re-marked; a grid axis moved; a storey
/// changed; a schedule cell rewritten and a row added; and the whole new sheet shifted on its page
/// with the grid shared, which is no change at all. WHAT IT DOES NOT: two real issues of a job —
/// the set-diff verb on 31130's May and September issues is that measurement — and a sheet whose
/// grid was renumbered between issues, which reads as removed and added axes.
/// </remarks>
public sealed class AReissueIsWhatMovedTests
{
    private static SheetObjects Sheet(
        IReadOnlyList<(double X, double Y)>? columns = null, IReadOnlyList<(double, double)>? sizes = null,
        IReadOnlyList<WallPanel>? walls = null, IReadOnlyList<FootingOutline>? footings = null,
        IReadOnlyList<GridAxis>? grid = null, IReadOnlyList<StoreyLadder.Storey>? storeys = null,
        IReadOnlyList<ScheduleTable>? schedules = null)
        => new("S2.01", 11, "plan", "P3",
            columns ?? Array.Empty<(double, double)>(),
            sizes ?? (columns ?? Array.Empty<(double, double)>()).Select(_ => (600.0, 600.0)).ToList(),
            walls ?? Array.Empty<WallPanel>(), footings ?? Array.Empty<FootingOutline>(),
            grid ?? Array.Empty<GridAxis>(), storeys ?? Array.Empty<StoreyLadder.Storey>(), schedules ?? Array.Empty<ScheduleTable>());

    private static WallPanel Wall(double x0, double y0, double x1, double y1, double t = 300)
    {
        double dx = x1 - x0, dy = y1 - y0, l = Math.Sqrt(dx * dx + dy * dy), nx = -dy / l * t / 2, ny = dx / l * t / 2;
        return new WallPanel(new List<(double, double)> { (x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny) }, (x0, y0), (x1, y1), t);
    }

    private static FootingOutline Footing(string mark, double x, double y, double size = 2000)
        => new(mark, new List<(double, double)> { (x - size / 2, y - size / 2), (x + size / 2, y - size / 2), (x + size / 2, y + size / 2), (x - size / 2, y + size / 2) }, (x, y), size, size, 0);

    [Fact]
    public void ColumnsMovedAddedRemovedAndResizedAreEachNamed()
    {
        var old = Sheet(columns: [(1000, 1000), (5000, 1000), (9000, 1000), (13000, 1000)], sizes: [(600, 600), (600, 600), (600, 600), (600, 600)]);
        var @new = Sheet(columns: [(1030, 1000), (5000, 1400), (9000, 1000), (20000, 1000)], sizes: [(600, 600), (600, 600), (900, 600), (600, 600)]);
        var d = SheetDiff.Compare(old, @new);
        Assert.Equal(1, d.ColumnsSame);                                  // the first: 30 mm is drafting, not a move
        Assert.Single(d.ColumnsMoved);
        Assert.Equal(400, d.ColumnsMoved[0].DistanceMm, 0);
        Assert.Single(d.ColumnsResized);
        Assert.Equal((9000.0, 1000.0), d.ColumnsResized[0].At);
        Assert.Single(d.ColumnsAdded);
        Assert.Single(d.ColumnsRemoved);
        Assert.Equal((13000.0, 1000.0), d.ColumnsRemoved[0]);
        Assert.Equal(4, d.Changes);
        Assert.Contains(d.Lines(), l => l.StartsWith("column moved 400 mm"));
    }

    [Fact]
    public void AWallLengthenedIsTheSameWallChangedAndAWallGoneIsRemoved()
    {
        var old = Sheet(walls: [Wall(0, 0, 6000, 0), Wall(0, 5000, 0, 9000)]);
        var @new = Sheet(walls: [Wall(0, 0, 8000, 0)]);
        var d = SheetDiff.Compare(old, @new);
        var change = Assert.Single(d.WallsChanged);
        Assert.Contains("length 236 -> 315 in", change.What);
        Assert.Single(d.WallsRemoved);
        Assert.Empty(d.WallsAdded);
    }

    [Fact]
    public void AFootingReMarkedAGridMovedAStoreyChangedAndAScheduleCellRewritten()
    {
        var old = Sheet(
            footings: [Footing("F1", 2000, 2000), Footing("F2", 8000, 2000)],
            grid: [new("1", true, 1000), new("2", true, 7000), new("A", false, 500), new("B", false, 6000)],
            storeys: [new("L2", "L1", 3000, 0), new("L3", "L2", 3000, 0)],
            schedules: [new("COLUMN SCHEDULE", "column", [new("C1", new Dictionary<string, string> { ["SIZE"] = "600 x 600", ["REINF"] = "8-25M" }, "plan")])]);
        var @new = Sheet(
            footings: [Footing("F3", 2000, 2000), Footing("F2", 8000, 2000)],
            grid: [new("1", true, 1000), new("2", true, 7000), new("A", false, 800), new("B", false, 6000), new("C", false, 9000)],
            storeys: [new("L2", "L1", 3200, 0), new("L4", "L3", 3000, 0)],
            schedules: [new("COLUMN SCHEDULE", "column", [
                new("C1", new Dictionary<string, string> { ["SIZE"] = "600 x 600", ["REINF"] = "12-25M" }, "plan"),
                new("C2", new Dictionary<string, string> { ["SIZE"] = "400 x 400" }, "plan")])]);
        var d = SheetDiff.Compare(old, @new);
        Assert.Equal("mark F1 -> F3", Assert.Single(d.FootingsChanged).What);
        Assert.Equal(1, d.FootingsSame);
        // 1, 2 and B agree on one frame; A alone disagrees, so A moved and C is new
        Assert.StartsWith("new issue set on the old one's grid by name", d.FrameNote);
        var moved = Assert.Single(d.GridMoved);
        Assert.Equal("A", moved.Name);
        Assert.Equal(300, moved.ToMm - moved.FromMm, 0);
        Assert.Equal("C", Assert.Single(d.GridAdded).Name);
        Assert.Single(d.StoreysChanged);
        Assert.Single(d.StoreysAdded);
        Assert.Single(d.StoreysRemoved);
        var cell = Assert.Single(d.ScheduleChanges);
        Assert.Equal(("C1", "REINF", "8-25M", "12-25M"), (cell.Mark, cell.Column, cell.From, cell.To));
        Assert.Equal("COLUMN SCHEDULE C2", Assert.Single(d.ScheduleRowsAdded));
    }

    [Fact]
    public void APierReadAsAWallThenAsAColumnIsOneMemberNotAChange()
    {
        var old = Sheet(walls: [Wall(0, 0, 1220, 0, 610)]);                // a 48" x 24" pier read as a wall
        var @new = Sheet(columns: [(610, 0)], sizes: [(1220, 610)]);      // read as a column on the reissue
        var d = SheetDiff.Compare(old, @new);
        Assert.Equal(0, d.Changes);
        var kind = Assert.Single(d.KindChanges);
        Assert.Equal("a wall 48 x 24 in", kind.From);
        Assert.Empty(d.ColumnsAdded);
        Assert.Empty(d.WallsRemoved);
        Assert.Contains(d.Lines(), l => l.Contains("the same member, not a change"));
    }

    [Fact]
    public void ASheetShiftedOnItsPageWithTheGridSharedHasNotChanged()
    {
        var grid = new List<GridAxis> { new("1", true, 1000), new("2", true, 7000), new("3", true, 13000), new("A", false, 500) };
        var old = Sheet(columns: [(1000, 500), (7000, 500), (13000, 500)], walls: [Wall(1000, 500, 13000, 500)], footings: [Footing("F1", 7000, 500)], grid: grid);
        // the whole plan drawn 2.5 m right and 1 m up on the new page
        (double, double) S((double X, double Y) p) => (p.X + 2500, p.Y + 1000);
        var @new = Sheet(
            columns: old.Columns.Select(S).ToList(),
            walls: [Wall(3500, 1500, 15500, 1500)],
            footings: [Footing("F1", 9500, 1500)],
            grid: grid.Select(g => new GridAxis(g.Name, g.Vertical, g.AtMm + (g.Vertical ? 2500 : 1000))).ToList());
        var d = SheetDiff.Compare(old, @new);
        Assert.StartsWith("new issue set on the old one's grid by name", d.FrameNote);
        Assert.Equal(0, d.Changes);
        Assert.Equal(3, d.ColumnsSame);
        Assert.Equal(1, d.WallsSame);
        Assert.Equal(1, d.FootingsSame);
    }
}
