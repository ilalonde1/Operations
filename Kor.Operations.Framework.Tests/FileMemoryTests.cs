using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Kor.Operations.Framework.Start;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// 3 Nov (found by the lead 08:5x): Framework's file memory, imported after 168 with SourceKind 'session', was on NO
/// start section - "imported file memory" was read off a field beside it (<c>SourceKind is null</c>). The record now says
/// what it is: SourceKind 'file' citing the memory file (kor_brain_import.py), and <see cref="StartRecord.FromFileMemory"/>
/// is read off that citation. PURE: which rows are file memory; the MEMORY part names every imported row exactly once -
/// the index whole, the rows it does not name after it, a stale row named as stale.
/// Does NOT cover: the SQL (FileMemoryLiveTests), the Python importer (Framework tools/kor_brain_import_test.py).
/// Same-class miss: a memory folder outside <c>.claude\projects\*\memory</c> (another tool's layout) reads as a plain
/// file source - on the record part, not the MEMORY part.
/// </summary>
public class FileMemoryTests
{
    private static StartRecord Row(int id, string name, string? kind, string? sref, string status = "current", string? body = null) =>
        new(id, "zz", name, "feedback", $"desc of {id}", body, status, kind, null, null, null, sref, null, null, null);

    [Theory]
    [InlineData(null, null, true)]                                                                       // imported before 168
    [InlineData("file", @"C:\Users\x\.claude\projects\C--VIsual-Studio-Projects-Framework\memory\feedback_a.md", true)]
    [InlineData("file", "C:/Users/x/.Claude/Projects/p/Memory/MEMORY.md", true)]
    [InlineData("file", @"C:\Users\x\.claude\projects\p\memory\sub\a.md", false)]                         // not directly in the folder
    [InlineData("file", @"C:\Users\x\.claude\projects\p\memory\a.txt", false)]
    [InlineData("file", @"C:\repo\docs\memory\a.md", false)]
    [InlineData("session", @"C:\Users\x\.claude\projects\p\memory\a.md", false)]                          // the 3 Nov shape: a session, not a file
    [InlineData("git", null, false)]
    public void FileMemoryIsReadOffWhatTheRecordCites(string? kind, string? sref, bool expected) =>
        Assert.Equal(expected, Row(1, "a", kind, sref).FromFileMemory);

    private const string Dir = @"C:\Users\x\.claude\projects\zz\memory\";

    private static StartSide Side(params StartRecord[] rows) =>
        new("zz", DateTimeOffset.Now, rows, null) { Office = Array.Empty<StartRecord>() };

    private static int Count(string text, string name) =>
        Regex.Matches(text, @"(?<![\w.-])" + Regex.Escape(name) + @"(?![\w-])").Count;

    [Fact]
    public void TheMemoryPartNamesEveryImportedRowOnceTheIndexFirst()
    {
        var index = "# Memory Index\n- [one](feedback_one.md) - the first\n- [two](project_two.md) - the second\n";
        var side = Side(Row(1, ModuleState.IndexName, "file", Dir + "MEMORY.md", body: index),
                        Row(2, "feedback_one", "file", Dir + "feedback_one.md"),
                        Row(3, "project_two", "file", Dir + "project_two.md"),
                        Row(4, "reference_not_indexed", "file", Dir + "reference_not_indexed.md"),
                        Row(5, "a_session_note", "session", "session x"));
        var pages = string.Join("\n", Enumerable.Range(1, 4).Select(p => StartRender.Memory(side, p, 4, StartRender.HookBudget)));
        Assert.Contains("LEGACY MEMORY (3 rows imported from the file memories", pages);
        Assert.Contains(index.Trim(), pages);
        Assert.Contains("Imported, not named in the index (1): reference_not_indexed", pages);
        foreach (var n in new[] { "feedback_one", "project_two", "reference_not_indexed" }) Assert.Equal(1, Count(pages, n));
        Assert.Equal(0, Count(pages, "a_session_note"));                                     // a session note is a sourced record
        Assert.DoesNotContain("STALE", pages);
    }

    [Fact]
    public void AStaleImportedRowIsNamedStaleOnTheMemoryPart()
    {
        var side = Side(Row(1, ModuleState.IndexName, "file", Dir + "MEMORY.md", body: "- [one](feedback_one.md)"),
                        Row(2, "feedback_one", "file", Dir + "feedback_one.md", status: "stale"));
        var page = StartRender.Memory(side, 1, 4, StartRender.HookBudget);
        Assert.Contains("STALE - the memory file changed since it was imported (read the file, or re-import the lane) (1): feedback_one", page);
    }

