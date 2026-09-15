#nullable enable
using System.IO.Compression;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using RawContentSubpath = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.RawSubpath;
using static Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// WHAT THIS COVERS: hand-built raw pages replay sequential thinning, strict closure, Close commands,
/// ordinal gaps, annotation opt-in and scale projection; disk records preserve every field and
/// double bit, walk once per key, and recover from bad versions, truncation and key mismatches.
/// WHAT IT DOES NOT: prove Walk/Derive equals the old walk on a real PDF, exercise PdfPig's Bezier,
/// glyph or annotation extraction, or measure corpus performance. The six-set gate must compare
/// finished models byte for byte. A same-class fault missed here is a Bezier sample formula/order
/// changing in Walk: these fixtures start with already flattened points.
/// </summary>
public sealed class APageIsWalkedOnceTests
{
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    private static RawContentSubpath Sub(int ordinal, params (double X, double Y)[] points)
        => new(3, ordinal, points, false, true, false, 0.125, (16, 32, 48), true);
    private static RawPage Page(params RawContentSubpath[] subpaths)
        => new(4, 612.25, 792.125, [], subpaths, []);

    [Theory]
    [InlineData(0.4)]
    [InlineData(0.5)]
    public void ThinningComparesToTheLastKeptPointAndIncludesTheBoundary(double spacing)
    {
        var raw = Page(Sub(7, (0, 0), (spacing, 0), (2 * spacing, 0)));
        var ordinals = new List<int>();
        var derived = Derive(raw, false, minPointDistance: 0.5, keptSubpathOrdinals: ordinals);
        var path = Assert.Single(derived.Paths);
        Assert.Equal(new[] { (0.0, 0.0), (2 * spacing, 0.0) }, path.Points);
        Assert.Equal(7, Assert.Single(ordinals));
        Assert.Equal(3, raw.Subpaths[0].Points.Count); // Derivation did not thin the stored record.
        Assert.Equal(3, path.PathOrdinal);
        Assert.True(path.IsClipping);
        Assert.True(path.IsFilled);
        Assert.False(path.IsStroked);
        Assert.False(path.IsAnnotation);
        Assert.Equal((16, 32, 48), ((int)path.Color.R, (int)path.Color.G, (int)path.Color.B));
        EqualDouble(0.125, path.LineWidth);
        EqualDouble(0, path.MinX); EqualDouble(0, path.MinY);
        EqualDouble(2 * spacing, path.MaxX); EqualDouble(0, path.MaxY);
    }

    [Fact]
    public void ASubpathThatThinsAwayDoesNotRenumberItsNeighbour()
    {
        var raw = Page(Sub(7, (0, 0), (0.4, 0)), Sub(11, (5, 0), (6, 0)) with { PathOrdinal = 8 });
        var kept = new List<int> { 99 }; // The API appends; it does not clear its caller's list.
        var derived = Derive(raw, false, minPointDistance: 0.5, keptSubpathOrdinals: kept);
        Assert.Equal(8, Assert.Single(derived.Paths).PathOrdinal);
        Assert.Equal(new[] { 99, 11 }, kept);
        var fullKept = new List<int>();
        Assert.Equal(2, Derive(raw, false, keptSubpathOrdinals: fullKept).Paths.Count);
        Assert.Equal(new[] { 7, 11 }, fullKept);
    }

    [Fact]
    public void ExplicitClosureDistanceRemovesTheLastPointButOnlyWhenStrictlyCloser()
    {
        var raw = Page(Sub(0, (0, 0), (10, 0), (0.4, 0)));
        var closed = Assert.Single(Derive(raw, false, closeDistance: 0.5).Paths);
        Assert.True(closed.IsClosed);
        Assert.Equal(new[] { (0.0, 0.0), (10.0, 0.0) }, closed.Points);
        var atBoundary = Assert.Single(Derive(raw, false, closeDistance: 0.4).Paths);
        Assert.False(atBoundary.IsClosed);
        Assert.Equal(3, atBoundary.Points.Count);
        Assert.Equal(3, raw.Subpaths[0].Points.Count);
        // Closure is not attempted on a two-point polyline, even when its ends nearly meet.
        Assert.False(Assert.Single(Derive(Page(Sub(0, (0, 0), (0.1, 0))), false, closeDistance: 1).Paths).IsClosed);
    }

