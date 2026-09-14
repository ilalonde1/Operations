#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// WHAT THIS COVERS: a six-set reading keyed by PDF bytes, scale, reader source paths/bytes and
/// serialised options, with the analyzer's sheet CSV and the original page/summary counts.
/// WHAT IT DOES NOT: hash DXF contents or restore reader-side static state, assemblies, diagnostic
/// totals or in-memory views. Like the analyzer, recompose reads the DXF outlet and rebuilds levels.
/// A same-class fault this cannot catch is a composer relying on static state populated only by a
/// full read; options in the key do not replace initialising that state on the recompose path.
/// </summary>
internal static class SixSetReadCache
{
    internal const string ManifestName = "read-cache.json";
    private const int Version = 1;
    private static readonly string[] DxfReaderFiles =
    [
        "PlanSheetNaming.cs", "DrawingVocabulary.cs", "DxfSheet.cs", "DxfModels.cs",
        "LoopGeometry.cs", "PlanLoopBuilder.cs", "DashedLineJoiner.cs", "MatchLineSheetJoin.cs",
        "GridAlignment.cs", "StructuralPlanClassifier.cs", "RuleSettings.cs",
    ];
    private static readonly HashSet<string> IntakeExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "CorpusAnalyzer.cs", "CorpusDiff.cs", "SheetDiff.cs", "SetCheck.cs", "StoreysFromPlans.cs",
    };

    internal sealed record Fingerprint(string PdfSha256, int Scale, string ReaderSha256);
    internal sealed record Manifest(int Version, Fingerprint Inputs, DateTime ReadAtUtc, int Pages,
        int Written, int Empty, int NotPlan, int Failed, string SheetsSha256);
    internal sealed record CachedRead(Manifest Manifest, PdfOnlyBuild.SheetsResult Sheets);

    internal static string RepositoryRoot(string start)
    {
        for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core.Tests.csproj")))
                return dir.Parent?.FullName ?? throw new DirectoryNotFoundException("The tests project has no parent directory.");
        throw new DirectoryNotFoundException("Cannot find Kor.Operations.EngineeringTools.Core.Tests.csproj above the test output.");
    }

    internal static string ReaderHash(string root, PdfIntakeOptions options)
    {
        string core = Path.Combine(root, "Kor.Operations.EngineeringTools.Core");
        var paths = Directory.EnumerateFiles(Path.Combine(core, "PdfToSafe"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(core, "Intake"), "*.cs", SearchOption.AllDirectories)
                .Where(p => !IntakeExcluded.Contains(Path.GetFileName(p))))
            .Concat(DxfReaderFiles.Select(f => Path.Combine(core, "Dxf", f)))
            // Already in Intake; explicit inclusion makes moving/removing the wrapper fail safely.
            .Append(Path.Combine(core, "Intake", "PdfOnlyBuild.cs"))
            .Select(p => Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'))
            .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal);
        var text = new StringBuilder();
        foreach (string path in paths)
            text.Append(path).Append('\n').Append(HashFile(Path.Combine(root, path))).Append('\n');
        // Record.ToString() prints collection types, not their words. JSON includes every list value.
        text.Append("options\n").Append(JsonSerializer.Serialize(options));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    internal static Fingerprint Inputs(string pdf, int scale, string readerHash) => new(HashFile(pdf), scale, readerHash);

    internal static CachedRead? Read(string work, Fingerprint expected, out string reason)
    {
        string path = Path.Combine(work, ManifestName), csv = Path.Combine(work, "sheets.csv"), dxf = Path.Combine(work, "dxf");
        reason = "manifest missing";
        if (!File.Exists(path)) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path));
            reason = "manifest invalid or version changed";
            if (saved is null || saved.Version != Version || saved.Inputs is null || saved.Pages <= 0) return null;
            reason = "manifest changed: PDF";
            if (saved.Inputs.PdfSha256 != expected.PdfSha256) return null;
            reason = "manifest changed: scale";
            if (saved.Inputs.Scale != expected.Scale) return null;
            reason = "manifest changed: reader source or options";
            if (saved.Inputs.ReaderSha256 != expected.ReaderSha256) return null;
            reason = "sheets.csv missing";
            if (!File.Exists(csv)) return null;
            reason = "dxf directory missing";
            if (!Directory.Exists(dxf)) return null;
            reason = "sheets.csv changed";
            if (HashFile(csv) != saved.SheetsSha256) return null;
            var rows = CorpusAnalyzer.ReadSheetRows(csv, Guid.Empty).ToList();
            var files = rows.SelectMany(r => CorpusAnalyzer.DxfFilesOf(r.DxfFiles)).ToHashSet(StringComparer.Ordinal);
            reason = "dxf views missing or changed file list";
            if (files.Count == 0 || !files.SetEquals(Directory.EnumerateFiles(dxf, "*.dxf").Select(f => Path.GetFileName(f)!))) return null;
            var sheets = new PdfOnlyBuild.SheetsResult(
                rows.Select(r => new PdfOnlyBuild.SheetOutcome(r.Page, r.SheetNumber, r.SheetType, r.Title, r.Level,
                    r.ScaleNote, r.ScaleDenominator, 0, 0, r.Slabs, r.Columns, r.Walls, 0, r.Lines,
                    CorpusAnalyzer.DxfFilesOf(r.DxfFiles), r.SelfCheck ?? "", r.Failure)).ToList(),
                [], saved.Written, saved.Empty, saved.NotPlan, saved.Failed, 0, 0, 0, 0, 0, 0, 0);
            reason = "manifest matched";
            return new CachedRead(saved, sheets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or FormatException or OverflowException or ArgumentException or IndexOutOfRangeException)
        {
            reason = $"read cache invalid: {ex.GetType().Name}";
            return null;
        }
    }

    internal static void Save(string work, string job, Fingerprint inputs, PdfOnlyBuild.BuildOutcome built)
    {
        Directory.CreateDirectory(work);
        string path = Path.Combine(work, ManifestName), csv = Path.Combine(work, "sheets.csv");
        File.Delete(path); // A failed write must not leave an earlier manifest authorising the new rows.
        CorpusAnalyzer.WriteSheetCsv(csv, CorpusAnalyzer.SheetRows(built, Guid.Empty, job));   // the analyzer's own projection: one sheets.csv, two writers
        var s = built.Sheets;
        var manifest = new Manifest(Version, inputs, DateTime.UtcNow, built.Pages,
            s.Written, s.Empty, s.NotPlan, s.Failed, HashFile(csv));
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        if (JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) != manifest)
            throw new IOException("The read-cache manifest did not round-trip after writing.");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