    [Fact]
    public void WithoutAnIndexAStaleRowIsMarkedOnItsOwnLineAndNamedOnce()
    {
        // review row 268: the STALE list came before the ':' and the row list named the stale one a second time
        var side = Side(Row(2, "feedback_one", "file", Dir + "feedback_one.md", status: "stale"),
                        Row(3, "project_two", "file", Dir + "project_two.md"));
        var page = StartRender.Memory(side, 1, 4, StartRender.HookBudget);
        Assert.Contains("one imported before 168 has no source):\n- record #2 [feedback] feedback_one - desc of 2 [STALE - the memory file changed since it was imported]\n" +
                        "- record #3 [feedback] project_two - desc of 3", page);
        Assert.Equal(1, Count(page, "feedback_one"));
    }

    [Fact]
    public void UnindexedNamesPastTheCapAreCountedNotListed()
    {
        var rows = new List<StartRecord> { Row(1, ModuleState.IndexName, "file", Dir + "MEMORY.md", body: "# Memory Index") };
        rows.AddRange(Enumerable.Range(0, 200).Select(i => Row(10 + i, $"feedback_unindexed_number_{i:D3}", "file", Dir + $"feedback_unindexed_number_{i:D3}.md")));
        var page = StartRender.Memory(Side(rows.ToArray()), 1, 4, null);
        Assert.Matches(@"Imported, not named in the index: 200 rows \([\d,]+ chars of names\) - python -I "".*kor_brain\.py"" list --lane ""zz""", page);
        Assert.DoesNotContain("feedback_unindexed_number_000", page);
    }

    [Theory]
    [InlineData("- [x](feedback_one.md)", "feedback_one", true)]
    [InlineData("- [x](./feedback_one.md) - d", "feedback_one", true)]
    [InlineData("- [x](my_feedback_one.md)", "feedback_one", false)]
    [InlineData("- [x](feedback_one_more.md)", "feedback_one", false)]
    [InlineData("- feedback_one is mentioned without its file", "feedback_one", true)]       // a bare stem names it too
    [InlineData("- feedback_one-two is another stem", "feedback_one", false)]
    [InlineData("- see the_feedback_one", "feedback_one", false)]
    public void TheIndexNamesARowByItsFileName(string index, string name, bool named) =>
        Assert.Equal(named, StartRender.NamedIn(index, name));
}

