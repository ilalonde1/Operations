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
}
