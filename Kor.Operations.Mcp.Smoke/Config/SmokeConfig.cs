#nullable enable
using Kor.Operations.App.Options;
using Microsoft.Extensions.Configuration;

namespace Kor.Operations.Mcp.Smoke.Config;

internal sealed class SmokeConfig
{
    private const string ProductionFallback = @"\\KOR-APP01\C$\Program Files\KorOperations\Mcp\appsettings.Production.json";

    public SmokeMcpConfig Mcp { get; init; } = new();
    public DeltekOdbcOptions DeltekOdbc { get; init; } = new();
    public FinancialsOptions Financials { get; init; } = new();
    public string SourcePath { get; init; } = "";

    internal static SmokeConfig Load()
    {
        var local = Path.GetFullPath("appsettings.smoke.json");
        var source = File.Exists(local) ? local : ProductionFallback;
        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                "Smoke config not found. Expected ./appsettings.smoke.json or production fallback.",
                source);
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(source, optional: false, reloadOnChange: false)
            .Build();

        var cfg = new SmokeConfig
        {
            Mcp = configuration.GetSection("Mcp").Get<SmokeMcpConfig>() ?? new SmokeMcpConfig(),
            DeltekOdbc = configuration.GetSection("DeltekOdbc").Get<DeltekOdbcOptions>() ?? new DeltekOdbcOptions(),
            Financials = configuration.GetSection("Financials").Get<FinancialsOptions>() ?? new FinancialsOptions(),
            SourcePath = source,
        };
        if (string.IsNullOrWhiteSpace(cfg.Mcp.Endpoint))
            cfg.Mcp.Endpoint = "http://kor-app01:5500/ask";
        return cfg;
    }
}

internal sealed class SmokeMcpConfig
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string SqlConnectionString { get; init; } = "";
    public string Endpoint { get; set; } = "http://kor-app01:5500/ask";

    // Mirrors McpOptions.EmployeeSummaryExcludedIds so calibrators apply the
    // same exclusion list the live tool does. Without this, populating the
    // production filter would silently break get_employee_performance smoke
    // calibration (Codex Batch-100 audit, 2026-05-14).
    public IReadOnlyCollection<string> EmployeeSummaryExcludedIds { get; init; } = Array.Empty<string>();
}
