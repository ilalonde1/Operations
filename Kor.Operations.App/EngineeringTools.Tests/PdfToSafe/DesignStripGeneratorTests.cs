#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// DesignStripGenerator tiles a slab bounding box with named strips. Zero
    /// existing tests; this file locks the spacing, prefix, and direction
    /// semantics engineers see in SAFE after export.
    /// </summary>
    public class DesignStripGeneratorCoverageTests
    {
        private static readonly List<List<(double, double)>> Rect10x10 = new()
        {
            new() { (0, 0), (10000, 0), (10000, 10000), (0, 10000) }
        };

        [Fact]
        public void Generate_EmptySlabs_ReturnsEmpty()
        {
            var strips = DesignStripGenerator.Generate(
                new List<List<(double, double)>>(), spacingMm: 2000, stripAAlongX: true);
            Assert.Empty(strips);
        }

        [Fact]
        public void Generate_ZeroSpacing_ReturnsEmpty()
        {
            var strips = DesignStripGenerator.Generate(Rect10x10, spacingMm: 0, stripAAlongX: true);
            Assert.Empty(strips);
        }

        [Fact]
        public void Generate_NegativeSpacing_ReturnsEmpty()
        {
            var strips = DesignStripGenerator.Generate(Rect10x10, spacingMm: -100, stripAAlongX: true);
            Assert.Empty(strips);
        }

        [Fact]
        public void Generate_DegenerateSlab_SinglePoint_ReturnsEmpty()
        {
            var degen = new List<List<(double, double)>> { new() { (100, 100) } };
            var strips = DesignStripGenerator.Generate(degen, spacingMm: 1000, stripAAlongX: true);
            Assert.Empty(strips);
        }

        [Fact]
        public void Generate_10mSquareAt2mSpacing_ProducesExpectedStripCounts()
        {
            var strips = DesignStripGenerator.Generate(Rect10x10, spacingMm: 2000, stripAAlongX: true).ToList();
            // hw = 1000; count formula = ceil((maxY+hw - (minY-hw)) / spacing) + 1
            //   = ceil((10000+1000 - (0-1000)) / 2000) + 1 = ceil(12000/2000) + 1 = 7
            // Same for X, so 7 SA + 7 SB = 14 strips.
            Assert.Equal(14, strips.Count);
        }

        [Fact]
        public void Generate_StripAAlongX_SaIsHorizontalSbIsVertical()
        {
            var strips = DesignStripGenerator.Generate(Rect10x10, spacingMm: 2500, stripAAlongX: true).ToList();
            Assert.All(strips.Where(s => s.Name.StartsWith("SA")), s => Assert.True(s.IsAlongX));
            Assert.All(strips.Where(s => s.Name.StartsWith("SB")), s => Assert.False(s.IsAlongX));
        }

        [Fact]
        public void Generate_StripAAlongX_False_SaIsVerticalSbIsHorizontal()
        {
            var strips = DesignStripGenerator.Generate(Rect10x10, spacingMm: 2500, stripAAlongX: false).ToList();
            Assert.All(strips.Where(s => s.Name.StartsWith("SA")), s => Assert.False(s.IsAlongX));
            Assert.All(strips.Where(s => s.Name.StartsWith("SB")), s => Assert.True(s.IsAlongX));
        }

        [Fact]
        public void Generate_NamesFormattedAsThreeDigitNumbers()
        {
            // SA001, SA002, ... — D3 format.
            var strips = DesignStripGenerator.Generate(Rect10x10, spacingMm: 2500, stripAAlongX: true)
                .Where(s => s.Name.StartsWith("SA"))
                .Select(s => s.Name)
                .ToList();
            Assert.Contains("SA001", strips);
            // Ensure zero-padding holds even with fewer than 10 strips.
            Assert.All(strips, n => Assert.Equal(5, n.Length)); // "SA" + 3 digits
        }

        [Fact]
        public void Generate_HalfWidthIsExactlyHalfSpacing()
        {
            var strips = DesignStripGenerator.Generate(Rect10x10, spacingMm: 2000, stripAAlongX: true).ToList();
            Assert.All(strips, s => Assert.Equal(1000.0, s.HalfWidth));
        }

        [Fact]
        public void Generate_StripsExtendOneHalfWidthBeyondExtents()
        {
            var strips = DesignStripGenerator.Generate(Rect10x10, spacingMm: 2000, stripAAlongX: true).ToList();
            var horizontals = strips.Where(s => s.IsAlongX).ToList();
            // X1 and X2 reach from minX-hw to maxX+hw.
            Assert.All(horizontals, s => Assert.Equal(-1000.0, s.X1));
            Assert.All(horizontals, s => Assert.Equal(11000.0, s.X2));
        }

        [Fact]
        public void Generate_MultipleSlabs_BoundingBoxIsUnion()
        {
            // Two slabs — strip extents should cover both.
            var slabs = new List<List<(double, double)>>
            {
                new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) },
                new() { (5000, 5000), (8000, 5000), (8000, 8000), (5000, 8000) },
            };
            var strips = DesignStripGenerator.Generate(slabs, spacingMm: 1000, stripAAlongX: true).ToList();
            var horizontals = strips.Where(s => s.IsAlongX).ToList();
            Assert.All(horizontals, s => Assert.InRange(s.Y1, -500, 8500));
        }
    }
}