/// <summary>
/// LIVE (rolled back): a synthetic memory folder (a <c>.claude\projects\&lt;p&gt;\memory</c> under %TEMP%) is written into a
/// zz-test lane the way kor_brain_import.py writes it - SourceKind 'file', SourceRef the file's absolute path,
/// SourceHash its SHA-256 - and the CLI's MEMORY part (the hook's pages 1-4) names every imported row exactly once; while
/// all are current the record part names none of them (a STALE one is named in its stale section, by design). Then one
/// file changes: K5 freshness reads it stale (SHA-256) and WRITES it, and the MEMORY part then names it stale. The lane's brain.Memory and brain.Event counts, read on their own connection, are equal
/// before and after (0 and 0: the run is rolled back).
/// Does NOT cover: the Python writer itself (Framework kor_brain_import_test.py runs it and reads these pages through kor.exe).
/// Same-class misses: a lane whose rows were written 'session' by the importer between 168 and this fix reads as sourced
/// records until the lane is re-imported; the MEMORY part runs no freshness check (it reads the Status the record part
/// last WROTE), so if Claude Code runs the start hooks concurrently, the first start after a file changes can show the
/// row unmarked on the MEMORY part while the record part's stale section names it (review row 268; the test writes the
/// verdict first and cannot see that lag).
/// </summary>
[Collection(LiveDb.Name)]
public class FileMemoryLiveTests
{
    private static readonly string Lane = $"zz-test-filemem-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";

    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("the test is not running inside the Operations checkout");
    }

    private static (long Memory, long Events) Counts() => BrainDb.Read(KorStandardsConnection.Login.Brain, d =>
    {
        // the lane's own rows: other sessions write brain.Memory and brain.Event while this runs
        using var c = d.Command("SELECT (SELECT COUNT_BIG(*) FROM brain.Memory WHERE Lane = @m), (SELECT COUNT_BIG(*) FROM brain.Event WHERE Module = @m);", 30);
        c.Parameters.Add("@m", System.Data.SqlDbType.NVarChar, 60).Value = Lane;
        using var r = c.ExecuteReader();
        r.Read();
        return (r.GetInt64(0), r.GetInt64(1));
    });

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string Context(string json)
    {
        if (json.Length == 0) return "";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString()!;
    }

    private static string Part(BrainDb db, params string[] args)
    {
        var output = new StringWriter();
        var stdin = JsonSerializer.Serialize(new { session_id = "filemem", source = "startup", cwd = RepoDir() });
        Assert.Equal(0, Program.Run(new[] { "start", "--module", Lane }.Concat(args)
                                        .Concat(new[] { "--hook", "--cache-dir", Path.Combine(Path.GetTempPath(), $"kor-filemem-cache-{Guid.NewGuid():N}") }).ToArray(),
                                    () => stdin, output, new StringWriter(), Path.Combine(Path.GetTempPath(), $"kor-filemem-{Guid.NewGuid():N}.log"), db));
        return Context(output.ToString());
    }

    private static int Count(string text, string name) =>
        Regex.Matches(text, @"(?<![\w.-])" + Regex.Escape(name) + @"(?![\w-])").Count;

    [Fact]
    public void LiveEveryImportedMemoryIsOnTheMemoryPartOnceAndGoesStaleWhenItsFileChanges()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"kor-filemem-{Guid.NewGuid():N}", ".claude", "projects", "zz-test", "memory");
        Directory.CreateDirectory(folder);
        var names = new[] { "feedback_alpha_filemem", "project_beta_filemem", "reference_gamma_filemem", "user_delta_unindexed" };
        foreach (var n in names) File.WriteAllText(Path.Combine(folder, n + ".md"), $"---\nname: {n}\ntype: feedback\n---\nbody of {n}\n");
        File.WriteAllText(Path.Combine(folder, "MEMORY.md"),
                          "# Memory Index\n" + string.Join("\n", names.Take(3).Select(n => $"- [{n[..5]}]({n}.md) - an entry")) + "\n");
        var before = Counts();
        try
        {
            using (var db = BrainDb.OpenRolledBack())
            {
                foreach (var f in Directory.GetFiles(folder, "*.md"))
                {
                    var stem = Path.GetFileNameWithoutExtension(f);
                    RecordStore.Insert(db, new NewRecord(Lane, stem == "MEMORY" ? ModuleState.IndexName : stem, stem == "MEMORY" ? "reference" : "feedback",
                                                         $"imported {stem}", File.ReadAllText(f), "filemem-test", SourceKind: "file",
                                                         SourceRef: f, SourceHash: Sha(f), Source: $"file memory {Path.GetFileName(f)}"));
                }
                string Pages() => string.Join("\n", Enumerable.Range(1, 4).Select(p => Part(db, "--part", "memory", "--page", $"{p}", "--pages", "4")));
                var pages = Pages();
                Assert.DoesNotContain("LEGACY MEMORY: none", pages);
                Assert.Contains("LEGACY MEMORY (4 rows imported from the file memories", pages);
                foreach (var n in names) Assert.Equal(1, Count(pages, n));                 // every imported name, none twice
                Assert.Contains("Imported, not named in the index (1): user_delta_unindexed", pages);

                // the record part: none of them is a sourced record
                var record = Part(db, "--part", "record");
                foreach (var n in names) Assert.Equal(0, Count(record, n));

                // one file changes: K5 reads it stale by its SHA-256, and the MEMORY part names it stale
                File.AppendAllText(Path.Combine(folder, names[1] + ".md"), "changed after import\n");
                var fresh = Freshness.CheckModule(db, Lane);
                var beta = Assert.Single(fresh.Rows, r => r.Name == names[1]);
                Assert.Equal(Verdict.Stale, beta.Verdict);
                Assert.Equal(4, fresh.Rows.Count(r => r.Verdict == Verdict.Current));       // the other 3 and the index
                Assert.Contains("(1): project_beta_filemem", Pages().Split('\n').Single(l => l.StartsWith("STALE - the memory file changed", StringComparison.Ordinal)));
            }
            Assert.Equal(before, Counts());
        }
        finally
        {
            Directory.Delete(Path.GetFullPath(Path.Combine(folder, "..", "..", "..", "..")), recursive: true);
        }
    }
}
