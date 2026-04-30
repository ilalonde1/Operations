#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// Grid generator is pure clustering arithmetic driven by column
    /// positions. Gets called automatically for every F2K export, so any
    /// regression here shows up as bad grids on every engineer's model.
    /// </summary>
    public class StructuralGridGeneratorCoverageTests
    {
        [Fact]
        public void Generate_NoColumns_ReturnsEmpty()
        {
            var result = StructuralGridGenerator.Generate(System.Array.Empty<(double, double)>());
            Assert.Empty(result);
        }

        [Fact]
        public void Generate_SingleColumn_ReturnsOneXAndOneY()
        {
            var result = StructuralGridGenerator.Generate(new[] { (1000.0, 2000.0) }).ToList();
            Assert.Equal(2, result.Count);
            Assert.Single(result, g => !g.IsAlongX && g.OrdMm == 1000.0);
            Assert.Single(result, g =>  g.IsAlongX && g.OrdMm == 2000.0);
        }

        [Fact]
        public void Generate_AlignedColumns_MergeWithinTolerance()
        {
            // Three columns on the same Y line, within 100 mm of each other,
            // default 400 mm tolerance → single horizontal grid line.
            var cols = new[]
            {
                (1000.0, 5000.0),
                (3000.0, 5050.0),
                (5000.0, 4980.0),
            };
            var result = StructuralGridGenerator.Generate(cols).ToList();
            var horizontals = result.Where(g => g.IsAlongX).ToList();
            Assert.Single(horizontals);
            // Merged ordinate is the mean of the cluster.
            Assert.Equal((5000.0 + 5050.0 + 4980.0) / 3.0, horizontals[0].OrdMm, 3);
        }

        [Fact]
        public void Generate_DistinctYs_StayAsSeparateGridLines()
        {
            var cols = new[]
            {
                (0.0, 0.0),
                (0.0, 5000.0),   // well beyond default 400mm tol
                (0.0, 10000.0),
            };
            var result = StructuralGridGenerator.Generate(cols).ToList();
            Assert.Equal(3, result.Count(g => g.IsAlongX));
        }

        [Fact]
        public void Generate_HorizontalLabels_UseLetters()
        {
            var cols = new[] { (0.0, 0.0), (0.0, 5000.0), (0.0, 10000.0) };
            var labels = StructuralGridGenerator.Generate(cols)
                .Where(g => g.IsAlongX)
                .Select(g => g.Label)
                .ToList();
            Assert.Contains("A", labels);
            Assert.Contains("B", labels);
            Assert.Contains("C", labels);
        }

        [Fact]
        public void Generate_VerticalLabels_UseNumbers()
        {
            var cols = new[] { (0.0, 0.0), (5000.0, 0.0), (10000.0, 0.0) };
            var labels = StructuralGridGenerator.Generate(cols)
                .Where(g => !g.IsAlongX)
                .Select(g => g.Label)
                .ToList();
            Assert.Contains("1", labels);
            Assert.Contains("2", labels);
            Assert.Contains("3", labels);
        }

        [Fact]
        public void Generate_TooManyGridsPerDirection_BailsOut()
        {
            // Default limit is 20 grids per direction; feed 25 distinct Xs.
            var cols = new List<(double, double)>();
            for (int i = 0; i < 25; i++) cols.Add((i * 10000.0, 0.0));

            var result = StructuralGridGenerator.Generate(cols);
            Assert.Empty(result);
        }

        [Fact]
        public void Generate_CustomTolerance_CanTightenMerging()
        {
            // With a 50 mm tolerance, points at 5000 and 5100 should NOT merge.
            var cols = new[] { (0.0, 5000.0), (0.0, 5100.0) };
            var result = StructuralGridGenerator.Generate(cols, clusterToleranceMm: 50.0);
            Assert.Equal(2, result.Count(g => g.IsAlongX));
        }

        [Fact]
        public void Generate_OrdinatesReturnedInAscendingOrder()
        {
            // Supply out-of-order input — expect sorted ordinates on output.
            var cols = new[] { (9000.0, 0.0), (1000.0, 0.0), (5000.0, 0.0) };
            var xs = StructuralGridGenerator.Generate(cols)
                .Where(g => !g.IsAlongX)
                .Select(g => g.OrdMm)
                .ToList();
            Assert.Equal(new[] { 1000.0, 5000.0, 9000.0 }, xs);
        }
    }
}
