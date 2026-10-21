using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// EVERY MATERIAL A SECTION NAMES IS DEFINED (2026-10-20). The F2K's frame-section tables name the default grade (C30)
/// for every column and wall/beam section, while the material tables defined only the slabs' grades: a C35 slab with a
/// column under it gave a file whose column section pointed at a material it never defined.
/// WHAT THIS COVERS: every <c>Material=</c> in the written file is a row of MATERIAL PROPERTIES - GENERAL, for a slab
/// grade other than the default, with a column and a wall section. WHAT IT DOES NOT: that SAFE opens the file (no F2K
/// from this writer has been import-tested in SAFE); that C30 is the right material for those sections - it is the
/// writer's existing choice, now at least defined.
/// </summary>
public sealed class EveryMaterialASectionNamesIsDefinedTests
{
    [Fact]
    public void ASlabGradeOtherThanTheSectionsDefaultStillDefinesTheSectionsMaterial()
    {
        var ring = new List<(double X, double Y)> { (0, 0), (10000, 0), (10000, 10000), (0, 10000) };
        var story = new F2kStoryData(
            new() { ("P1", 0, 0, 0), ("P2", 10000, 0, 0), ("P3", 10000, 10000, 0), ("P4", 0, 10000, 0), ("P5", 5000, 5000, 0), ("P6", 2000, 2000, 0), ("P7", 2000, 8000, 0) },
            new() { ("A1", new List<string> { "P1", "P2", "P3", "P4" }, ring, "S250C35", 250, "C35", ((byte)0, (byte)0, (byte)0)) },
            new(),
            new() { ("L1", "P6", "P7", 6000, "W200x1000") },
            new() { "P5" },
            new() { ("C400x400", 400, 400) },
            new(),
            new() { ring },
            new() { (5000, 5000) });

        string f2k = Path.Combine(Path.GetTempPath(), $"kor-safe-grade-{Guid.NewGuid():N}.f2k");
        try
        {
            using (var sw = new StreamWriter(f2k, false, Encoding.ASCII))
                F2kWriter.WriteTables(sw, new[] { story }, null, new ExportSettings(), new List<StructuralGridLine>(), CultureInfo.InvariantCulture);

            var lines = File.ReadAllLines(f2k, Encoding.ASCII);
            int start = Array.FindIndex(lines, l => l.Contains("\"MATERIAL PROPERTIES - GENERAL\""));
            Assert.True(start >= 0, "no material table written");
            var defined = lines.Skip(start + 1).TakeWhile(l => l.Trim().Length > 0)
                .Select(l => Regex.Match(l, @"Material=(\S+)").Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var named = lines.SelectMany(l => Regex.Matches(l, @"(?<![\w""])Material=(\S+)").Select(m => m.Groups[1].Value))
                .Distinct(StringComparer.Ordinal).ToList();

            Assert.Contains("C35", defined);
            var undefined = named.Where(n => !defined.Contains(n)).ToList();
            Assert.True(undefined.Count == 0, "sections name materials the file never defines: " + string.Join(", ", undefined));
        }
        finally { File.Delete(f2k); }
    }
}
