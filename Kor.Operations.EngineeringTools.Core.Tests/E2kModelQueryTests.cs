using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The query surface — the questions a person asks a finished model.
///
/// It exists so that the answer from a terminal and the answer from /ask are the same answer. The
/// test that matters most here is not any single number: it is that two questions about the same
/// model never contradict each other, because the moment they do, nobody can trust either.
/// </summary>
public sealed class E2kModelQueryTests
{
    private readonly ITestOutputHelper _out;
    public E2kModelQueryTests(ITestOutputHelper output) => _out = output;

    // Three storeys ten feet apart. LEVEL 2 deliberately carries nothing.
    private static E2kDocument Building(params string[] body) => E2kDocument.Parse(new[]
    {
        "$ CONTROLS",
        "  UNITS  \"Kip\"  \"in\"",
        "",
        "$ STORIES - IN SEQUENCE FROM TOP",
        "  STORY \"LEVEL 3\"  HEIGHT 120",
        "  STORY \"LEVEL 2\"  HEIGHT 120",
        "  STORY \"LEVEL 1\"  HEIGHT 120",
        "  STORY \"Base\"  ELEV 0",
        "",
        "$ SLAB PROPERTIES",
        "  SHELLPROP  \"KOR-S12\"  PROPTYPE  \"Slab\"  MATERIAL \"30 MPa Floor\"  SLABTHICKNESS 12",
        "",
        "$ WALL PROPERTIES",
        "  SHELLPROP  \"KOR-W12\"  PROPTYPE  \"Wall\"  MATERIAL \"65 MPa Walls\"  WALLTHICKNESS 12",
        "",
        "$ FRAME SECTIONS",
        "  FRAMESECTION  \"KOR-C12x12\"  MATERIAL \"65 MPa Columns\"  SHAPE \"Concrete Rectangular\"  D 12 B 12 ",
        "",
        "$ POINT COORDINATES",
        "  POINT \"P1\"  0 0 0",
        "  POINT \"P2\"  120 0 0",
        "  POINT \"P3\"  120 120 0",
        "  POINT \"P4\"  0 120 0",
        "  POINT \"P5\"  30 30 0",
        "  POINT \"P6\"  90 30 0",
        "  POINT \"P7\"  90 90 0",
        "  POINT \"P8\"  30 90 0",
        "  POINT \"P11\"  60 60 0",
        "",
    }.Concat(body).Concat(new[] { "", "$ END OF MODEL FILE" }).ToArray());

    /// <summary>One slab, one wall and one column on LEVEL 1 and LEVEL 3; nothing on LEVEL 2.</summary>
    private static string[] TopAndBottom => new[]
    {
        "$ AREA CONNECTIVITIES",
        "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
        "  AREA \"F3\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
        "  AREA \"W1\"  PANEL  4  \"P1\"  \"P2\"  \"P2\"  \"P1\"  1  1  0  0",
        "",
        "$ LINE CONNECTIVITIES",
        "  LINE  \"C1\"  COLUMN  \"P11\"  \"P11\"  1",
        "",
        "$ AREA ASSIGNS",
        "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
        "  AREAASSIGN  \"F3\"  \"LEVEL 3\"  SECTION \"KOR-S12\"",
        "  AREAASSIGN  \"W1\"  \"LEVEL 1\"  SECTION \"KOR-W12\"",
        "",
        "$ LINE ASSIGNS",
        "  LINEASSIGN  \"C1\"  \"LEVEL 1\"  SECTION \"KOR-C12x12\"  ANG 0",
    };

    [Fact]
    public void EveryStoreyIsAnsweredForEvenTheOnesHoldingNothing()
    {
        var storeys = E2kModelQuery.Storeys(Building(TopAndBottom));

        // Base is the datum the storeys are measured from, not a storey that holds structure, and
        // the model's own storey list leaves it out. Everything above it is accounted for, top down.
        Assert.Equal(3, storeys.Count);
        Assert.Equal(new[] { "LEVEL 3", "LEVEL 2", "LEVEL 1" }, storeys.Select(s => s.Name));

        var one = Assert.Single(storeys, s => s.Name == "LEVEL 1");
        Assert.Equal(1, one.Slabs);
        Assert.Equal(1, one.Walls);
        Assert.Equal(1, one.Columns);
        Assert.Equal(100.0, one.SlabAreaSqFt, 6);          // 10 ft x 10 ft
        Assert.Equal(120.0, one.RiseInches, 6);
        Assert.Equal(new[] { "12\"" }, one.SlabThicknesses);
    }

