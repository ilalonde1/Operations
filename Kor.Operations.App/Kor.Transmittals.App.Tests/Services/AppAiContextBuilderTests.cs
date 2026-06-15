#nullable enable
using System.Reflection;
using System.Runtime.CompilerServices;
using AppFin = Kor.Operations.Financials;
using AppSvc = Kor.Operations.Services;
using Xunit;

namespace Kor.Operations.Tests.Services;

/// <summary>
/// Locks the contract of <see cref="AppSvc.AppAiContextBuilder.BuildFullContext"/>
/// — the seam that decides whether IAiContextProvider snapshots actually reach
/// Claude on every AI bar query. Phase 11e left this builder registered but
/// unused; Batch 60 wired it back into AppAiService.AskAsync. If these tests
/// regress, the AI bar quietly stops seeing KOR's KPI methodology again and
/// Claude falls back to inventing formulas (see 2026-05-10 Net Multiplier
/// incident in project memory).
/// </summary>
public sealed class AppAiContextBuilderTests
{
    [Fact]
    public void BuildFullContext_ConcatenatesAllRegisteredProviders_WithHeaderSeparators()
    {
        var builder = new AppSvc.AppAiContextBuilder();
        builder.Register(new FakeProvider("Alpha", "alpha body"));
        builder.Register(new FakeProvider("Beta",  "beta body"));

        var ctx = builder.BuildFullContext();

        Assert.Contains("=== ALPHA ===", ctx, System.StringComparison.Ordinal);
        Assert.Contains("alpha body",   ctx, System.StringComparison.Ordinal);
        Assert.Contains("=== BETA ===", ctx, System.StringComparison.Ordinal);
        Assert.Contains("beta body",    ctx, System.StringComparison.Ordinal);
        // Order matters: Alpha registered first → comes first.
        Assert.True(
            ctx.IndexOf("=== ALPHA ===", System.StringComparison.Ordinal) <
            ctx.IndexOf("=== BETA ===",  System.StringComparison.Ordinal));
    }