    [Fact]
    public void DefaultClosureUsesEachAxisBelowHalfAPointAndKeepsTheLastPoint()
    {
        var raw = Page(Sub(0, (0, 0), (10, 0), (0.49, 0.49)));
        var path = Assert.Single(Derive(raw, false).Paths);
        Assert.True(path.IsClosed); // Euclidean distance is MORE than .5; the legacy test is per-axis.
        Assert.Equal(3, path.Points.Count);
        Assert.Equal(raw.Subpaths[0].Points, path.Points);
        Assert.False(Assert.Single(Derive(Page(Sub(0, (0, 0), (10, 0), (0.5, 0))), false).Paths).IsClosed);
        Assert.False(Assert.Single(Derive(raw, false, closeDistance: 0.5).Paths).IsClosed);
    }

    [Fact]
    public void ACloseCommandClosesWithoutRemovingAnotherPoint()
    {
        var raw = Page(Sub(0, (0, 0), (10, 0), (0.1, 0)) with { HasCloseCommand = true });
        var path = Assert.Single(Derive(raw, false, closeDistance: 1).Paths);
        Assert.True(path.IsClosed);
        Assert.Equal(3, path.Points.Count);
        var far = Page(Sub(0, (0, 0), (100, 100)) with { HasCloseCommand = true });
        Assert.True(Assert.Single(Derive(far, false, closeDistance: 0).Paths).IsClosed);
    }

    [Fact]
    public void AnnotationsAreAppendedUnchangedOnlyWhenAskedAndNeverGetContentOrdinals()
    {
        var raw = Sample();
        var kept = new List<int>();
        var derived = Derive(raw, true, minPointDistance: 1000, closeDistance: 1000, keptSubpathOrdinals: kept);
        Assert.Empty(kept);
        AssertPath(raw.AnnotationPaths[0], Assert.Single(derived.Paths)); // Not thinned, closed or re-bounded.
        Assert.Empty(Derive(raw, false, minPointDistance: 1000).Paths);
        var all = Derive(raw, true);
        Assert.Equal(3, all.Paths.Count);
        Assert.False(all.Paths[0].IsAnnotation);
        Assert.False(all.Paths[1].IsAnnotation);
        AssertPath(raw.AnnotationPaths[0], all.Paths[2]);
        Assert.Equal(raw.PageNumber, all.PageNumber);
        EqualDouble(raw.WidthPts, all.WidthPts); EqualDouble(raw.HeightPts, all.HeightPts);
        Assert.Equal(raw.Words, all.Words);
    }

    [Fact]
    public void TheClassifierOverloadDerivesAtTheSameScaleAndPreservesMetadata()
    {
        const double scale = 2.5;
        var raw = Sample();
        var kept = new List<int>();
        var parsed = PdfPlanReader.ParsePage(raw, scale, out var content, kept);
        var expectedKept = new List<int>();
        var expected = Derive(raw, true, PdfToSafeConstants.MinVertexDistanceMm / scale,
            PdfToSafeConstants.MinVertexDistanceMm * 4.0 / scale, expectedKept);
        Assert.Equal(expectedKept, kept);
        Assert.Equal(expected.Paths.Count, content.Paths.Count);
        Assert.Equal(content.Paths.Count, parsed.Count);
        for (int i = 0; i < parsed.Count; i++)
        {
            AssertPath(expected.Paths[i], content.Paths[i]);
            var p = content.Paths[i]; var projected = parsed[i];
            Assert.Equal(p.Points.Select(x => (x.X * scale, x.Y * scale)).ToArray(), projected.Points);
            Assert.Equal(p.IsClosed, projected.IsClosed);
            Assert.Equal(p.IsFilled, projected.IsFilled);
            Assert.Equal(p.IsStroked, projected.IsStroked);
            Assert.Equal(p.Color, projected.Color);
            EqualDouble(p.LineWidth, projected.LineWidth);
            Assert.Equal(p.IsAnnotation, projected.IsAnnotation);
            Assert.Equal(p.IsClipping, projected.IsClipping);
            Assert.Equal(p.PathOrdinal, projected.PathOrdinal);
        }
    }

