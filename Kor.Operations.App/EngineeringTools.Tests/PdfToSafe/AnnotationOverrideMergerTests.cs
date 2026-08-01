#nullable enable
using System.Collections.Generic;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

public sealed class AnnotationOverrideMergerTests
{
    [Fact]
    public void Empty_overrides_returns_text_based_values_unchanged()
    {
        var input = new AnnotationResolution(
            SlabThicknessMm:  new double?[] { 200, null, 250 },
            ColumnSectionMm:  new (double, double)?[] { (400, 400), null },
            LineSectionMm:    new (double, double)?[] { null, (300, 600) });

        var result = AnnotationOverrideMerger.Merge(
            input,
            new Dictionary<int, double>(),
            new Dictionary<int, (double, double)>(),
            new Dictionary<int, (double, double)>());

        Assert.Equal(new double?[] { 200, null, 250 }, result.SlabThicknessMm);
        Assert.Equal(new (double, double)?[] { (400, 400), null }, result.ColumnSectionMm);
        Assert.Equal(new (double, double)?[] { null, (300, 600) }, result.LineSectionMm);
    }

    [Fact]
    public void Override_wins_over_text_value()
    {
        var input = new AnnotationResolution(
            SlabThicknessMm:  new double?[] { 200, 250 },
            ColumnSectionMm:  new (double, double)?[] { (400, 400) },
            LineSectionMm:    new (double, double)?[] { (300, 600) });

        var result = AnnotationOverrideMerger.Merge(
            input,
            new Dictionary<int, double> { [0] = 300 },
            new Dictionary<int, (double, double)> { [0] = (500, 600) },
            new Dictionary<int, (double, double)> { [0] = (200, 400) });

        Assert.Equal(300, result.SlabThicknessMm[0]);
        Assert.Equal(250, result.SlabThicknessMm[1]); // unchanged
        Assert.Equal((500.0, 600.0), result.ColumnSectionMm[0]);
        Assert.Equal((200.0, 400.0), result.LineSectionMm[0]);
    }

    [Fact]
    public void Override_fills_in_null_text_value()
    {
        var input = new AnnotationResolution(
            SlabThicknessMm:  new double?[] { null, null },
            ColumnSectionMm:  new (double, double)?[] { null },
            LineSectionMm:    new (double, double)?[] { null });

        var result = AnnotationOverrideMerger.Merge(
            input,
            new Dictionary<int, double> { [1] = 220 },
            new Dictionary<int, (double, double)> { [0] = (450, 450) },
            new Dictionary<int, (double, double)> { [0] = (250, 500) });

        Assert.Null(result.SlabThicknessMm[0]);
        Assert.Equal(220, result.SlabThicknessMm[1]);
        Assert.Equal((450.0, 450.0), result.ColumnSectionMm[0]);
        Assert.Equal((250.0, 500.0), result.LineSectionMm[0]);
    }

    [Fact]
    public void Out_of_range_override_index_is_ignored()
    {
        var input = new AnnotationResolution(
            SlabThicknessMm: new double?[] { 200 },
            ColumnSectionMm: new (double, double)?[] { (400, 400) },
            LineSectionMm:   new (double, double)?[] { (300, 600) });

        var result = AnnotationOverrideMerger.Merge(
            input,
            new Dictionary<int, double> { [5] = 999 },
            new Dictionary<int, (double, double)>(),
            new Dictionary<int, (double, double)>());

        Assert.Equal(new double?[] { 200 }, result.SlabThicknessMm);
    }

    [Fact]
    public void Returns_new_instance_does_not_mutate_input_arrays()
    {
        var slabIn = new double?[] { 200 };
        var colIn  = new (double, double)?[] { (400, 400) };
        var lineIn = new (double, double)?[] { (300, 600) };
        var input = new AnnotationResolution(slabIn, colIn, lineIn);

        AnnotationOverrideMerger.Merge(
            input,
            new Dictionary<int, double> { [0] = 999 },
            new Dictionary<int, (double, double)> { [0] = (1, 1) },
            new Dictionary<int, (double, double)> { [0] = (1, 1) });

        Assert.Equal(200, slabIn[0]);
        Assert.Equal((400.0, 400.0), colIn[0]);
        Assert.Equal((300.0, 600.0), lineIn[0]);
    }
}
