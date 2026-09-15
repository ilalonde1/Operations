#nullable enable
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Kor.Operations.EngineeringTools.Dxf;
using RawContentSubpath = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.RawSubpath;
using static Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader;

namespace Kor.Operations.EngineeringTools.PdfToSafe;

/// <summary>
/// The PDF walk, keyed only by content SHA-256, page and curve tessellation. Thinning and reader
/// rules run after this record. Bump Version when the walk, word extraction or annotation rules
/// change. A bad record is a miss; a write that cannot be verified fails visibly.
/// </summary>
public sealed class PageReadCache(string? root = null)
{
    private const byte Version = 1;
    private static readonly object[] Gates = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
    private static readonly uint[] CrcTable = MakeCrcTable();

    public RawPage GetOrWalk(string pdfSha256, int page, int curveSegments, Func<RawPage> walk)
    {
        ArgumentNullException.ThrowIfNull(pdfSha256);
        ArgumentNullException.ThrowIfNull(walk);
        if (pdfSha256.Length != 64 || !pdfSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("A PDF content SHA-256 must contain 64 hexadecimal characters.", nameof(pdfSha256));
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        string hash = pdfSha256.ToUpperInvariant();
        string directory = Path.Combine(root ?? DrawingMirror.Root, "pages", hash);
        string path = Path.Combine(directory, FormattableString.Invariant($"{page}-{curveSegments}.bin"));
        // Bounded, process-wide locks: two callers of the same key walk once, even across instances.
        // Independent processes can both walk a cold key; each publishes a complete verified file.
        uint stripe = unchecked((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(Path.GetFullPath(path))) % (uint)Gates.Length;
        lock (Gates[stripe])
        {
            try { return Read(path, hash, page, curveSegments); }
            catch (Exception ex)
            {
                Trace.TraceInformation($"PageReadCache miss, page {page}: {ex.GetType().Name}: {ex.Message}");
            }

            var raw = walk();
            if (raw.PageNumber != page) throw new InvalidDataException("The walk returned a different page than the cache key.");
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var gzip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true))
                    using (var writer = new BinaryWriter(gzip, Encoding.UTF8, leaveOpen: false))
                        Write(writer, hash, curveSegments, raw);
                    file.Flush(flushToDisk: true);
                }
                // Read the actual gzip and all fields back before publishing it. Comparison uses
                // the in-memory source, not reserialization by the same potentially faulty writer.
                if (!Same(raw, Read(temporary, hash, page, curveSegments)))
                    throw new IOException("The page-walk cache did not round-trip bit for bit after writing.");
                File.Move(temporary, path, overwrite: true);
                return raw;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static void Write(BinaryWriter w, string hash, int curveSegments, RawPage raw)
    {
        w.Write(Version); w.Write(hash); w.Write(curveSegments);
        w.Write(raw.PageNumber); w.Write(raw.WidthPts); w.Write(raw.HeightPts);
        w.Write(raw.Words.Count);
        foreach (var word in raw.Words)
        {
            w.Write(word.Text); w.Write(word.Cx); w.Write(word.Cy);
            w.Write(word.MinX); w.Write(word.MinY); w.Write(word.MaxX); w.Write(word.MaxY);
        }
        w.Write(raw.Subpaths.Count);
        foreach (var sub in raw.Subpaths)
        {
            w.Write(sub.PathOrdinal); w.Write(sub.SubpathOrdinal); WritePoints(w, sub.Points);
            w.Write(sub.HasCloseCommand); w.Write(sub.IsFilled); w.Write(sub.IsStroked);
            w.Write(sub.LineWidth); WriteColor(w, sub.Color); w.Write(sub.IsClipping);
        }
        w.Write(raw.AnnotationPaths.Count);
        foreach (var path in raw.AnnotationPaths)
        {
            WritePoints(w, path.Points);
            w.Write(path.IsClosed); w.Write(path.IsFilled); w.Write(path.IsStroked);
            w.Write(path.MinX); w.Write(path.MinY); w.Write(path.MaxX); w.Write(path.MaxY);
            WriteColor(w, path.Color); w.Write(path.IsAnnotation); w.Write(path.LineWidth);
            w.Write(path.IsClipping); w.Write(path.PathOrdinal);
        }
    }

    private static RawPage Read(string path, string hash, int page, int curveSegments)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length < 18) throw new InvalidDataException("A page-walk gzip is short.");
        // Some decompressors accept a truncated gzip footer after returning the entire payload.
        // Check its CRC and size explicitly so that a killed/truncated record always misses.
        file.Position = file.Length - 8;
        Span<byte> footer = stackalloc byte[8]; file.ReadExactly(footer);
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(footer);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(footer[4..]);
        file.Position = 0;
        using var payload = new MemoryStream();
        using (var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: true)) gzip.CopyTo(payload);
        if (unchecked((uint)payload.Length) != size || Crc32(payload.GetBuffer().AsSpan(0, checked((int)payload.Length))) != crc)
            throw new InvalidDataException("The page-walk gzip footer does not match its payload.");
        payload.Position = 0;
        using var r = new BinaryReader(payload, Encoding.UTF8, leaveOpen: true);
        if (r.ReadByte() != Version) throw new InvalidDataException("The page-walk version changed.");
        if (r.ReadString() != hash || r.ReadInt32() != curveSegments)
            throw new InvalidDataException("The page-walk content hash or tessellation does not match its key.");
        int number = r.ReadInt32();
        if (number != page) throw new InvalidDataException("The page-walk page number does not match its key.");
        double width = r.ReadDouble(), height = r.ReadDouble();
        int count = ReadCount(r, 49);
        var words = new List<TextToken>(count);
        for (int i = 0; i < count; i++)
            words.Add(new TextToken(r.ReadString(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble()));
        count = ReadCount(r, 27);
        var subpaths = new List<RawContentSubpath>(count);
        for (int i = 0; i < count; i++)
        {
            int pathOrdinal = r.ReadInt32(), subpathOrdinal = r.ReadInt32();
            var points = ReadPoints(r);
            if (points.Count < 2) throw new InvalidDataException("A raw content subpath must have at least two points.");
            subpaths.Add(new RawContentSubpath(pathOrdinal, subpathOrdinal, points,
                r.ReadBoolean(), r.ReadBoolean(), r.ReadBoolean(), r.ReadDouble(), ReadColor(r), r.ReadBoolean()));
        }
        count = ReadCount(r, 56);
        var annotations = new List<GeomPath>(count);
        for (int i = 0; i < count; i++)
        {
            var points = ReadPoints(r);
            annotations.Add(new GeomPath(points, r.ReadBoolean(), r.ReadBoolean(), r.ReadBoolean(),
                r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble())
            {
                Color = ReadColor(r), IsAnnotation = r.ReadBoolean(), LineWidth = r.ReadDouble(),
                IsClipping = r.ReadBoolean(), PathOrdinal = r.ReadInt32(),
            });
        }
        if (payload.Position != payload.Length) throw new InvalidDataException("The page-walk record contains trailing data.");
        return new RawPage(number, width, height, words, subpaths, annotations);
    }

    private static int ReadCount(BinaryReader r, int minimumBytesPerItem)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > (r.BaseStream.Length - r.BaseStream.Position) / minimumBytesPerItem)
            throw new InvalidDataException("A page-walk collection count exceeds its remaining data.");
        return count;
    }
    private static void WritePoints(BinaryWriter w, IReadOnlyList<(double X, double Y)> points)
    {
        w.Write(points.Count);
        foreach (var p in points) { w.Write(p.X); w.Write(p.Y); }
    }
    private static List<(double X, double Y)> ReadPoints(BinaryReader r)
    {
        int count = ReadCount(r, 16);
        var points = new List<(double X, double Y)>(count);
        for (int i = 0; i < count; i++) points.Add((r.ReadDouble(), r.ReadDouble()));
        return points;
    }
    private static void WriteColor(BinaryWriter w, (byte R, byte G, byte B) color)
    { w.Write(color.R); w.Write(color.G); w.Write(color.B); }
    private static (byte R, byte G, byte B) ReadColor(BinaryReader r) => (r.ReadByte(), r.ReadByte(), r.ReadByte());

    private static bool Bits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    private static bool PointsEqual(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
        => a.Count == b.Count && a.Zip(b).All(p => Bits(p.First.X, p.Second.X) && Bits(p.First.Y, p.Second.Y));
    private static bool Same(RawPage a, RawPage b)
    {
        if (a.PageNumber != b.PageNumber || !Bits(a.WidthPts, b.WidthPts) || !Bits(a.HeightPts, b.HeightPts)
            || a.Words.Count != b.Words.Count || a.Subpaths.Count != b.Subpaths.Count || a.AnnotationPaths.Count != b.AnnotationPaths.Count) return false;
        for (int i = 0; i < a.Words.Count; i++)
        {
            var x = a.Words[i]; var y = b.Words[i];
            if (x.Text != y.Text || !Bits(x.Cx, y.Cx) || !Bits(x.Cy, y.Cy) || !Bits(x.MinX, y.MinX)
                || !Bits(x.MinY, y.MinY) || !Bits(x.MaxX, y.MaxX) || !Bits(x.MaxY, y.MaxY)) return false;
        }
        for (int i = 0; i < a.Subpaths.Count; i++)
        {
            var x = a.Subpaths[i]; var y = b.Subpaths[i];
            if (x.PathOrdinal != y.PathOrdinal || x.SubpathOrdinal != y.SubpathOrdinal || !PointsEqual(x.Points, y.Points)
                || x.HasCloseCommand != y.HasCloseCommand || x.IsFilled != y.IsFilled || x.IsStroked != y.IsStroked
                || !Bits(x.LineWidth, y.LineWidth) || x.Color != y.Color || x.IsClipping != y.IsClipping) return false;
        }
        for (int i = 0; i < a.AnnotationPaths.Count; i++)
        {
            var x = a.AnnotationPaths[i]; var y = b.AnnotationPaths[i];
            if (!PointsEqual(x.Points, y.Points) || x.IsClosed != y.IsClosed || x.IsFilled != y.IsFilled || x.IsStroked != y.IsStroked
                || !Bits(x.MinX, y.MinX) || !Bits(x.MinY, y.MinY) || !Bits(x.MaxX, y.MaxX) || !Bits(x.MaxY, y.MaxY)
                || x.Color != y.Color || x.IsAnnotation != y.IsAnnotation || !Bits(x.LineWidth, y.LineWidth)
                || x.IsClipping != y.IsClipping || x.PathOrdinal != y.PathOrdinal) return false;
        }
        return true;
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint c = i;
            for (int bit = 0; bit < 8; bit++) c = (c & 1) != 0 ? 0xedb88320U ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in bytes) crc = CrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        return ~crc;
    }
}