    [Fact]
    public void TwoCallsWalkOnceAndTheDiskRecordPreservesEveryDoubleBit()
    {
        using var disk = new CacheDirectory();
        var expected = Sample();
        int calls = 0;
        RawPage WalkOnce() { calls++; return expected; }
        var first = new PageReadCache(disk.Root).GetOrWalk(Hash, 4, 8, WalkOnce);
        var second = new PageReadCache(disk.Root).GetOrWalk(Hash.ToLowerInvariant(), 4, 8, WalkOnce);
        Assert.Equal(1, calls);
        AssertRaw(expected, first); AssertRaw(expected, second);
        Assert.NotSame(first, second); // This is a deserialize, not a process-local object cache.
        Assert.True(File.Exists(disk.PathFor(Hash, 4, 8)));
        Assert.Empty(Directory.EnumerateFiles(disk.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("truncated-body")]
    [InlineData("truncated-footer")]
    [InlineData("missing")]
    public void AnUnreadableRecordWalksAgainAndRewrites(string damage)
    {
        using var disk = new CacheDirectory();
        var cache = new PageReadCache(disk.Root);
        var expected = Sample();
        int calls = 0;
        RawPage WalkAgain() { calls++; return expected; }
        cache.GetOrWalk(Hash, 4, 8, WalkAgain);
        string path = disk.PathFor(Hash, 4, 8);
        if (damage == "version")
        {
            byte[] payload;
            using (var file = File.OpenRead(path))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var memory = new MemoryStream()) { gzip.CopyTo(memory); payload = memory.ToArray(); }
            payload[0] = byte.MaxValue; // Version byte is FIRST inside the decompressed record.
            using var fileOut = File.Create(path);
            using var gzipOut = new GZipStream(fileOut, CompressionLevel.Fastest);
            gzipOut.Write(payload);
        }
        else if (damage == "missing") File.Delete(path);
        else
        {
            byte[] bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes[..(damage == "truncated-footer" ? bytes.Length - 1 : bytes.Length / 2)]);
        }
        AssertRaw(expected, cache.GetOrWalk(Hash, 4, 8, WalkAgain));
        AssertRaw(expected, cache.GetOrWalk(Hash, 4, 8, () => throw new InvalidOperationException("The rewritten cache must hit.")));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void AnEmptyPageIsAValidCacheHit()
    {
        using var disk = new CacheDirectory();
        var cache = new PageReadCache(disk.Root);
        var expected = Page();
        cache.GetOrWalk(Hash, 4, 0, () => expected);
        var actual = cache.GetOrWalk(Hash, 4, 0, () => throw new InvalidOperationException("Empty does not mean missing."));
        AssertRaw(expected, actual);
        Assert.Empty(Derive(actual, true).Paths);
    }

    [Fact]
    public void AnUnverifiableWalkIsNotPublished()
    {
        using var disk = new CacheDirectory();
        var cache = new PageReadCache(disk.Root);
        // Walk never emits a one-point content subpath. Read-back must reject an invalid record.
        Assert.Throws<InvalidDataException>(() => cache.GetOrWalk(Hash, 4, 8, () => Page(Sub(7, (0, 0)))));
        Assert.False(File.Exists(disk.PathFor(Hash, 4, 8)));
        Assert.Empty(Directory.EnumerateFiles(disk.Root, "*.tmp", SearchOption.AllDirectories));
        AssertRaw(Sample(), cache.GetOrWalk(Hash, 4, 8, Sample));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("page")]
    [InlineData("curves")]
    public void AnotherKeyCannotReuseAWalkOrAcceptARecordCopiedUnderItsName(string dimension)
    {
        using var disk = new CacheDirectory();
        var cache = new PageReadCache(disk.Root);
        cache.GetOrWalk(Hash, 4, 8, Sample);
        string hash = dimension == "hash" ? new string('A', 64) : Hash;
        int page = dimension == "page" ? 5 : 4, curves = dimension == "curves" ? 12 : 8;
        string destination = disk.PathFor(hash, page, curves);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(disk.PathFor(Hash, 4, 8), destination);
        int calls = 0;
        var expected = Sample() with { PageNumber = page };
        var actual = cache.GetOrWalk(hash, page, curves, () => { calls++; return expected; });
        Assert.Equal(1, calls);
        AssertRaw(expected, actual);
        AssertRaw(expected, cache.GetOrWalk(hash, page, curves, () => throw new InvalidOperationException("Expected the repaired key to hit.")));
    }

    private static RawPage Sample()
    {
        double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        return new RawPage(4, Math.BitIncrement(612.25), 792.125,
            [new TextToken("LEVEL é ½", Math.BitIncrement(1.25), 2.125, negativeZero, -1.75, 4.5, 8.875)],
            [Sub(7, (negativeZero, 0), (0.4, 0), (0.8, 0)),
             Sub(11, (5.125, -2.25), (6.375, 3.5), (5.25, -2.125)) with
             { PathOrdinal = 8, HasCloseCommand = true, IsFilled = false, IsStroked = true,
                 LineWidth = Math.BitIncrement(0.25), Color = (240, 128, 0), IsClipping = false }],
            [new GeomPath([(1.125, 2.25), (1.25, 2.375), (1.375, 2.5)], false, true, true,
                -3.125, -4.25, 7.5, 8.75)
                { Color = (32, 64, 96), IsAnnotation = true, LineWidth = negativeZero, IsClipping = true, PathOrdinal = -1 }]);
    }

    private static void EqualDouble(double expected, double actual)
    {
        Assert.True(expected == actual);
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }
    private static void AssertPoints(IReadOnlyList<(double X, double Y)> expected, IReadOnlyList<(double X, double Y)> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++) { EqualDouble(expected[i].X, actual[i].X); EqualDouble(expected[i].Y, actual[i].Y); }
    }
    private static void AssertPath(GeomPath expected, GeomPath actual)
    {
        AssertPoints(expected.Points, actual.Points);
        Assert.Equal(expected.IsClosed, actual.IsClosed); Assert.Equal(expected.IsFilled, actual.IsFilled);
        Assert.Equal(expected.IsStroked, actual.IsStroked); Assert.Equal(expected.Color, actual.Color);
        EqualDouble(expected.MinX, actual.MinX); EqualDouble(expected.MinY, actual.MinY);
        EqualDouble(expected.MaxX, actual.MaxX); EqualDouble(expected.MaxY, actual.MaxY);
        Assert.Equal(expected.IsAnnotation, actual.IsAnnotation); EqualDouble(expected.LineWidth, actual.LineWidth);
        Assert.Equal(expected.IsClipping, actual.IsClipping); Assert.Equal(expected.PathOrdinal, actual.PathOrdinal);
    }
    private static void AssertRaw(RawPage expected, RawPage actual)
    {
        Assert.Equal(expected.PageNumber, actual.PageNumber);
        EqualDouble(expected.WidthPts, actual.WidthPts); EqualDouble(expected.HeightPts, actual.HeightPts);
        Assert.Equal(expected.Words.Count, actual.Words.Count);
        for (int i = 0; i < expected.Words.Count; i++)
        {
            var e = expected.Words[i]; var a = actual.Words[i];
            Assert.Equal(e.Text, a.Text); EqualDouble(e.Cx, a.Cx); EqualDouble(e.Cy, a.Cy);
            EqualDouble(e.MinX, a.MinX); EqualDouble(e.MinY, a.MinY); EqualDouble(e.MaxX, a.MaxX); EqualDouble(e.MaxY, a.MaxY);
        }
        Assert.Equal(expected.Subpaths.Count, actual.Subpaths.Count);
        for (int i = 0; i < expected.Subpaths.Count; i++)
        {
            var e = expected.Subpaths[i]; var a = actual.Subpaths[i];
            Assert.Equal(e.PathOrdinal, a.PathOrdinal); Assert.Equal(e.SubpathOrdinal, a.SubpathOrdinal);
            AssertPoints(e.Points, a.Points);
            Assert.Equal(e.HasCloseCommand, a.HasCloseCommand); Assert.Equal(e.IsFilled, a.IsFilled);
            Assert.Equal(e.IsStroked, a.IsStroked); EqualDouble(e.LineWidth, a.LineWidth);
            Assert.Equal(e.Color, a.Color); Assert.Equal(e.IsClipping, a.IsClipping);
        }
        Assert.Equal(expected.AnnotationPaths.Count, actual.AnnotationPaths.Count);
        for (int i = 0; i < expected.AnnotationPaths.Count; i++) AssertPath(expected.AnnotationPaths[i], actual.AnnotationPaths[i]);
    }

    private sealed class CacheDirectory : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "kor-page-cache-tests-" + Guid.NewGuid().ToString("N"));
        internal string PathFor(string hash, int page, int curves)
            => Path.Combine(Root, "pages", hash, $"{page}-{curves}.bin");
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
