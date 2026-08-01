using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Alerts.Rules.Legal;

/// <summary>
/// Fires when a non-Resolved collections case has not been updated in
/// <see cref="StaleDaysThreshold"/> days. Catches cases that get opened
/// then forgotten — the legal action stalled, but no one came back to
/// move it to Resolved.
/// </summary>
public sealed class StaleCollectionsCaseRule : LegalRuleBase
{
    private const int StaleDaysThreshold = 30;
    private const int HighSeverityDaysThreshold = 60;

    public StaleCollectionsCaseRule(IOptions<McpOptions> options, ILogger<StaleCollectionsCaseRule> logger)
        : base(options, logger)
    {
    }

    public override string RuleId => "stale-collections-case";

    protected override string Sql => @"
WITH Clients AS (
    SELECT ClientID, Name
    FROM OPENQUERY([DELTEK_VP], 'SELECT ClientID, Name FROM C0000052267P_1_KOR00000000.dbo.CL')
)
SELECT cc.Id,
       cc.ClientID,
       cl.Name AS ClientName,
       cc.Status,
       CONVERT(DATE, cc.LastUpdatedAt) AS LastUpdated,
       cc.LastUpdatedBy,
       DATEDIFF(DAY, cc.LastUpdatedAt, GETDATE()) AS DaysStale,
       cc.LegalAmount,
       cc.Notes
FROM Mcp.CollectionsCase cc
LEFT JOIN Clients cl ON cl.ClientID = cc.ClientID
WHERE cc.Status <> N'Resolved'
  AND DATEDIFF(DAY, cc.LastUpdatedAt, GETDATE()) > @StaleDays
ORDER BY cc.LastUpdatedAt ASC;";

    protected override void BindParameters(SqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("@StaleDays", StaleDaysThreshold);
    }

    protected override RichAlert MapRow(SqlDataReader reader)
    {
        var caseId = reader.GetInt64(0);
        var clientId = reader.GetString(1);
        var clientName = reader.IsDBNull(2) ? null : reader.GetString(2);
        var status = reader.GetString(3);
        var lastUpdated = reader.GetDateTime(4);
        var lastUpdatedBy = reader.GetString(5);
        var daysStale = reader.GetInt32(6);
        var legalAmount = reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7);
        var notes = reader.IsDBNull(8) ? null : reader.GetString(8);

        var label = string.IsNullOrWhiteSpace(clientName) ? clientId : clientName;
        var severity = daysStale > HighSeverityDaysThreshold
            ? AlertSeverity.High
            : AlertSeverity.Medium;

        var title = $"Stale collections case: {label} - {status} - {daysStale}d since update";
        var body =
            $"Collections case for {label} (status {status}) has not been touched in {daysStale} days. " +
            $"Last update: {lastUpdated:yyyy-MM-dd} by {lastUpdatedBy}. " +
            (legalAmount.HasValue ? $"Legal amount: ${legalAmount.Value:N0}. " : "") +
            (string.IsNullOrWhiteSpace(notes) ? "" : $"Last notes: \"{notes.Trim()}\". ") +
            "Either record progress on the case or move it to Resolved.";

        return new RichAlert(
            RuleId: RuleId,
            Section: Section,
            Severity: severity,
            Title: title,
            Body: body,
            Subject: caseId.ToString());
    }
}
