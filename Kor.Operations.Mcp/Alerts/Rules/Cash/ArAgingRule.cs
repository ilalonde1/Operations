using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Alerts.Rules.Cash;

public sealed class ArAgingRule : IAlertRule
{
    public string RuleId => "ar-aging";
    public AlertSection Section => AlertSection.CashAndFinancials;

    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<ArAgingRule> _logger;

    public ArAgingRule(IOptions<McpOptions> options, ILogger<ArAgingRule> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RichAlert>> RunAsync(CancellationToken ct)
    {
        // SQL validated 2026-05-08 against KOR's live Deltek linked server.
        // Returns invoices >= $25K outstanding AND >90 days past InvoiceDate.
        // (DueDate is often NULL in KOR's data, so InvoiceDate is the basis.)
        const string sql = @"
            WITH ARDeduped AS (
                SELECT
                    WBS1, Invoice,
                    MAX(ClientID)                 AS ClientID,
                    MAX(InvoiceDate)              AS InvoiceDate,
                    MAX(InvBalanceSourceCurrency) AS OutstandingBalance
                FROM [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.AR
                GROUP BY WBS1, Invoice
            ),
            InvoiceAmounts AS (
                SELECT
                    WBS1, Invoice,
                    MAX(TransactionCurrencyCode) AS Currency,
                    ABS(SUM(Amount))             AS OriginalAmount
                FROM [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.LedgerAR
                WHERE TransType = 'IN'
                GROUP BY WBS1, Invoice
            )
            SELECT
                a.Invoice                                         AS InvoiceNumber,
                a.WBS1                                            AS WBS1,
                pr.Name                                           AS ProjectName,
                a.ClientID                                        AS ClientID,
                ia.Currency                                       AS Currency,
                ia.OriginalAmount                                 AS OriginalAmount,
                a.OutstandingBalance                              AS OutstandingBalance,
                CONVERT(DATE, a.InvoiceDate)                      AS InvoiceDate,
                DATEDIFF(DAY, a.InvoiceDate, GETDATE())           AS DaysOutstanding
            FROM ARDeduped a
            INNER JOIN InvoiceAmounts ia
                ON a.WBS1 = ia.WBS1 AND a.Invoice = ia.Invoice
            LEFT JOIN [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.PR pr
                ON a.WBS1 = pr.WBS1 AND LTRIM(RTRIM(pr.WBS2)) = ''
            WHERE a.OutstandingBalance >= 25000
              AND DATEDIFF(DAY, a.InvoiceDate, GETDATE()) > 90
            ORDER BY a.InvoiceDate ASC;";

        var alerts = new List<RichAlert>();
        try
        {
            await using var cn = new SqlConnection(_options.Value.SqlConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var invoice  = reader["InvoiceNumber"]?.ToString() ?? "";
                var wbs1     = reader["WBS1"]?.ToString() ?? "";
                var name     = reader["ProjectName"]?.ToString() ?? "(unknown project)";
                var clientId = reader["ClientID"]?.ToString() ?? "(unknown client)";
                var currency = reader["Currency"]?.ToString() ?? "";
                var origAmt  = reader["OriginalAmount"] is DBNull ? 0m : Convert.ToDecimal(reader["OriginalAmount"]);
                var outstand = reader["OutstandingBalance"] is DBNull ? 0m : Convert.ToDecimal(reader["OutstandingBalance"]);
                var invDate  = reader["InvoiceDate"] is DateTime dt ? dt : (DateTime?)null;
                var daysOut  = reader["DaysOutstanding"] is DBNull ? 0 : Convert.ToInt32(reader["DaysOutstanding"]);

                // Severity: high if >= $100K + > 120d, or any single >$200K, or >2 years old.
                //           medium otherwise (i.e. all rule-qualifying items).
                var severity = (outstand >= 100_000m && daysOut > 120) || outstand >= 200_000m || daysOut > 730
                    ? AlertSeverity.High
                    : AlertSeverity.Medium;

                var title = $"AR risk: invoice {invoice} - {clientId} - {currency} {outstand:N0} outstanding {daysOut}d";

                var body =
                    $"Invoice {invoice} on project {wbs1} ({name}) for client {clientId} is {daysOut} days outstanding " +
                    $"as of {DateTime.Today:yyyy-MM-dd}. " +
                    $"Outstanding balance: {currency} {outstand:N0} (original face value {currency} {origAmt:N0}). " +
                    $"Invoice date: {invDate:yyyy-MM-dd}. " +
                    (daysOut > 730 ? "Over 2 years old - likely a write-off candidate worth verifying with the client. " :
                     daysOut > 365 ? "Over 1 year outstanding - escalate beyond standard collections cadence. " :
                                     "Past the standard 90-day cadence - direct outreach to the client's AP contact recommended. ");

                alerts.Add(new RichAlert(
                    RuleId: RuleId,
                    Section: Section,
                    Severity: severity,
                    Title: title,
                    Body: body,
                    Subject: invoice));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ArAgingRule failed; emitting zero alerts for this run.");
        }

        return alerts;
    }
}
