using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Alerts.Rules.Legal;

/// <summary>
/// Fires when a Lien-status case has a LienExpiryDate within
/// <see cref="WarningWindowDays"/> days (or already past). BC builders'
/// liens normally expire one year after filing unless extended; missing
/// the window means the lien lapses and the security is gone.
/// </summary>
public sealed class LienExpiryRule : IAlertRule
{
    public string RuleId => "lien-expiry-approaching";
    public AlertSection Section => AlertSection.CashAndFinancials;

    private const int WarningWindowDays = 30;

    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<LienExpiryRule> _logger;

    public LienExpiryRule(IOptions<McpOptions> options, ILogger<LienExpiryRule> logger)
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
       cc.LienExpiryDate,
       DATEDIFF(DAY, GETDATE(), cc.LienExpiryDate) AS DaysToExpiry,
       cc.LegalAmount
FROM Mcp.CollectionsCase cc
LEFT JOIN Clients cl ON cl.ClientID = cc.ClientID
WHERE cc.Status = N'Lien'
  AND cc.LienExpiryDate IS NOT NULL
  AND DATEDIFF(DAY, GETDATE(), cc.LienExpiryDate) <= @WarnDays
ORDER BY cc.LienExpiryDate ASC;";

        var alerts = new List<RichAlert>();
        try
        {
            await using var cn = new SqlConnection(_options.Value.SqlConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@WarnDays", WarningWindowDays);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var caseId = reader.GetInt64(0);
                var clientId = reader.GetString(1);
                var clientName = reader.IsDBNull(2) ? null : reader.GetString(2);
                var expiryDate = reader.GetDateTime(3);
                var daysToExpiry = reader.GetInt32(4);
                var legalAmount = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5);

                var label = string.IsNullOrWhiteSpace(clientName) ? clientId : clientName;

                string title;
                string urgency;
                if (daysToExpiry < 0)
                {
                    title = $"Lien EXPIRED: {label} - {Math.Abs(daysToExpiry)}d ago";
                    urgency = "EXPIRED. The lien has already lapsed — security is gone unless it was extended and the date wasn't updated.";
                }
                else if (daysToExpiry <= 7)
                {
                    title = $"Lien expiring this week: {label} - {daysToExpiry}d";
                    urgency = "Less than a week. Extend or move to Foreclosure / WriteOff this week.";
                }
                else
                {
                    title = $"Lien approaching expiry: {label} - {daysToExpiry}d";
                    urgency = $"Lien expires in {daysToExpiry} days ({expiryDate:yyyy-MM-dd}). Plan extension, foreclosure, or write-off before then.";
                }

                var body =
                    $"Lien against {label} (case #{caseId}) " +
                    (legalAmount.HasValue ? $"covering ${legalAmount.Value:N0} " : "") +
                    $"expires {expiryDate:yyyy-MM-dd}. " +
                    urgency;

                alerts.Add(new RichAlert(
                    RuleId: RuleId,
                    Section: Section,
                    Severity: daysToExpiry < 0 || daysToExpiry <= 7 ? AlertSeverity.High : AlertSeverity.Medium,
                    Title: title,
                    Body: body,
                    Subject: caseId.ToString()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LienExpiryRule failed; emitting zero alerts.");
        }

        return alerts;
    }
}
