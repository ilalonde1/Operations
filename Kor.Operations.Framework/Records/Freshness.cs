using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Records;

public enum Verdict { Current, Stale, Unverifiable }

/// <summary>One record as freshness saw it. HeadBlob is lowercase (or null: path missing / not read).</summary>
public sealed record Checked(int Id, string Lane, string Name, string Kind, string SourceKind, string? Repo, string? Path,
                             string? Blob, string? HeadBlob, Verdict Verdict, string Why);

/// <param name="Partial">the time budget ran out: some rows are Unverifiable "cap hit" and were not written.</param>
public sealed record FreshnessReport(IReadOnlyList<Checked> Rows, IReadOnlyDictionary<string, string?> RepoHeads, bool Written,
                                     bool Partial = false, TimeSpan Elapsed = default)
{
    public int Count(Verdict v) => Rows.Count(r => r.Verdict == v);
}

/// <summary>What git says is at HEAD: the commit and each asked path's blob (a path missing at HEAD is absent).</summary>
public sealed record HeadTree(string Commit, IReadOnlyDictionary<string, string> Blobs);

/// <summary>
/// K5 - NOTHING STALE (Framework docs/KNOWLEDGE-IDE-SPEC-K5-K6.md). A live record (not retired, Status current|stale,
/// SourceKind git|file) is CURRENT when its cited blob is the blob at HEAD of its repo, STALE when the blob differs or the
/// path is gone, UNVERIFIABLE (and left unwritten) when its repo is not on this machine or git fails. A 'file' source is
/// the SHA-256 of the file at SourceRef against SourceHash. One idempotent UPDATE writes Status + CheckedAtUtc and can
/// never write 'superseded'; a reverted file reads current again. Git output is lowercased before compare and write
/// (review row 102: CK_Memory_SourceBlob refuses uppercase, Latin1_General_BIN is case-sensitive).
/// Not covered: session/query/run-sourced records never go stale; a record that depends on another file than the one it
/// cites; contradiction by meaning. Same-class miss: a decision learned from Foo.cs and invalidated by Bar.cs stays current.
/// TODO(171): skip issues whose CheckKind is test/gate/query (their truth is their check, K5b) - the column does not
/// exist before 171, and on 168 every issue is a plain source-cited record.
/// </summary>
public static class Freshness
{
    /// <summary>Reads HEAD and the blobs of <paramref name="paths"/> in the repo at <paramref name="repoDir"/>; null = git failed.</summary>
    public delegate HeadTree? HeadReader(string repoDir, IReadOnlyCollection<string> paths);

    private const string Live =
        "m.RetiredAtUtc IS NULL AND m.Status IN (N'current', N'stale') AND m.SourceKind IN (N'git', N'file')";

    /// <param name="budget">time cap (the SessionStart hook); null = none. See <see cref="FreshnessReport.Partial"/>.</param>
    public static FreshnessReport CheckModule(BrainDb db, string module, string? fromDir = null, HeadReader? reader = null,
                                              TimeSpan? budget = null) =>
        Check(db, $"{Live} AND m.Lane = @a", module, null, fromDir, reader, write: true, budget);

    /// <summary>Every live record citing the repo (any lane) - after a commit, merge or checkout in it.</summary>
    public static FreshnessReport CheckRepo(BrainDb db, string repoLeaf, string? fromDir = null, HeadReader? reader = null) =>
        Check(db, $"{Live} AND m.SourceKind = N'git' AND m.SourceRepo = @a", repoLeaf, null, fromDir, reader, write: true);

    /// <summary>
    /// FileChanged: the records citing this file, compared against the file AS IT IS NOW in the working tree
    /// (<c>git hash-object</c>, the blob it would commit as). Writes nothing - HEAD has not moved; the commit re-checks.
    /// </summary>
    public static FreshnessReport CheckPath(BrainDb db, string absPath)
    {
        var full = Path.GetFullPath(absPath);
        var dir = Path.GetDirectoryName(full);
        while (dir is not null && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
        var top = dir is null ? null : Git.TopLevel(dir);
        if (top is null) return new FreshnessReport(Array.Empty<Checked>(), new Dictionary<string, string?>(), false);
        var rel = RecordStore.Relative(top, full);
        var leaf = Git.Leaf(top)!;
        HeadTree? Working(string repoDir, IReadOnlyCollection<string> paths)
        {
            var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in paths)
            {
                var abs = Path.Combine(repoDir, p);
                if (!File.Exists(abs)) continue;   // deleted in the working tree: reads as missing
                if (RecordStore.Lower(Git.Run(repoDir, TimeSpan.FromSeconds(3), "hash-object", "--", p)?.Trim()) is { } b) blobs[p] = b;
                else return null;
            }
            return new HeadTree("working-tree", blobs);
        }
        return Check(db, $"{Live} AND m.SourceKind = N'git' AND m.SourceRepo = @a AND m.SourcePath = @b", leaf, rel, top,
                     Working, write: false);
    }

