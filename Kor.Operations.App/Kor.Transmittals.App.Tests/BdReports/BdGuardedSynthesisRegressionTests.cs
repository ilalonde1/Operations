#nullable enable
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using Kor.Opportunities.Data.BdReports;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

/// <summary>
/// Brief 5 / BD-A02–04: EXPECTED conditional SQL refusal, rejected-write accounting,
/// deterministic evidence selection and a cutoff that leaves omitted signals new.
/// ACTUAL is the production SQL, batch result and evidence helper output.
/// Does not execute SQL, HTTP, the model or live identity resolution. A SQL trigger,
/// a wrongly assigned action FK or a signal backdated before the prior cutoff escapes.
/// Authored only: builds/tests/DB access prohibited; no executed red/green claim.
/// </summary>
public sealed class BdGuardedSynthesisRegressionTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void Persistence_RefusesChangedSnapshotAndNewerRow_InTheWritePredicate()
    {
        var sql = Sql("PersistSynthesisSql");
        var where = sql[sql.IndexOf("WHERE e.MajorProjectsInventoryId = @id", StringComparison.Ordinal)..];
        foreach (var required in new[]
        {
            "e.Id = @expectedEnrichmentId", "e.LastRefreshAtUtc = @expectedRefreshAtUtc",
            "CONVERT(varbinary(max), e.ResultJson) = CONVERT(varbinary(max), @expectedJson)",
            "NOT EXISTS", "newer.Id > e.Id", "WITH (UPDLOCK, HOLDLOCK)"
        })
            Assert.True(where.Contains(required, StringComparison.Ordinal),
                $"EXPECTED stale snapshot refused by {required}; ACTUAL write predicate:\n{where}");
        Assert.Contains("e.LastRefreshAtUtc = @evidenceCutoffUtc", sql);
        Assert.DoesNotContain("e.LastRefreshAtUtc = sysdatetimeoffset()", sql);

        var source = Source("OnDemandHoningSynthesizer.cs");
        Assert.Contains("new SqlCommand(PersistSynthesisSql, con)", source);
        Assert.Contains("expected.EnrichmentId", source);
        Assert.Contains("expected.HonedAtUtc", source);
        Assert.Contains("expected.ExistingHoningJson", source);
        Assert.Contains("expected.EvidenceCutoffUtc", source);
        Assert.Contains("PersistSynthesisAsync(expected[id], verdict, status, token)", source);
        Assert.Contains("ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0", source);
    }

    [Fact]
    public void CutoffWrite_RefusesUnreadEvidenceThatArrivedInsideTheConsumedInterval()
    {
        var sql = Sql("PersistSynthesisSql").Replace("\r\n", "\n");
        Assert.Contains("AND NOT EXISTS (\n      SELECT 1 FROM opportunities.IntelProjectAction", sql);
        var guard = sql[sql.IndexOf("SELECT 1 FROM opportunities.IntelProjectAction a WITH", StringComparison.Ordinal)..];
        foreach (var required in new[]
        {
            "WITH (UPDLOCK, HOLDLOCK)", "a.MajorProjectsInventoryId = @id",
            "a.UpdatedAtUtc > @expectedRefreshAtUtc", "a.UpdatedAtUtc <= @evidenceCutoffUtc",
            "AND NOT EXISTS", "OPENJSON(@evidenceJson)", "seen.Id = a.Id",
            "seen.UpdatedAtUtc = a.UpdatedAtUtc",
            "CONVERT(varbinary(max), seen.Recommendation) = CONVERT(varbinary(max), a.Recommendation)"
        })
            Assert.True(guard.Contains(required, StringComparison.Ordinal),
                $"EXPECTED unread/changed evidence to refuse cutoff advancement via {required}; ACTUAL:\n{guard}");
        Assert.Contains("expected.EvidenceJson", Source("OnDemandHoningSynthesizer.cs"));
    }

    [Fact]
    public async Task RefusedWrite_IsRejectedAndNeverCountedAsSynthesized()
    {
        var results = JsonNode.Parse("""[{"id":1,"verdict":"PURSUE"},{"id":2,"verdict":"DEAD"}]""")!.AsArray();
        var actual = await OnDemandHoningSynthesizer.ApplySynthesisResultsAsync(results,
            new long[] { 1, 2 }, (_, _, _, _) => Task.FromResult(false), CancellationToken.None);
        Assert.True(actual.Updated == 0 && actual.RejectedStale == 2,
            $"EXPECTED 0 synthesized, 2 rejected as stale; ACTUAL {actual}");
        var note = SqlBdReportService.SynthesisRefreshNote(new(2, 0, 0, 2, actual.Updated, 0,
            RejectedStale: actual.RejectedStale));
        Assert.Contains("2 rejected as stale", note);
        Assert.Contains("0 synthesized", note);
    }

    [Fact]
    public void ThirteenthSignalAndArrivalDuringCall_RemainAfterTheCutoff()
    {
        var signals = Enumerable.Range(1, 13).Select(i => Signal(i, Baseline.AddMinutes(i))).Reverse().ToArray();
        var actual = OnDemandHoningSynthesizer.SelectEvidence(Baseline, signals);
        Assert.True(actual.CutoffUtc == Baseline.AddMinutes(12),
            $"EXPECTED cutoff at included signal 12, not completion time; ACTUAL {actual.CutoffUtc:O}");
        Assert.DoesNotContain("action-13", actual.DeltaText);
        Assert.True(signals.Single(x => x.Id == 13).UpdatedAtUtc > actual.CutoffUtc);
        var arrivedDuringCall = Signal(14, Baseline.AddMinutes(14));
        Assert.True(arrivedDuringCall.UpdatedAtUtc > actual.CutoffUtc,
            "EXPECTED an action arriving during the call to stay eligible; ACTUAL consumed by cutoff");
        Assert.Equal(actual, OnDemandHoningSynthesizer.SelectEvidence(Baseline, signals.Reverse().ToArray()));
    }

    [Fact]
    public void SplitTimestampGroup_DoesNotAdvancePastUnseenTiedEvidence()
    {
        var sameTime = Baseline.AddMinutes(1);
        var actual = OnDemandHoningSynthesizer.SelectEvidence(Baseline,
            Enumerable.Range(1, 13).Select(i => Signal(i, sameTime)).ToArray());
        Assert.True(actual.CutoffUtc == Baseline,
            $"EXPECTED unchanged cutoff for an incomplete timestamp group; ACTUAL {actual.CutoffUtc:O}");
        Assert.Equal(Baseline, OnDemandHoningSynthesizer.SelectEvidence(Baseline,
            Array.Empty<OnDemandHoningSynthesizer.HoningSignal>()).CutoffUtc);
    }

    [Fact]
    public void CompletePrefixBeforeSplitTimestamp_CanAdvanceWithoutConsumingTheTie()
    {
        var signals = new[] { Signal(1, Baseline.AddMinutes(1)) }
            .Concat(Enumerable.Range(2, 12).Select(i => Signal(i, Baseline.AddMinutes(2)))).ToArray();
        var actual = OnDemandHoningSynthesizer.SelectEvidence(Baseline, signals);
        Assert.Equal(Baseline.AddMinutes(1), actual.CutoffUtc);
        Assert.Contains("action-12", actual.DeltaText);
        Assert.DoesNotContain("action-13", actual.DeltaText);
    }

    [Fact]
    public void Evidence_UsesOnlyResolvedActions_AndRetainsTheFullRecommendation()
    {
        var sql = Sql("ClassifySql");
        Assert.True(sql.Contains("a.MajorProjectsInventoryId = h.Id", StringComparison.Ordinal),
            $"EXPECTED resolved project FK; ACTUAL SQL:\n{sql}");
        Assert.DoesNotContain("OpportunityObservations", sql);
        Assert.DoesNotContain("LIKE", sql);
        Assert.Contains("TOP (13)", sql);
        Assert.Contains("ORDER BY a.UpdatedAtUtc, a.Id", sql);
        Assert.Contains("a.UpdatedAtUtc <= @readAtUtc", sql);
        var recommendation = new string('a', 200) + " Structural scope already awarded.";
        var actual = OnDemandHoningSynthesizer.SelectEvidence(Baseline,
            new[] { new OnDemandHoningSynthesizer.HoningSignal(1, Baseline.AddMinutes(1), recommendation) });
        Assert.Contains(recommendation, actual.DeltaText);
    }

    private static OnDemandHoningSynthesizer.HoningSignal Signal(long id, DateTimeOffset timestamp)
        => new(id, timestamp, $"action-{id}");

    private static string Sql(string name)
    {
        var field = typeof(OnDemandHoningSynthesizer).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field is not null, $"EXPECTED inspectable production SQL {name}; ACTUAL missing");
        return (string)field!.GetRawConstantValue()!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(
        XamlStaticResourceOrderTests.GetRepoRoot(), "Kor.Opportunities.Data", "BdReports", file));
}
