#nullable enable

using System.Collections.Generic;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class SlabThicknessReaderTests
{
    [Fact]
    public void Reads_the_modal_field_thickness_and_ignores_the_thicker_slab_bands()
    {
        // The real P6/P5 parkade callouts: 10" field slab (called out twice) + 24" slab bands.
        var lines = new List<string> { "10\" SLAB", "24\" SLAB", "10\" SLAB", "INTO BOTTOM OF SLAB, SLAB BAND OR FOOTING." };
        Assert.Equal(10, SlabThicknessReader.DominantThicknessIn(lines));
    }

    [Fact]
    public void Does_not_pick_up_a_slab_on_grade_or_a_column_callout()
    {
        // Both distractors have a word between the number and SLAB, so the tight pattern skips them.
        var lines = new List<string>
        {
            "4\" UNREINFORCED SLAB ON GRADE",
            "12\" PC3 x 8. FOR ALL SLAB ON GRADE STEPS",
            "12\" SLABS DETAIL OR THICKEN SLAB UPWARDS TO SUIT.",
        };
        Assert.Equal(12, SlabThicknessReader.DominantThicknessIn(lines)); // only the tight "12\" SLABS" counts
    }

    [Fact]
    public void Ties_go_to_the_thinner_field_slab()
    {
        var lines = new List<string> { "14\" SLAB", "10\" SLAB" };
        Assert.Equal(10, SlabThicknessReader.DominantThicknessIn(lines));
    }

    [Fact]
    public void Returns_null_when_no_slab_thickness_is_called_out()
    {
        Assert.Null(SlabThicknessReader.DominantThicknessIn(new List<string> { "SHEAR WALL SCHEDULE", "PLAN NORTH" }));
        Assert.Null(SlabThicknessReader.DominantThicknessIn(null));
    }

    [Fact]
    public void Rejects_implausible_thicknesses()
    {
        // 2" is a topping; 60" is a mat — neither is a field slab.
        Assert.Null(SlabThicknessReader.DominantThicknessIn(new List<string> { "2\" SLAB", "60\" SLAB" }));
    }

    [Fact]
    public void Reads_a_metric_mm_callout_as_inches()
    {
        // 5380 Heather (31065) calls out the field slab as "200 SLAB" — 200 mm = 7.87" → 8".
        var lines = new List<string> { "200 SLAB", "200 SLAB GC1", "200 SLAB", "300 15M @ 200 VERTS" };
        Assert.Equal(8, SlabThicknessReader.DominantThicknessIn(lines));
    }

    [Fact]
    public void Metric_callouts_beat_note_numbering_noise()
    {
        // The trap that fooled the imperial-only reader: "5. SLABS TO BE CAMBERED" matched as 5",
        // overriding the real metric callouts. The metric pool must win on a metric drawing.
        var lines = new List<string>
        {
            "5. SLABS TO BE CAMBERED PER STRUCTURAL",
            "200 SLAB", "200 SLAB", "250 SLAB",
        };
        Assert.Equal(8, SlabThicknessReader.DominantThicknessIn(lines)); // modal 200 mm → 8"
    }

    [Fact]
    public void A_thicker_metric_field_slab_converts()
    {
        // A podium at 300 mm = 11.81" → 12".
        var lines = new List<string> { "300 SLAB", "300 SLAB", "300 SLAB" };
        Assert.Equal(12, SlabThicknessReader.DominantThicknessIn(lines));
    }

    [Fact]
    public void A_lone_stray_metric_match_does_not_flip_an_imperial_drawing()
    {
        // One coincidental "200 SLAB" must not turn an imperial set metric — needs ≥2 and ≥ imperial count.
        var lines = new List<string> { "10\" SLAB", "10\" SLAB", "10\" SLAB", "200 SLAB" };
        Assert.Equal(10, SlabThicknessReader.DominantThicknessIn(lines));
    }

    // ── RecoverStructuralDepthIn: the wider read for a plate the field reader came up empty on ──

    [Fact]
    public void Recovery_reads_a_mat_or_slab_on_grade_depth_the_field_reader_skips()
    {
        // The field reader returns null for these (a word intervenes / it's a mat); recovery finds the depth.
        Assert.Null(SlabThicknessReader.DominantThicknessIn(new List<string> { "4\" UNREINFORCED SLAB ON GRADE" }));
        Assert.Equal(4, SlabThicknessReader.RecoverStructuralDepthIn(new List<string> { "4\" UNREINFORCED SLAB ON GRADE" }));
        Assert.Equal(24, SlabThicknessReader.RecoverStructuralDepthIn(new List<string> { "24\" MAT" }));
    }

    [Fact]
    public void Recovery_reads_a_metric_mat_depth_as_inches()
    {
        // A 600 mm parkade mat = 23.6" → 24".
        Assert.Equal(24, SlabThicknessReader.RecoverStructuralDepthIn(new List<string> { "600 MAT" }));
        // A deep raft the field reader's 600 mm cap excludes (900 mm = 35.4" → 35").
        Assert.Equal(35, SlabThicknessReader.RecoverStructuralDepthIn(new List<string> { "900 RAFT" }));
    }

    [Fact]
    public void Recovery_ignores_bare_slab_and_note_numbering()
    {
        // Bare "N\" SLAB" is the FIELD reader's job — recovery must not double-claim it.
        Assert.Null(SlabThicknessReader.RecoverStructuralDepthIn(new List<string> { "10\" SLAB", "10\" SLAB" }));
        // Note-numbering must never leak a phantom depth (the field reader's old trap).
        Assert.Null(SlabThicknessReader.RecoverStructuralDepthIn(new List<string> { "5. SLABS TO BE CAMBERED" }));
        Assert.Null(SlabThicknessReader.RecoverStructuralDepthIn(new List<string> { "GENERAL NOTES" }));
        Assert.Null(SlabThicknessReader.RecoverStructuralDepthIn(null));
    }
}
