#nullable enable
using System.Data.Odbc;
using Kor.Operations.App.Options;
using Kor.Operations.Mcp.Smoke.Audit;
using Kor.Operations.Mcp.Smoke.Config;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal interface ICalibrator
{
    Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct);
}

internal sealed record ExpectedAnswerValue(string Label, decimal ExpectedAmount);

internal sealed record ExpectedToolCall(
    string ToolName,
    IReadOnlyList<string> InputJsonContains,
    Func<AuditRow, bool>? Predicate = null);

internal sealed record CalibratedExpectation(
    string Label,
    IReadOnlyList<ExpectedToolCall> ExpectedToolCalls,
    IReadOnlyList<ExpectedAnswerValue> ExpectedValues)
{
    public string ExpectedTool => ExpectedToolCalls.FirstOrDefault()?.ToolName ?? "";
    public string ExpectedInputJsonContains => ExpectedToolCalls.FirstOrDefault()?.InputJsonContains.FirstOrDefault() ?? "";
    public decimal? ExpectedAmount => ExpectedValues.FirstOrDefault()?.ExpectedAmount;
}

internal abstract class CalibratorBase : ICalibrator
{
    protected CalibratorBase(SmokeServices services)
    {
        Services = services;
    }

    protected SmokeServices Services { get; }
    protected DeltekOdbcOptions Odbc => Services.OdbcOptions;
    protected FinancialsOptions Financials => Services.FinancialsOptions;

    public abstract Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct);

    protected OdbcConnection OpenConnection()
    {
        var dsn = string.IsNullOrWhiteSpace(Odbc.Dsn) ? "Deltek" : Odbc.Dsn;
        var factory = new Kor.Operations.Data.VpOdbcDsnFactory(dsn, Odbc.User, Odbc.Password, () => new Dictionary<string, string>());
        var cn = factory.Create();
        cn.Open();
        return cn;
    }

    protected static decimal SumBilledLine(Kor.Operations.Financials.BilledFinancialsResult result, string lineItem)
        => result.Lines
            .Where(l => string.Equals(l.RowKind, "SectionTotal", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(l.LineItem, lineItem, StringComparison.OrdinalIgnoreCase))
            .SelectMany(l => l.AmountsByPeriod.Values)
            .Sum();
}

internal sealed class SmokeServices : IServiceProvider
{
    public SmokeServices(SmokeConfig config)
    {
        Config = config;
        OdbcOptions = config.DeltekOdbc;
        FinancialsOptions = config.Financials;
    }

    public SmokeConfig Config { get; }
    public DeltekOdbcOptions OdbcOptions { get; }
    public FinancialsOptions FinancialsOptions { get; }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(SmokeConfig)) return Config;
        if (serviceType == typeof(DeltekOdbcOptions)) return OdbcOptions;
        if (serviceType == typeof(FinancialsOptions)) return FinancialsOptions;
        return null;
    }
}