    [Fact]
    public void BuildFullContext_SkipsProvidersWithoutData()
    {
        var builder = new AppSvc.AppAiContextBuilder();
        builder.Register(new FakeProvider("Loaded",   "real",  hasData: true));
        builder.Register(new FakeProvider("Unloaded", "should not appear", hasData: false));

        var ctx = builder.BuildFullContext();

        Assert.Contains("=== LOADED ===", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("UNLOADED", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("should not appear", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFullContext_SkipsThrowingProvider_DoesNotKillTheRest()
    {
        // The whole reason for the try/catch around each BuildContext: a future
        // VM with a buggy snapshot path cannot blank out the AI bar firm-wide.
        var builder = new AppSvc.AppAiContextBuilder();
        builder.Register(new ThrowingProvider("Broken"));
        builder.Register(new FakeProvider("Good", "still here"));

        var ctx = builder.BuildFullContext();

        Assert.DoesNotContain("BROKEN", ctx, System.StringComparison.Ordinal);
        Assert.Contains("=== GOOD ===", ctx, System.StringComparison.Ordinal);
        Assert.Contains("still here",   ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFullContext_AppendsLocalContext_AsCurrentlySelectedSection()
    {
        var builder = new AppSvc.AppAiContextBuilder();
        builder.Register(new FakeProvider("Provider", "body"));

        var ctx = builder.BuildFullContext("on Cash Position tile");

        Assert.Contains("=== CURRENTLY SELECTED ===", ctx, System.StringComparison.Ordinal);
        Assert.Contains("on Cash Position tile",      ctx, System.StringComparison.Ordinal);
        // Local-context section comes after the registered providers, not interleaved.
        Assert.True(
            ctx.IndexOf("=== PROVIDER ===",            System.StringComparison.Ordinal) <
            ctx.IndexOf("=== CURRENTLY SELECTED ===", System.StringComparison.Ordinal));
    }

    [Fact]
    public void BuildFullContext_OmitsCurrentlySelectedSection_WhenLocalContextEmpty()
    {
        var builder = new AppSvc.AppAiContextBuilder();
        builder.Register(new FakeProvider("Provider", "body"));

        Assert.DoesNotContain("CURRENTLY SELECTED", builder.BuildFullContext(""), System.StringComparison.Ordinal);
        Assert.DoesNotContain("CURRENTLY SELECTED", builder.BuildFullContext(null), System.StringComparison.Ordinal);
        Assert.DoesNotContain("CURRENTLY SELECTED", builder.BuildFullContext("   "), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Unregister_RemovesProviderFromOutput()
    {
        var builder = new AppSvc.AppAiContextBuilder();
        var p1 = new FakeProvider("Alpha", "alpha");
        var p2 = new FakeProvider("Beta",  "beta");
        builder.Register(p1);
        builder.Register(p2);

        builder.Unregister(p1);
        var ctx = builder.BuildFullContext();

        Assert.DoesNotContain("ALPHA", ctx, System.StringComparison.Ordinal);
        Assert.Contains("=== BETA ===", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Register_SameProviderNameTwice_KeepsLatestOnly()
    {
        // Re-registering (e.g. window reopens) replaces the stale instance —
        // existing contract from the original implementation, locked in here.
        var builder = new AppSvc.AppAiContextBuilder();
        builder.Register(new FakeProvider("Dash", "first"));
        builder.Register(new FakeProvider("Dash", "second"));

        var ctx = builder.BuildFullContext();

        Assert.DoesNotContain("first",  ctx, System.StringComparison.Ordinal);
        Assert.Contains("second",       ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Register_SameInstanceTwice_SurvivesUntilLastUnregister()
    {
        // The Workload + PM-Capacity bug: both windows register the SAME
        // singleton VM instance. Closing one (one Unregister) must NOT strip
        // the provider while the other window still holds it; only the second
        // Unregister removes it. Ref-counted by instance.
        var builder = new AppSvc.AppAiContextBuilder();
        var shared = new FakeProvider("PM Tools", "pm body");
        builder.Register(shared);   // window A
        builder.Register(shared);   // window B (same instance)

        builder.Unregister(shared); // close A
        Assert.Contains("pm body", builder.BuildFullContext(), System.StringComparison.Ordinal);

        builder.Unregister(shared); // close B
        Assert.DoesNotContain("pm body", builder.BuildFullContext(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Unregister_StaleInstance_DoesNotEvictSupersedingProvider()
    {
        // A different instance with the same name superseded the first; a late
        // Unregister of the stale instance must be a no-op, not evict the live one.
        var builder = new AppSvc.AppAiContextBuilder();
        var stale = new FakeProvider("Dash", "stale");
        var live  = new FakeProvider("Dash", "live");
        builder.Register(stale);
        builder.Register(live);   // replaces stale (same name, new instance)

        builder.Unregister(stale); // late close of the old window — must no-op

        Assert.Contains("live", builder.BuildFullContext(), System.StringComparison.Ordinal);
    }

    // ── End-to-end proof: BuildContext bridges to BuildFullContext ──
    //
    // Batch 60 wired AppAiContextBuilder back into AppAiService.AskAsync. This
    // test asserts the bridge from VM.BuildContext → BuildFullContext continues
    // to deliver the scope data. (Batch 92c stripped the legacy "KPI methodology"
    // prose block — methodology now lives in MCP tool descriptions + system
    // prompt, both cached server-side.)

    [Fact]
    public void BuildFullContext_EmitsExecutiveScope_WhenExecutiveVmRegistered()
    {
        var vm = ExecutiveVmWithKpi(new AppFin.ExecutiveKpi(
            Title: "Cash Position",
            ValueText: "$1.2M",
            SubText: "",
            StatusMessage: ""));

        var builder = new AppSvc.AppAiContextBuilder();
        builder.Register((AppSvc.IAiContextProvider)vm);

        var ctx = builder.BuildFullContext();

        Assert.Contains("Cash Position",        ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("KPI methodology", ctx, System.StringComparison.Ordinal);
    }

    // ── Test scaffolding ──

    private static AppFin.ExecutiveSummaryViewModel ExecutiveVmWithKpi(AppFin.ExecutiveKpi kpi)
    {
        var service = (AppFin.ExecutiveSummaryService)RuntimeHelpers.GetUninitializedObject(
            typeof(AppFin.ExecutiveSummaryService));
        var vm = new AppFin.ExecutiveSummaryViewModel(service);
        var result = new AppFin.ExecutiveSummaryResult(
            GeneratedAt: new System.DateTimeOffset(2026, 5, 10, 9, 0, 0, System.TimeSpan.Zero),
            Kpis: new List<AppFin.ExecutiveKpi> { kpi },
            Trends: new List<AppFin.ExecutiveTrend>(),
            Alerts: new List<AppFin.ExecutiveAlert>(),
            MaxPostedPeriod: new System.DateTime(2026, 4, 1));

        typeof(AppFin.ExecutiveSummaryViewModel)
            .GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { result });
        return vm;
    }

    private sealed class FakeProvider : AppSvc.IAiContextProvider
    {
        private readonly string _body;
        public FakeProvider(string name, string body, bool hasData = true)
        {
            ProviderName = name;
            _body = body;
            HasData = hasData;
        }
        public string ProviderName { get; }
        public bool HasData { get; }
        public string BuildContext() => _body;
        public string BuildLocalContext() => _body;
    }

    private sealed class ThrowingProvider : AppSvc.IAiContextProvider
    {
        public ThrowingProvider(string name) { ProviderName = name; }
        public string ProviderName { get; }
        public bool HasData => true;
        public string BuildContext() => throw new System.InvalidOperationException("simulated");
        public string BuildLocalContext() => throw new System.InvalidOperationException("simulated");
    }
}