    private sealed record Row(int Id, string Lane, string Name, string Kind, string SourceKind, string? Repo, string? Path,
                              string? Blob, string? Ref, string? Hash);

    private static FreshnessReport Check(BrainDb db, string where, string a, string? b, string? fromDir, HeadReader? reader,
                                         bool write, TimeSpan? budget = null)
    {
        reader ??= GitHead;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var partial = false;
        // The clock starts before the DB read: the cap covers the whole check, as the hook timeout does. The budget is checked before each repo's git read and before the file sources: past it, what is left is
        // reported Unverifiable ("cap hit") and NOT written; what was already checked is written as usual.
        bool Over() => budget is { } cap && clock.Elapsed >= cap && (partial = true);
        string CapWhy() => $"cap hit after {Secs(clock.Elapsed)} s - not checked";
        var rows = Load(db, where, a, b);
        var results = new List<Checked>();
        var heads = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var group in rows.Where(r => r.SourceKind == "git").GroupBy(r => r.Repo ?? ""))
        {
            if (Over())
            {
                results.AddRange(group.Select(r => Result(r, null, Verdict.Unverifiable, CapWhy())));
                continue;
            }
            var dir = group.Key.Length == 0 ? null : RepoLocator.Find(group.Key, fromDir);
            if (dir is null)
            {
                heads[group.Key] = null;
                results.AddRange(group.Select(r => Result(r, null, Verdict.Unverifiable,
                    $"repo {group.Key} is not on this machine beside {Git.TopLevel(fromDir ?? Environment.CurrentDirectory) ?? "the current folder"}")));
                continue;
            }
            var tree = reader(dir, group.Select(r => r.Path!).Distinct(StringComparer.Ordinal).ToList());
            if (tree is null)
            {
                heads[group.Key] = null;
                results.AddRange(group.Select(r => Result(r, null, Verdict.Unverifiable, $"git failed in {dir}")));
                continue;
            }
            heads[group.Key] = Lower(tree.Commit);
            foreach (var r in group)
            {
                var head = tree.Blobs.TryGetValue(r.Path!, out var hb) ? Lower(hb) : null;
                var cited = Lower(r.Blob);
                results.Add(head is null
                    ? Result(r, null, Verdict.Stale, $"{r.Path} is gone at {Short(tree.Commit)}")
                    : head == cited
                        ? Result(r, head, Verdict.Current, $"same blob at {Short(tree.Commit)}")
                        : Result(r, head, Verdict.Stale, $"{r.Path} changed: cited {Short(cited)}, now {Short(head)} at {Short(tree.Commit)}"));
            }
        }
        foreach (var r in rows.Where(r => r.SourceKind == "file"))
        {
            if (Over()) { results.Add(Result(r, null, Verdict.Unverifiable, CapWhy())); continue; }
            string? hash;
            try { hash = r.Ref is not null && File.Exists(r.Ref) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(r.Ref))).ToLowerInvariant() : null; }
            catch (Exception e) { results.Add(Result(r, null, Verdict.Unverifiable, $"cannot read {r.Ref}: {e.Message}")); continue; }
            var cited = r.Hash?.Trim().ToLowerInvariant();
            results.Add(hash is null ? Result(r, null, Verdict.Stale, $"{r.Ref} is gone")
                      : hash == cited ? Result(r, hash, Verdict.Current, "same SHA-256")
                      : Result(r, hash, Verdict.Stale, $"{r.Ref} changed (SHA-256)"));
        }
        results.Sort((x, y) => x.Id.CompareTo(y.Id));
        if (write) Write(db, results);
        return new FreshnessReport(results, heads, write, partial, clock.Elapsed);
    }

    private static Checked Result(Row r, string? head, Verdict v, string why) =>
        new(r.Id, r.Lane, r.Name, r.Kind, r.SourceKind, r.Repo, r.SourceKind == "git" ? r.Path : r.Ref,
            r.SourceKind == "git" ? r.Blob : r.Hash, head, v, why);

    /// <summary>
    /// The one write. The DATABASE decides current vs stale by comparing the head it is given with the cited blob/hash
    /// (binary collation); Unverifiable rows are not sent. Only rows still live and current|stale are touched, so it can
    /// never write 'superseded' nor revive a superseded or retired record. A missing path is sent as '' (never equal).
    /// </summary>
    internal static void Write(BrainDb db, IReadOnlyList<Checked> results)
    {
        var send = results.Where(r => r.Verdict != Verdict.Unverifiable)
                          .Select(r => new { r.Id, Head = r.HeadBlob ?? "" }).ToList();
        if (send.Count == 0) return;
        using var c = db.Command(
            "UPDATE m SET Status = CASE WHEN j.Head COLLATE Latin1_General_BIN = " +
            // the file hash compared as C# compares it: trimmed and lowercased (review row 139); a git blob is already
            // lowercase-only (CK_Memory_SourceBlob)
            "    (CASE m.SourceKind WHEN N'git' THEN CAST(m.SourceBlob AS nvarchar(64)) ELSE LOWER(LTRIM(RTRIM(m.SourceHash))) END) COLLATE Latin1_General_BIN " +
            "  THEN N'current' ELSE N'stale' END, CheckedAtUtc = SYSDATETIMEOFFSET() " +
            "FROM brain.Memory m JOIN OPENJSON(@j) WITH (Id int, Head nvarchar(64)) j ON j.Id = m.Id " +
            "WHERE m.Status IN (N'current', N'stale') AND m.RetiredAtUtc IS NULL AND m.SourceKind IN (N'git', N'file')");
        c.Parameters.Add("@j", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(send);
        c.ExecuteNonQuery();
    }

    private static List<Row> Load(BrainDb db, string where, string a, string? b)
    {
        using var c = db.Command(
            "SELECT m.Id, m.Lane, m.Name, m.Kind, m.SourceKind, m.SourceRepo, m.SourcePath, m.SourceBlob, m.SourceRef, m.SourceHash " +
            $"FROM brain.Memory m WHERE {where} ORDER BY m.Id");
        c.Parameters.Add("@a", SqlDbType.NVarChar, 400).Value = a;
        c.Parameters.Add("@b", SqlDbType.NVarChar, 400).Value = (object?)b ?? DBNull.Value;
        using var r = c.ExecuteReader();
        var rows = new List<Row>();
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        while (r.Read())
            rows.Add(new Row(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), S(5), S(6),
                             S(7)?.Trim(), S(8), S(9)));
        return rows;
    }

    /// <summary>
    /// The real reader: one <c>git rev-parse HEAD</c> and <c>git ls-tree -r -z --full-tree HEAD -- &lt;paths&gt;</c>
    /// (literal pathspecs, in chunks of 100), 3 s per call.
    /// </summary>
    public static HeadTree? GitHead(string repoDir, IReadOnlyCollection<string> paths)
    {
        var head = Git.Run(repoDir, TimeSpan.FromSeconds(3), "rev-parse", "HEAD")?.Trim();
        if (head is null) return null;
        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chunk in paths.Chunk(100))
        {
            var args = new List<string> { "--literal-pathspecs", "ls-tree", "-r", "-z", "--full-tree", head, "--" };
            args.AddRange(chunk);
            var raw = Git.Run(repoDir, TimeSpan.FromSeconds(3), args.ToArray());
            if (raw is null) return null;
            foreach (var entry in ParseLsTree(raw)) blobs[entry.Path] = entry.Blob;
        }
        return new HeadTree(head, blobs);
    }

    /// <summary>"&lt;mode&gt; blob &lt;sha&gt;\t&lt;path&gt;\0" per entry; non-blob entries (submodules) are skipped.</summary>
    public static IEnumerable<(string Path, string Blob)> ParseLsTree(string raw)
    {
        foreach (var entry in raw.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0) continue;
            var meta = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (meta.Length < 3 || meta[1] != "blob") continue;
            if (Lower(meta[2]) is { } blob) yield return (entry[(tab + 1)..], blob);
        }
    }

    private static string? Lower(string? s) => s?.Trim().ToLowerInvariant();

    /// <summary>Seconds with at most one decimal, always with a '.' (review rows 154, 157: never the machine's culture).</summary>
    public static string Secs(TimeSpan t) => t.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private static string Short(string? sha) => sha is null ? "-" : sha.Length > 7 ? sha[..7] : sha;
}
