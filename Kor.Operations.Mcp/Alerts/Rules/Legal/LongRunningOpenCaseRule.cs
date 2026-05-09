using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Alerts.Rules.Legal;

/// <summary>
/// Fires when a case is still in the "Open" pre-legal status more than
/// <see cref="OpenDaysThreshold"/> days after it was opened. "Open" should
/// be a brief flagging period — within two weeks the case should escalate
/// to Lien / Foreclosure, get written off, or get resolved.
/// </summary>
public sealed class LongRunningOpenCaseRule : IAlertRule
{
    public string RuleId => "long-running-open-case";
    public AlertSection Section => AlertSection.CashAndFinancials;

    private const int OpenDaysThreshold = 14;

    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<LongRunningOpenCaseRule> _logger;

    public LongRunningOpenCaseRule(IOptions<McpOptions> options, ILogger<LongRunningOpenCaseRule> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RichAlert>> RunAsync(CancellationToken ct)
    {
        const string sql = @"
WITH Clients AS (
    SELECT ClientID, Name
    FROM OPENQUERY([DELTEK_VP], 'SELECT ClientID, Name FROM C0000052267P_1_KOR00000000.dbo.CL')
)
SELECT cc.Id,
       cc.ClientID,
       cl.Name AS ClientName,
       CONVERT(DATE, cc.OpenedAt) AS OpenedAt,
       DATEDIFF(DAY, cc.OpenedAt, GETDATE()) AS DaysOpen,
       cc.LegalAmount,
       (SELECT COUNT(*) FROM Mcp.CollectionsCaseInvoice cci WHERE cci.CaseId = cc.Id) AS InvoiceCount
FROM Mcp.CollectionsCase cc
LEFT JOIN Clients cl ON cl.ClientID = cc.ClientID
WHERE cc.Status = N'Open'
  AND DATEDIFF(DAY, cc.OpenedAt, GETDATE()) > @OpenDays
ORDER BY cc.OpenedAt ASC;";

        var alerts = new List<RichAlert>();
        try
        {
            await using var cn = new SqlConnection(_options.Value.SqlConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@OpenDays", OpenDaysThreshold);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var caseId = reader.GetInt64(0);
                var clientId = reader.GetString(1);
                var clientName = reader.IsDBNull(2) ? null : reader.GetString(2);
                var openedAt = reader.GetDateTime(3);
                var daysOpen = reader.GetInt32(4);
                var legalAmount = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5);
                var invoiceCount = reader.GetInt32(6);

                var label = string.IsNullOrWhiteSpace(clientName) ? clientId : clientName;

                var title = $"Open collections case still flagged: {label} - {daysOpen}d";
                var body =
                    $"Case for {label} has been in 'Open' status for {daysOpen} days (since {openedAt:yyyy-MM-dd}) " +
                    $"covering {invoiceCount} invoice{(invoiceCount == 1 ? "" : "s")}" +
                    (legalAmount.HasValue ? $" totalling ${legalAmount.Value:N0}" : "") +
                    ". Either escalate to Lien / Foreclosure, write it off, or resolve. " +
                    "An indefinitely-Open case is invisible to AR aging but doing nothing legally — that's the worst of both.";

                alerts.Add(new RichAlert(
                    RuleId: RuleId,
                    Section: Section,
                    Severity: AlertSeverity.Medium,
                    Title: title,
                    Body: body,
                    Subject: caseId.ToString()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LongRunningOpenCaseRule failed; emitting zero alerts.");
        }

        return alerts;
    }
}
