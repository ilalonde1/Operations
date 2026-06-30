#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class NonConcreteHintTests
{
    [Fact]
    public void Wood_frame_set_is_hinted()
    {
        // Substantial framing vocabulary (>= the floor), as a real wood-frame set carries it (Birken: 205).
        var lines = Enumerable.Range(0, 10).SelectMany(_ => new[]
        {
            "THE WOOD TRUSSES SHALL BE DESIGNED FOR 1/360 DEFLECTION",
            "5 1/4 x 11 7/8 ENGINEERED JOIST", "PLYWOOD SHEATHING NAILED PER SCHEDULE",
            "SIMPSON HGUS HANGER", "GLULAM BEAM",
            // a wood-frame set still has concrete foundations — these must NOT suppress the hint
            "CONCRETE FOUNDATION 25 MPa", "15M REINF IN FOOTING", "SLAB ON GRADE",
        }).ToList();
        Assert.Contains("WOOD-FRAME", SlabTakeoffEngine.NonConcreteHint(lines));
    }

    [Fact]
    public void Steel_framed_set_is_hinted()
    {
        var lines = Enumerable.Range(0, 10).SelectMany(_ => new[]
        {
            "STRUCTURAL STEEL W360X64 BEAM", "W250X45 COLUMN", "HSS 102x102x6",
            "BASE PLATE 20mm", "WIDE FLANGE FRAMING",
        }).ToList();
        Assert.Contains("STEEL-FRAMED", SlabTakeoffEngine.NonConcreteHint(lines));
    }

    [Fact]
    public void Concrete_set_gets_no_hint()
    {
        var lines = new[]
        {
            "200 SLAB", "CONCRETE 30 MPa", "15M @ 200 REINF EACH WAY",
            "SUSPENDED SLAB", "SHEAR WALL", "TYPICAL FLOOR PLAN",
        };
        Assert.Equal("", SlabTakeoffEngine.NonConcreteHint(lines));
    }

    [Fact]
    public void A_stray_wood_note_on_a_concrete_set_does_not_trip_it()
    {
        // One incidental "wood blocking" note on an otherwise concrete set must not mislabel it.
        var lines = new[]
        {
            "200 SLAB", "CONCRETE 30 MPa", "15M @ 200 REINF", "SUSPENDED SLAB",
            "CONCRETE TOPPING", "WOOD BLOCKING AT PARAPET",
        };
        Assert.Equal("", SlabTakeoffEngine.NonConcreteHint(lines));
    }
}
