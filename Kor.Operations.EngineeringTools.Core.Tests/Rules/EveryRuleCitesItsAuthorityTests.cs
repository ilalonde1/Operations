#nullable enable
using Kor.Operations.EngineeringTools.Core.Tests.Intake;
using Kor.Operations.EngineeringTools.Dxf;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Rules;

/// <summary>
/// EVERY RULE THAT LEANS ON THE PROFESSION'S KNOWLEDGE NAMES ITS AUTHORITY AS A ROW (WP7, 2026-09-16). A reader constant
/// the triage classes <c>Rule</c> and explains by a code or a standard (NBC, BCBC, CSA - the tread sizes today) has a
/// <c>knowledge.RuleClause</c> row (migration 096) whose clause exists and is live; and a clause written from memory
/// (<c>from-memory-unverified</c>) is read from the source within thirty days, or this fails until it is - a remembered
/// number is a guess with a citation, and the ingestor exists to replace it (<c>takeoff knowledge-ingest</c>).
/// WHAT THIS COVERS: the link's existence for every code-naming Rule; the age of unverified clauses. WHAT IT DOES
/// NOT: whether the rule's value follows from the clause's (drafting slack sits between them, recorded in HowUsed);
/// rules that lean on a code without saying so in their triage line (name the code there and this test sees it).
/// It needs KorStandards; an unset connection FAILS, as CompiledDefaultsAreTheBankedRowsTests does.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class EveryRuleCitesItsAuthorityTests
{
    private static string Connection()
    {
        string? conn = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(conn), $"{RuleSettings.ConnectionEnvironmentVariable} is not set. This gate reads KorStandards and never skips.");
        return conn!;
    }

    [Fact]
    public void EveryCodeNamingRuleHasALiveClauseLink()
    {
        var rules = EveryReaderConstantIsTriagedTests.RulesNamingACode();
        Assert.NotEmpty(rules);   // the tread sizes, at least
        var linked = new HashSet<string>(StringComparer.Ordinal);
        using (var c = new SqlConnection(Connection()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT RuleName FROM knowledge.vw_RuleAuthority";
            using var r = cmd.ExecuteReader();
            while (r.Read()) linked.Add(r.GetString(0));
        }
        var missing = rules.Where(x => !linked.Contains(x.Name)).Select(x => x.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "rules that name a code in their triage line with no knowledge.RuleClause row: " + string.Join(", ", missing));
    }

    [Fact]
    public void AClauseWrittenFromMemoryIsReadFromTheSourceWithinThirtyDays()
    {
        var stale = new List<string>();
        using (var c = new SqlConnection(Connection()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT s.Code, cl.ClauseRef, cl.Topic, cl.CreatedAtUtc FROM knowledge.Clause cl JOIN knowledge.Source s ON s.Id = cl.SourceId
                                 WHERE cl.RetiredAtUtc IS NULL AND cl.Confidence = 'from-memory-unverified' AND cl.CreatedAtUtc < DATEADD(day, -30, SYSDATETIMEOFFSET())";
            using var r = cmd.ExecuteReader();
            while (r.Read()) stale.Add($"{r.GetString(0)} {r.GetString(1)} {r.GetString(2)} (written {r.GetDateTimeOffset(3):yyyy-MM-dd})");
        }
        Assert.True(stale.Count == 0, "clauses written from memory more than thirty days ago and never read from the source - run takeoff knowledge-ingest on KOR's copy: " + string.Join("; ", stale));
    }
}
