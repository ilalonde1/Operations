using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1192's lint (3 Nov): kor migration-lint, on the REAL migrations in the sibling KOR.Drafter checkout and on
/// synthetic 178s. Covers: at the 177 cut-off 170-177 pass; with the cut-off removed (0) the rules are not blind - 170-174
/// fail L2 (each hand-wrote its probes), 175 and 176 fail L1 only (they hold no probe handling at all), 177 fails L2 (it
/// names the procedure only inside string literals: it builds it from @Body and calls it through @ProcName); a 178 that
/// hand-writes a TRY/CATCH probe fails L1 and L2; one that runs its probes through the procedure passes; probe words in
/// comments or string literals never match, and a comment naming the procedure never excuses code; SqlText keeps length and
/// line numbers and models nested comments, '' escapes and [ ] identifiers.
/// Does NOT cover: probe handling inside a dynamic-SQL string (blanked as data); a file that also calls the procedure once.
/// </summary>
public class MigrationLintTests
{
    private static string DrafterDb()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        var drafter = RepoLocator.Find("KOR.Drafter", d!.FullName);
        Assert.NotNull(drafter);
        return Path.Combine(drafter!, "db");
    }

    private static string Real(int n) => Directory.GetFiles(DrafterDb(), $"{n}_*.sql").Single();

    private static IReadOnlyList<string> LintFile(string path, int cutoff = MigrationLint.Cutoff) =>
        MigrationLint.Lint(path, File.ReadAllText(path), cutoff);

    [Theory]
    [InlineData(170)] [InlineData(171)] [InlineData(172)] [InlineData(173)] [InlineData(174)] [InlineData(175)] [InlineData(176)] [InlineData(177)]
    public void AtTheCutoffTheRealMigrationsPass(int n) => Assert.Empty(LintFile(Real(n)));

    [Theory]
    [InlineData(170)] [InlineData(171)] [InlineData(172)] [InlineData(173)] [InlineData(174)]
    public void WithoutTheCutoffTheHandWrittenProbesFailL2(int n)
    {
        var f = LintFile(Real(n), cutoff: 0);
        Assert.Contains(f, l => l.Contains(": L2 hand-written probe handling - BEGIN CATCH (line ", StringComparison.Ordinal));
        Assert.Contains(f, l => l.Contains(": L1 ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(175)] [InlineData(176)]
    public void WithoutTheCutoffTheFilesWithNoProbeHandlingFailL1Only(int n)
    {
        var f = LintFile(Real(n), cutoff: 0);
        Assert.Single(f);
        Assert.Contains(": L1 its header has no line starting 'PROBES:'", f[0]);
    }

    [Fact]
    public void WithoutTheCutoff177FailsL2BecauseItNamesTheProcedureOnlyInLiterals()
    {
        var f = LintFile(Real(177), cutoff: 0);
        Assert.Single(f);
        Assert.Contains(": L2 ", f[0]);
    }

    private const string HandWritten = """
        /*
            178 - a test
        */
        SET XACT_ABORT OFF;
        BEGIN TRAN;
        BEGIN TRY
            EXECUTE AS USER = N'korbrain_app';
            EXEC sp_executesql N'UPDATE brain.Memory SET Name = Name';
            REVERT;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() = -1 ROLLBACK;
        END CATCH;
        """;

    [Fact]
    public void A178ThatHandWritesATryCatchProbeFailsL1AndL2()
    {
        var f = MigrationLint.Lint("178_ATest.sql", HandWritten);
        Assert.Equal(2, f.Count);
        Assert.StartsWith("178_ATest.sql: L1 ", f[0]);
        Assert.Equal("178_ATest.sql: L2 hand-written probe handling - BEGIN CATCH (line 11) with EXECUTE AS / sp_executesql (line 7, 8); " +
                     "XACT_STATE() (line 12) - and the code never names migration.usp_MigrationProbe: run each probe through it", f[1]);
    }

    [Fact]
    public void A178ThatRunsItsProbesThroughTheProcedurePasses()
    {
        var sql = "/*\n    178 - a test\n    PROBES: through migration.usp_MigrationProbe\n*/\nBEGIN TRAN;\n" +
                  "DECLARE @v nvarchar(20);\nEXEC [migration].[usp_MigrationProbe] @Label = N'x', @Sql = N'SELECT 1', @Isolate = 0, @Verdict = @v OUTPUT;\n" +
                  "IF XACT_STATE() = -1 ROLLBACK;\n";
        Assert.Empty(MigrationLint.Lint("178_ATest.sql", sql));
    }

    [Fact]
    public void ProbeWordsInCommentsAndLiteralsNeverMatchAndACommentNeverExcuses()
    {
        var quiet = "-- PROBES: none\n/* BEGIN CATCH EXECUTE AS XACT_STATE() /* nested */ still a comment */\n" +
                    "PRINT N'BEGIN CATCH sp_executesql XACT_STATE() it''s data';\nSELECT [it's -- an identifier] = 1;\n";
        Assert.Empty(MigrationLint.Lint("178_ATest.sql", quiet));
        var excused = "-- PROBES: none\n-- run through migration.usp_MigrationProbe\nIF XACT_STATE() = -1 ROLLBACK;\n";
        Assert.Contains(MigrationLint.Lint("178_ATest.sql", excused), l => l.Contains(": L2 ", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnnumberedFileIsNotAMigrationAndAnUnclosedCommentIsAFinding()
    {
        Assert.Empty(MigrationLint.Lint("seed_data.sql", HandWritten, cutoff: 0));
        Assert.Contains(MigrationLint.Lint("178_x.sql", "-- PROBES: none\n/* never closed\nSELECT 1"), l => l.Contains(": L0 ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheNeutralCopyKeepsLengthAndLines()
    {
        var sql = "SELECT N'a''b\nc' AS x -- note\n/* a /* b */ c */ [d'e] FROM t;";
        var r = SqlText.Neutralise(sql);
        Assert.Null(r.Unproved);
        Assert.Equal(sql.Length, r.Neutral.Length);
        Assert.Equal(sql.Count(c => c == '\n'), r.Neutral.Count(c => c == '\n'));
        Assert.DoesNotContain("note", r.Neutral);
        Assert.DoesNotContain("a''b", r.Neutral);
        Assert.Contains("[d'e] FROM t;", r.Neutral);
        Assert.DoesNotContain(" c */", r.Neutral);
    }

    [Fact]
    public void TheCliPrintsOneLinePerFindingAndExitsOneOrZero()
    {
        var output = new StringWriter();
        Assert.Equal(1, Program.Run(new[] { "migration-lint", "-", "--name", "178_ATest.sql" }, () => HandWritten, output, new StringWriter(),
                                    Path.Combine(Path.GetTempPath(), $"kor-lint-{Guid.NewGuid():N}.log")));
        Assert.Equal(2, output.ToString().Trim().Split('\n').Length);
        var ok = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "migration-lint", Real(177) }, () => null, ok, new StringWriter(),
                                    Path.Combine(Path.GetTempPath(), $"kor-lint-{Guid.NewGuid():N}.log")));
        Assert.Equal("", ok.ToString());
        var err = new StringWriter();
        Assert.Equal(1, Program.Run(new[] { "migration-lint", "-" }, () => HandWritten, new StringWriter(), err,
                                    Path.Combine(Path.GetTempPath(), $"kor-lint-{Guid.NewGuid():N}.log")));
        Assert.Contains("needs --name", err.ToString());
    }
}