    [Fact]
    public void AStoreyHoldingNothingBetweenTwoThatDoIsWorthALook()
    {
        var concerns = E2kModelQuery.WorthALook(Building(TopAndBottom));

        var empty = Assert.Single(concerns, c => c.Storey == "LEVEL 2" && c.What.Contains("holds nothing"));
        Assert.Contains("above and below", empty.Why);
    }

    [Fact]
    public void ATowerWithNoGapsIsSaidToHaveNone()
    {
        var full = Building(
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "  AREA \"W1\"  PANEL  4  \"P1\"  \"P2\"  \"P2\"  \"P1\"  1  1  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
            "  AREAASSIGN  \"W1\"  \"LEVEL 1\"  SECTION \"KOR-W12\"");

        // LEVEL 2 and LEVEL 3 hold nothing, but nothing above them does either, so they are the top
        // of the model rather than a hole in it.
        Assert.DoesNotContain(E2kModelQuery.WorthALook(full), c => c.What.Contains("holds nothing"));
    }

    [Fact]
    public void TheHolesAreReportedBiggestFirstOnTheStoreyTheyAreCutIn()
    {
        var holes = E2kModelQuery.Openings(Building(
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "  AREA \"O1\"  AREA  4  \"P5\"  \"P6\"  \"P7\"  \"P8\"  0  0  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
            "  AREAASSIGN  \"O1\"  \"LEVEL 1\"  OPENING \"Yes\""));

        var hole = Assert.Single(holes);
        Assert.Equal("LEVEL 1", hole.Storey);
        Assert.Equal(25.0, hole.AreaSqFt, 6);              // 5 ft x 5 ft
    }

    [Fact]
    public void EverySectionTheModelUsesIsNamedWithItsSizeAndWhereItIsUsed()
    {
        var sections = E2kModelQuery.Sections(Building(TopAndBottom));

        var slab = Assert.Single(sections, s => s.Section == "KOR-S12");
        Assert.Equal("Slab", slab.Kind);
        Assert.Equal("12\"", slab.Size);
        Assert.Equal(2, slab.Used);                        // LEVEL 1 and LEVEL 3

        var col = Assert.Single(sections, s => s.Section == "KOR-C12x12");
        Assert.Equal("Column", col.Kind);
        Assert.Equal("12\" x 12\"", col.Size);
        Assert.Contains("LEVEL 1", col.Storeys);
    }

    /// <summary>
    /// The invariant that makes the surface trustworthy: two questions about one model give one
    /// answer. Ask what a storey holds and ask what it costs, and the concrete must match — because
    /// an /ask layer will route the two questions to these two methods and a person will read both.
    /// </summary>
    [Fact]
    public void AskingWhatAStoreyHoldsAndWhatItCostsGivesTheSameConcrete()
    {
        var doc = Building(TopAndBottom);

        var byStorey = E2kModelQuery.Storeys(doc).ToDictionary(s => s.Name, s => s.ConcreteYd3, StringComparer.OrdinalIgnoreCase);
        var byTakeoff = E2kQuantityTakeoff.Read(doc).Inputs
            .GroupBy(i => i.Level, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.ConcreteVolume), StringComparer.OrdinalIgnoreCase);

        foreach (var (storey, concrete) in byTakeoff)
        {
            _out.WriteLine($"{storey}: query {byStorey[storey]:N3} yd³ vs takeoff {concrete:N3} yd³");
            Assert.Equal(concrete, byStorey[storey], 6);
        }

        // And no storey is quietly given concrete by one and not the other.
        foreach (var (storey, concrete) in byStorey.Where(kv => kv.Value > 0))
            Assert.True(byTakeoff.ContainsKey(storey), $"{storey} has concrete in the storey answer and none in the takeoff.");
    }
}
