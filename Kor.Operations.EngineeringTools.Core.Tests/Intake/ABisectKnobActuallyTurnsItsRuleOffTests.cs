using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A BISECT'S KNOB IS AN INSTRUMENT, AND AN UNTESTED INSTRUMENT ANSWERS THE SAME EITHER WAY (2026-09-23).
///
/// `corpus-gate --bisect KOR_STEP&lt;n&gt;_OFF,...` re-reads the sets that lost with each rule turned off in turn,
/// to say WHICH rule took them. That is only worth anything if setting the variable changes what the reader does.
/// The way it silently would not: caching the lookup in a `static readonly`. The bisect sets the variable and
/// re-reads inside the SAME process, so a field initialised at first touch freezes whatever the process started
/// with, every arm of the bisect reads the same, and the report says "no single rule explains it" — a green that
/// means nothing, which is the exact fault class Codex found five of in the gate itself on 2026-09-22.
///
/// WHAT THIS COVERS: step 130's knob, both ways, on the witness the fate ledger already carries — 31009's east
/// edge, a line as long as the paper standing at mid-page.
///
/// WHAT IT DOES NOT: the other three knobs (131, 133, 134, 135) are read inside their own methods and have no
/// test of their own; a knob that is honoured here but not passed through to the DXF-side replica of a rule; and
/// it does not prove a knob reaches the reader from a CHILD process, which is how `corpus-analyze` is usually run.
/// A same-class fault it would NOT catch: a knob spelled one way in the code and another way in the bisect's
/// list, since both sides of that pairing are named here in one place.
/// </summary>
public sealed class ABisectKnobActuallyTurnsItsRuleOffTests
{
    // The fate ledger's own case (step 130): a 100,000 mm page, a 60,000 mm line at x = 50,000. Dead centre.
    private const double PageMm = 100_000;
    private const double MidPage = 50_000;
    private const double AtEdge = 3_000;

    [Fact]
    public void Step130StandsALineAsLongAsThePaperAtMidPageUpAsTheBuildings()
    {
        Assert.False(GeometryFilterService.AtTheMargin(MidPage, PageMm, step130Off: false));
        Assert.True(GeometryFilterService.AtTheMargin(AtEdge, PageMm, step130Off: false));
        Assert.True(GeometryFilterService.AtTheMargin(PageMm - AtEdge, PageMm, step130Off: false));
    }

    [Fact]
    public void WithItsKnobOnStep130IsGoneAndEveryLongLineIsTheFrameAgain()
    {
        Assert.True(GeometryFilterService.AtTheMargin(MidPage, PageMm, step130Off: true));
        Assert.True(GeometryFilterService.AtTheMargin(AtEdge, PageMm, step130Off: true));
    }

    /// <summary>
    /// The variable the bisect sets is the variable the rule reads. Set and restored in a finally, and nothing
    /// inside the window reads a PDF — the window is two property reads — so no class running beside this one
    /// can see the flag.
    /// </summary>
    [Fact]
    public void TheKnobTheBisectSetsIsTheKnobTheRuleReads()
    {
        string? was = Environment.GetEnvironmentVariable("KOR_STEP130_OFF");
        try
        {
            Environment.SetEnvironmentVariable("KOR_STEP130_OFF", "1");
            Assert.True(GeometryFilterService.Step130Off);
            Environment.SetEnvironmentVariable("KOR_STEP130_OFF", null);
            Assert.False(GeometryFilterService.Step130Off);
        }
        finally { Environment.SetEnvironmentVariable("KOR_STEP130_OFF", was); }
    }
}
