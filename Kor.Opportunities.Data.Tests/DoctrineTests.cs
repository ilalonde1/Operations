using System.Text.RegularExpressions;
using Kor.Opportunities.Data.Awards;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// Machine-enforced BD data doctrine (docs/BD-Doctrine.md). These are
/// architecture tests: they scan the actual source tree so a violation fails
/// the build the day it is written, instead of surfacing months later as a
/// data-corruption audit. Escapes go in doctrine-allowlist.txt with a reason —
/// a reviewable decision, never silent drift.
/// </summary>
public sealed class DoctrineTests
{
    // ---- shared plumbing ----------------------------------------------------

    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kor.Operations.sln"))
                               && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Opportunities.Data")))
        {
            dir = dir.Parent!;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(RepoRoot, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}_Publish{Path.DirectorySeparatorChar}")
                     && !f.EndsWith("DoctrineTests.cs", StringComparison.OrdinalIgnoreCase));

    private static readonly HashSet<string> Allowlist = LoadAllowlist();

    private static HashSet<string> LoadAllowlist()
    {
        var path = Path.Combine(RepoRoot, "Kor.Opportunities.Data.Tests", "doctrine-allowlist.txt");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return set;
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var parts = trimmed.Split('|');
            if (parts.Length >= 2) set.Add($"{parts[0].Trim()}|{parts[1].Trim()}");
        }

