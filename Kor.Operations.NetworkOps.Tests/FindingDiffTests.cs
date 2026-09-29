#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// What gets emailed. The promise: told once when a finding appears or gets worse, once when it
// clears, never again just because it is still there, and never for Info on its own.
//
// WHAT IT COVERS: new / escalated / unchanged / cleared classification, the notify rule, the
// digest's "no news is no email", ordering and HTML-escaping of evidence. WHAT IT DOES NOT: the
// store's upsert (SQL, live) or Graph delivery. A SAME-CLASS FAULT IT WOULD NOT CATCH: a finding
// that flaps (fires, clears, fires) produces an email each way -- by design today, and noisy if
// a rule sits on its threshold.
public sealed class FindingDiffTests
{
    private static Finding F(string key, Severity s) => new(key, s, key + " title", key + " evidence");
    private static OpenFinding O(string key, Severity s, Severity? notified = null) => new(1, key, s, DateTime.UtcNow, notified);

    [Fact]
    public void A_finding_is_new_once_then_unchanged()
    {
        var first = FindingDiff.Compute([], [F("gpu-hangs", Severity.Warning)]);
        Assert.Equal(ChangeKind.New, first.Single().Kind);
        Assert.True(FindingDiff.IsNotifiable(first.Single()));

        var second = FindingDiff.Compute([O("gpu-hangs", Severity.Warning, Severity.Warning)], [F("gpu-hangs", Severity.Warning)]);
        Assert.Equal(ChangeKind.Unchanged, second.Single().Kind);
        Assert.False(FindingDiff.IsNotifiable(second.Single()));
    }

    [Fact]
    public void Getting_worse_is_news_getting_better_is_not()
    {
        var worse = FindingDiff.Compute([O("gpu-hangs", Severity.Warning, Severity.Warning)], [F("gpu-hangs", Severity.Critical)]).Single();
        var better = FindingDiff.Compute([O("gpu-hangs", Severity.Critical, Severity.Critical)], [F("gpu-hangs", Severity.Warning)]).Single();
        Assert.Equal(ChangeKind.Escalated, worse.Kind);
        Assert.True(FindingDiff.IsNotifiable(worse));
        Assert.Equal(ChangeKind.Unchanged, better.Kind);
    }

    [Fact]
    public void Clearing_is_announced_only_if_the_raising_was()
    {
        var mailed = FindingDiff.Compute([O("disk-missing", Severity.Critical, Severity.Critical)], []).Single();
        var neverMailed = FindingDiff.Compute([O("fan-profile-loud", Severity.Info, null)], []).Single();
        Assert.Equal(ChangeKind.Cleared, mailed.Kind);
        Assert.True(FindingDiff.IsNotifiable(mailed));
        Assert.False(FindingDiff.IsNotifiable(neverMailed));
    }

    [Fact]
    public void Info_findings_are_recorded_but_never_mailed_on_their_own()
        => Assert.False(FindingDiff.IsNotifiable(FindingDiff.Compute([], [F("fan-profile-loud", Severity.Info)]).Single()));

    [Fact]
    public void No_news_is_no_email()
    {
        var unchanged = FindingDiff.Compute([O("gpu-hangs", Severity.Warning, Severity.Warning)], [F("gpu-hangs", Severity.Warning)]);
        Assert.Null(AlertDigest.Compose([new DeviceChanges("KOR-305", unchanged.Where(FindingDiff.IsNotifiable).ToList())], DateTime.Now));
    }

    [Fact]
    public void The_digest_leads_with_critical_names_the_machine_and_escapes_evidence()
    {
        var changes = new List<DeviceChanges>
        {
            new("KOR-305", [new FindingChange(ChangeKind.New, "gpu-hangs", F("gpu-hangs", Severity.Warning), null)]),
            new("KOR-206-N", [new FindingChange(ChangeKind.New, "disk-missing",
                new Finding("disk-missing", Severity.Critical, "A drive has disappeared", "Samsung <SSD> 870 & co"), null)]),
            new("KOR-216", [new FindingChange(ChangeKind.Cleared, "fan-profile-loud", null, O("fan-profile-loud", Severity.Info, Severity.Info))]),
        };
        var d = AlertDigest.Compose(changes, new DateTime(2026, 9, 29, 10, 30, 0))!;
        Assert.StartsWith("[NetworkOps] 1 critical, 2 new or worse, 1 cleared", d.Subject);
        Assert.True(d.HtmlBody.IndexOf("Critical", StringComparison.Ordinal) < d.HtmlBody.IndexOf("Warning", StringComparison.Ordinal));
        Assert.Contains("KOR-206-N", d.HtmlBody);
        Assert.Contains("Samsung &lt;SSD&gt; 870 &amp; co", d.HtmlBody);
        Assert.DoesNotContain("<SSD>", d.HtmlBody);
    }
}
