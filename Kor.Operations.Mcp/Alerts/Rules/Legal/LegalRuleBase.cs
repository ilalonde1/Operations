using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Alerts.Rules.Legal;

/// <summary>
/// Shared scaffolding for the legal-track collections rules. Each concrete
/// rule supplies its <see cref="IAlertRule.RuleId"/>, the SQL it wants to
/// run, an optional parameter binder, and a per-row mapping from
/// <see cref="SqlDataReader"/> to a <see cref="RichAlert"/>. The base
/// handles the connection lifecycle and the catch-and-log fallback so
/// rules can't accidentally divaricate on those bits — and adding a
/// fifth rule no longer means cargo-culting another 90-line copy.
/// </summary>
public abstract class LegalRuleBase : IAlertRule
{
    private readonly IOptions<McpOptions> _options;
    private readonly ILogger _logger;

    protected LegalRuleBase(IOptions<McpOptions> options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public abstract string RuleId { get; }
    public AlertSection Section => AlertSection.CashAndFinancials;

    protected abstract string Sql { get; }

    /// <summary>
    /// Optional. Override to bind SQL parameters before the reader executes.
    /// Default is a no-op — many rules have no parameters.
    /// </summary>
    protected virtual void BindParameters(SqlCommand cmd) { }

    /// <summary>
    /// Map one reader row to zero or one alert. Returning <c>null</c> lets
    /// the rule skip rows that don't merit emitting (e.g., a guard row that
    /// passed the WHERE clause but failed a downstream sanity check).
    /// </summary>
    protected abstract RichAlert? MapRow(SqlDataReader reader);

    public async Task<IReadOnlyList<RichAlert>> RunAsync(CancellationToken ct)
    {
        var alerts = new List<RichAlert>();
        try
        {
            await using var cn = new SqlConnection(_options.Value.SqlConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(Sql, cn) { CommandTimeout = 60 };
            BindParameters(cmd);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var alert = MapRow(reader);
                if (alert is not null)
                {
                    alerts.Add(alert);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{RuleId} failed; emitting zero alerts.", RuleId);
        }

        return alerts;
    }
}
