using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Covers fill/stroke combinations on synthetic slab, column and line candidates, annotation
/// exemption, and precedence over furniture while preserving markup-only precedence.
/// The differential compares retained inputs alone against those inputs interleaved with no-ink
/// content: ordered geometry, colours, sizes, annotation flags, drop panels, metadata and fates
/// of surviving paths must match. Independent point lists prevent mutation from masking a change.
/// Does not compare PDF parsing, corpus counts, overlays or exported DXF bytes. A reader assigning
/// the wrong ink flags, or a threshold wrong in both runs, would not be caught by this check.
/// </summary>
public sealed class APathThatDrawsNothingIsNotGeometryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnlyContentWithNeitherFillNorStrokeIsDiscarded(bool filled, bool stroked)
    {
        foreach (bool annotation in new[] { false, true })
        {
            var paths = Candidates().Select(p => p with
            {
                IsFilled = filled, IsStroked = stroked, IsAnnotation = annotation,
            }).ToList();
            var fates = new List<PathFate>();
            var geometry = FateFixture.Classify(paths, fates);
            Assert.Equal(Enumerable.Range(0, paths.Count), fates.Select(f => f.PathIndex));
            if (!annotation && !filled && !stroked)
            {
                Assert.Empty(geometry.Slabs);
                Assert.Empty(geometry.Columns);
                Assert.Empty(geometry.Lines);
                Assert.All(fates, f =>
                {
                    Assert.Equal(PathReason.NoInk, f.Reason);
                    Assert.Equal(Disposition.Discarded, f.Disposition);
                    Assert.Null(f.ObjectIndex);
                });
            }
            else
            {
                Assert.Single(geometry.Slabs);
                Assert.Single(geometry.Columns);
                Assert.Single(geometry.Lines);
                Assert.Equal(new[] { PathReason.BecameSlab, PathReason.BecameColumnByShape, PathReason.EmittedAsLine },
                    fates.Select(f => f.Reason));
            }

            // The rule applies even when the caller does not request a ledger.
            TheLedgerChangesNothingButTheLedgerTests.AssertGeometryEqual(geometry,
                FateFixture.Classify(Clone(paths), null));
        }
    }

    [Fact]
    public void NoInkPrecedesFurnitureButFollowsMarkupOnly()
    {
        var path = FateFixture.Rect(600, 600, 20000, 20000) with { IsFilled = false, IsStroked = false };
        var fates = new List<PathFate>();
        FateFixture.Classify([path], fates);
        Assert.Equal(PathReason.NoInk, Assert.Single(fates).Reason);
        fates.Clear();
        FateFixture.Classify([path], fates, markupOnly: true);
        Assert.Equal(PathReason.MarkupOnlyMode, Assert.Single(fates).Reason);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AddingNoInkContentChangesNoSurvivingGeometryOrFate(bool markupOnly, bool excludeGrid)
    {
        var retained = FateFixture.Cases().Select(c => c.Path)
            .Where(p => p.IsAnnotation || p.IsFilled || p.IsStroked).ToList();
        // Keep no-ink annotations, plus a real drop-panel candidate, in the reference population.
        retained.AddRange(Candidates().Select(p => p with
        {
            IsAnnotation = true, IsFilled = false, IsStroked = false,
        }));
        retained.Add(FateFixture.Rect(1600, 300));
        var invisible = Candidates().Append(FateFixture.Rect(1600, 300))
            .Select(p => p with { IsFilled = false, IsStroked = false }).ToArray();
        var mixed = new List<RawSubpath>();
        for (int i = 0; i < retained.Count; i++)
        {
            mixed.Add(invisible[i % invisible.Length]);
            mixed.Add(retained[i]);
        }
        var expectedFates = new List<PathFate>();
        var actualFates = new List<PathFate>();
        var expected = FateFixture.Classify(Clone(retained), expectedFates, markupOnly, excludeGrid, slabMinimum: 1000);
        var actual = FateFixture.Classify(Clone(mixed), actualFates, markupOnly, excludeGrid, slabMinimum: 1000);
        TheLedgerChangesNothingButTheLedgerTests.AssertGeometryEqual(expected, actual);
        Assert.Equal(Enumerable.Range(0, mixed.Count), actualFates.Select(f => f.PathIndex));
        for (int i = 0; i < retained.Count; i++)
        {
            var discarded = actualFates[2 * i];
            Assert.Equal(markupOnly ? PathReason.MarkupOnlyMode : PathReason.NoInk, discarded.Reason);
            Assert.Equal(Disposition.Discarded, discarded.Disposition);
            Assert.Null(discarded.ObjectIndex);
            Assert.Equal(expectedFates[i], actualFates[2 * i + 1] with { PathIndex = i });
        }
    }

    private static List<RawSubpath> Candidates() =>
    [
        FateFixture.Rect(5000, 4000),
        // Red makes the old unfilled-small-shape rule inapplicable: no ink must still be refused.
        FateFixture.Rect(600, 800) with { Color = (0xF0, 0, 0) },
        FateFixture.Line(1000, 1000, 4000, 1500),
    ];

    private static List<RawSubpath> Clone(IEnumerable<RawSubpath> paths)
        => paths.Select(p => p with { Points = p.Points.ToList() }).ToList();
}