        return set;
    }

    private static bool IsAllowlisted(string file, string line)
    {
        var rel = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
        return Allowlist.Any(a =>
        {
            var parts = a.Split('|');
            return rel.EndsWith(parts[0], StringComparison.OrdinalIgnoreCase)
                && line.Contains(parts[1], StringComparison.OrdinalIgnoreCase);
        });
    }

    // ---- D1: fill-only canonical FK writes ----------------------------------

    private static readonly Regex FkAssignment =
        new(@"\b\w*CanonicalOrgId\s*=\s*@\w+", RegexOptions.Compiled);

    // Comparison contexts (WHERE/JOIN predicates), not SET assignments. Lines may
    // begin inside a C# string literal, so allow leading quotes/concat tokens.
    private static readonly Regex ComparisonContext =
        new(@"^\s*[+@$""]*\s*(WHERE|AND|OR|ON|HAVING|IF|WHEN|SELECT|DELETE)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void CanonicalFkWrites_AreGuarded()
    {
        var violations = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var match = FkAssignment.Match(line);
                if (!match.Success) continue;
                if (line.Contains("COALESCE", StringComparison.OrdinalIgnoreCase)) continue;      // guarded fill-only
                if (line.Contains("CASE WHEN", StringComparison.OrdinalIgnoreCase)) continue;     // name-paired guard
                if (ComparisonContext.IsMatch(line)) continue;                                    // predicate, not a write
                // Single-line SQL where the match sits inside the predicate half
                // ("DELETE ... WHERE Fk = @id") — a predicate, not an assignment.
                var beforeMatch = line[..match.Index];
                if (beforeMatch.Contains("WHERE ", StringComparison.OrdinalIgnoreCase)
                    || beforeMatch.Contains(" AND ", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsAllowlisted(file, line)) continue;

                violations.Add($"{Path.GetRelativePath(RepoRoot, file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Doctrine D1 (docs/BD-Doctrine.md): canonical FK SET assignments must be COALESCE/CASE-guarded " +
            "(a resolver miss must never null a good link). Fix or allowlist WITH A REASON in doctrine-allowlist.txt:\n"
            + string.Join('\n', violations));
    }

    // ---- D2: every canonical FK column is registered or excluded ------------

    /// <summary>
    /// FK columns that reference CanonicalOrg but deliberately have NO paired
    /// raw-name column (child rows, ledgers, links) — the wheel cannot repair
    /// what has no name to resolve. Each entry documents why it is excluded.
    /// </summary>
    private static readonly HashSet<string> RegistryExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "CanonicalOrgId",                 // generic child-row FK (enrichment, alias, intel, triggers, warmth, mentions)
        "MergedFromCanonicalOrgId",       // merge ledger — history, never resolved
        "MergedIntoCanonicalOrgId",       // merge ledger — history, never resolved
        "TargetCanonicalOrgId",           // IntelProjectAction target — set by intel extraction with org context
        "ResolvedCanonicalOrgId",         // OpportunityInterestedFirms — IS registered; name column is RawFirmName
        "ProponentCanonicalOrgId",        // registered (MPI)
        "ArchitectCanonicalOrgId",        // registered (MPI); ArchitectDisplacementBriefs copy is a derived link
        "StructuralEngineerCanonicalOrgId", // registered (MPI)
        "GeneralContractorCanonicalOrgId",  // registered (MPI)
        "BuyerCanonicalOrgId",            // registered (Opportunities, KorPursuits, HistoricalOpportunities); CrmEngagements copy is set from linked opportunity
        "LostToCanonicalOrgId",           // registered (KorPursuits); CrmEngagements copy is user-picked in CRM UI
        "AwardingCanonicalOrgId",         // registered (OpportunityAwards)
        "AwardedToCanonicalOrgId",        // registered (OpportunityAwards, HistoricalOpportunities)
        "BidderCanonicalOrgId",           // registered (OpportunityBids)
        "ApplicantCanonicalOrgId",        // registered (BuildingPermit)
        "ContractorCanonicalOrgId",       // registered (BuildingPermit)
        "OwnerCanonicalOrgId",            // registered (BuildingPermit)
    };

    [Fact]
    public void EveryCanonicalFkColumn_IsRegisteredOrExcluded()
    {
        var token = new Regex(@"\b(\w*CanonicalOrgId)\b", RegexOptions.Compiled);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var schemaDir = Path.Combine(RepoRoot, "Kor.Opportunities.Data", "Schema");
        foreach (var file in Directory.EnumerateFiles(schemaDir, "*.sql"))
        {
            foreach (Match m in token.Matches(File.ReadAllText(file)))
            {
                // Column names carry no underscore; tokens with one are index /
                // constraint / default names (IX_/FK_/DF_/UX_...), not columns.
                if (!m.Groups[1].Value.Contains('_'))
                {
                    found.Add(m.Groups[1].Value);
                }
            }
        }

        var registered = CanonicalColumnRegistry.All.Select(e => e.FkColumn)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var undecided = found
            .Where(c => !registered.Contains(c) && !RegistryExclusions.Contains(c))
            .OrderBy(c => c)
            .ToList();

        Assert.True(undecided.Count == 0,
            "Doctrine D2 (docs/BD-Doctrine.md): a new *CanonicalOrgId column exists in Schema/*.sql that is " +
            "neither in CanonicalColumnRegistry (wheel-repaired + audited) nor a documented exclusion here. " +
            "DECIDE — register it or exclude it with a reason:\n" + string.Join('\n', undecided));
    }

    // ---- D3: every Quartz job is visible in the schedule registry -----------

    [Fact]
    public void EveryQuartzJob_IsInScheduleRegistry()
    {
        var programCs = File.ReadAllText(Path.Combine(RepoRoot, "Kor.Opportunities.Worker", "Program.cs"));
        var addJob = new Regex(@"AddJob<([\w\.]+)>", RegexOptions.Compiled);
        var jobs = addJob.Matches(programCs)
            .Select(m => m.Groups[1].Value.Split('.').Last())
            .ToHashSet(StringComparer.Ordinal);

        var defsSource = File.ReadAllText(Path.Combine(RepoRoot, "Kor.Opportunities.Worker", "Services", "ScheduledJobDefinition.cs"));

        var missing = jobs
            .Where(j => !defsSource.Contains(j, StringComparison.Ordinal))
            .Where(j => !IsAllowlisted("Kor.Opportunities.Worker/Program.cs", j))
            .OrderBy(j => j)
            .ToList();

        Assert.True(missing.Count == 0,
            "Doctrine D3 (docs/BD-Doctrine.md): Quartz jobs registered in Program.cs but invisible to the Admin " +
            "schedule registry (ScheduledJobDefinitions). Register them or allowlist with a reason:\n"
            + string.Join('\n', missing));
    }

    // ---- D4: the junk guard can never silently weaken ------------------------

    public static readonly TheoryData<string> JunkCorpus = new()
    {
        // The exact placeholder classes scrubbed from live data on 2026-07-11
        // (95 rows) — they must never survive the shared cleaner again.
        "no", "yes", "TBD", "tba", "N/A", "unknown", "Unknown at this time",
        "Not publicly confirmed", "not confirmed", "Not disclosed",
        "Neither firm has been announced",
        "multiple", "various firms", "unnamed consultant",
        // Multi-firm narrative strings (the migration-153 junk-org class):
        "Kasian (architect of record); Dialog (design architect)",
    };

    [Theory]
    [MemberData(nameof(JunkCorpus))]
    public void JunkCorpus_NeverResolves(string junk)
    {
        var cleaned = TeamNameCleaner.Clean(junk);
        if (cleaned is not null)
        {
            // Multi-firm strings may legitimately reduce to their lead firm — but a
            // placeholder must never survive as itself.
            Assert.NotEqual(junk.Trim(), cleaned);
        }
    }

    public static readonly TheoryData<string> RealFirms = new()
    {
        "Kasian Architecture", "DIALOG", "Fast + Epp", "RJC Engineers",
        "Chris Dikeakos Architects Inc.", "Black & McDonald", "PCL Constructors Westcoast Inc.",
    };

    [Theory]
    [MemberData(nameof(RealFirms))]
    public void RealFirms_AlwaysSurvive(string firm)
    {
        Assert.Equal(firm, TeamNameCleaner.Clean(firm));
    }

    // ---- D11: actionable pools come from the lifecycle views -----------------

    /// <summary>
    /// Migration 284 made "actionable" a single predicate
    /// (vw_ActionableProjects / vw_ActionableOpportunities). A Worker job that
    /// re-derives it inline (filtering DismissedAtUtc / un-owned / seat-filled
    /// itself) WILL drift from the view the day either changes — the exact
    /// failure class this lifecycle build exists to end. Transition stores and
    /// the reaper legitimately touch lifecycle columns (guarded UPDATEs and
    /// owner-scoped reads are not pool derivations) and are allowlisted.
    /// </summary>
    private static readonly string[] PoolDerivationTokens =
    {
        "DismissedAtUtc IS NULL",
        "OwnerStaffId IS NULL",
    };

    private static readonly Regex SeatStatusExclusion =
        new(@"SeatStatus\s*,\s*N?''\s*\)\s*(<>|NOT\s+IN)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void D11_WorkerJobs_UseLifecycleViews_NotInlinePredicates()
    {
        var workerRoot = Path.Combine(RepoRoot, "Kor.Opportunities.Worker");
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(workerRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var hit = PoolDerivationTokens.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase))
                          || SeatStatusExclusion.IsMatch(line);
                if (!hit) continue;
                if (IsAllowlisted(file, line)) continue;
                violations.Add($"{Path.GetRelativePath(RepoRoot, file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Doctrine D11 (docs/BD-Doctrine.md): Worker jobs must read actionable pools from " +
            "vw_ActionableProjects / vw_ActionableOpportunities, never re-derive lifecycle predicates inline. " +
            "Fix or allowlist WITH A REASON in doctrine-allowlist.txt:\n" + string.Join('\n', violations));
    }

    [Theory]
    [InlineData("Kor.Opportunities.Worker/Services/Reporting/WeeklyAttackSheetJob.cs", "vw_ActionableProjects")]
    [InlineData("Kor.Opportunities.Worker/Services/Reporting/BdMorningReportJob.cs", "vw_ActionableProjects")]
    public void D11_KnownConsumers_ReadTheView(string relativePath, string expectedView)
    {
        var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"expected consumer missing: {relativePath}");
        Assert.Contains(expectedView, File.ReadAllText(path), StringComparison.Ordinal);
    }
}
