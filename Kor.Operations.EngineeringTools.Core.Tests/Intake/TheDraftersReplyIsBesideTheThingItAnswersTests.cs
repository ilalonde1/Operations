#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The drafter's reply is beside the thing it answers (foundation §3.1): each of the engineer's
/// items on a round is answered by the nearest annotation another author put within an inch of it
/// on the back-checked copy — a tick is done, words are a reply, nothing is open.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a tick beside one instruction, words beside another, a third with nothing; the
/// engineer inferred as the round's main author; the engineer's own ticks not counted as items; a
/// drafter's comment beside no item listed as unprompted; a tick with no words at all (Bluebeam's
/// ink) counted as done. WHAT IT DOES NOT: the real rounds (the markup-reconcile verb on 31065's
/// MB-4 and MB-6 is that measurement); a reply that answers two items with one tick; the next round
/// raising an item again.
/// </remarks>
public sealed class TheDraftersReplyIsBesideTheThingItAnswersTests
{
    private static MarkupNote Note(string author, string type, string text, double cx, double cy, double w = 30, double h = 10)
        => new(type, text, author, 1) { Cx = cx, Cy = cy, Width = w, Height = h };
    private static MarkupNote Tick(string author, double cx, double cy) => Note(author, "Ink", "", cx, cy, 18, 18);

    [Fact]
    public void ATickIsDoneWordsAreAReplyAndSilenceIsOpen()
    {
        var round = Fixture([
            Note("mb", "FreeText", "Move column right 2\"", 100, 100),
            Note("mb", "FreeText", "Add 2-15M dowels", 400, 100),
            Note("mb", "FreeText", "Extend wall to grid 5", 700, 100),
            Note("mb", "Ink", "/", 1000, 1000, 14, 14),                 // the engineer's own tick, not an item
        ]);
        var backChecked = Fixture([
            Note("mb", "FreeText", "Move column right 2\"", 100, 100),
            Note("mb", "FreeText", "Add 2-15M dowels", 400, 100),
            Note("mb", "FreeText", "Extend wall to grid 5", 700, 100),
            Note("mb", "Ink", "/", 1000, 1000, 14, 14),
            Tick("sz", 130, 110),                                       // a tick with nothing in it, an inch away
            Note("sz", "FreeText", "Can't - clashes with the duct", 420, 90),
            Note("sz", "FreeText", "Also moved the beam at grid 7", 1500, 600),
        ]);
        var page = MarkupReconcile.Reconcile(round, backChecked);
        Assert.Equal("mb", page.Engineer);
        Assert.Equal(3, page.Results.Count);
        Assert.Equal(MarkupReconcile.Outcome.Done, page.Results[0].Outcome);
        Assert.Equal("sz", page.Results[0].ReplyAuthor);
        Assert.Equal(MarkupReconcile.Outcome.Replied, page.Results[1].Outcome);
        Assert.Equal("Can't - clashes with the duct", page.Results[1].Reply);
        Assert.Equal(MarkupReconcile.Outcome.Open, page.Results[2].Outcome);
        Assert.Equal((1, 1, 1), (page.Done, page.Replied, page.Open));
        var extra = Assert.Single(page.Unprompted);
        Assert.Equal("Also moved the beam at grid 7", extra.Text);
    }

    [Fact]
    public void OneTickAnswersEveryLabelBesideItAndARingIsAnItem()
    {
        // the engineer's column: mark, size and reinforcing as three labels; the drafter ticks once.
        // On the back-check round the engineer rings a thing to fix; his small scribbles are his ticks.
        var round = Fixture([
            Note("mb", "FreeText", "ZC1", 200, 300), Note("mb", "FreeText", "350x750", 200, 285), Note("mb", "FreeText", "8-35M verts", 200, 270),
            Note("mb", "Ink", "o", 900, 900, 127, 144),                 // a ring
            Note("mb", "Ink", "-", 1200, 1200, 890, 358),               // a long stroke
            Note("mb", "Ink", ".", 400, 400, 14, 14),                   // his own tick
        ]);
        var backChecked = Fixture(round.Annotations.Concat([Tick("sz", 230, 290), Tick("sz", 930, 880)]).ToList());
        var page = MarkupReconcile.Reconcile(round, backChecked);
        Assert.Equal(4, page.Results.Count);                             // three labels and the ring; not the stroke, not his tick
        Assert.Equal(4, page.Done);
        Assert.Contains(page.Results, r => r.Item.Action == "attend");
        Assert.Equal(MarkupList.Kind.Shape, MarkupList.Classify(round.Annotations[4]).Kind);
        Assert.Equal(MarkupList.Kind.Approval, MarkupList.Classify(round.Annotations[5]).Kind);
    }

    /// <summary>A plan record at 1:96 carrying these annotations, all of them.</summary>
    private static SheetRecord Fixture(List<MarkupNote> annotations)
    {
        var geometry = new ExtractedGeometry();
        var content = new VectorPageReader.PageContent(1, 3024, 2160, new List<VectorPageReader.TextToken>(), new List<VectorPageReader.GeomPath>());
        return new SheetRecord(1, 3024, 2160, 0, "S2.01", null, "plan", "P1", null, "1/8\" = 1'-0\"", 96,
            geometry, Array.Empty<ScheduleTable>(), Array.Empty<PlanMark>(), Array.Empty<SlabThicknessZoner.Callout>(),
            new GridBubbles.Grid(Array.Empty<GridBubbles.Bubble>(), Array.Empty<double>(), Array.Empty<double>(), Array.Empty<GridBubbles.Axis>()),
            SheetFurniture.Set.Empty, annotations.Where(a => a.Text.Length > 0).ToList(), 0, content, Array.Empty<PathFate>(), Array.Empty<WordFate>())
        { Annotations = annotations };
    }
}
