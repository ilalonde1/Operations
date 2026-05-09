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
        // Returns invoices >= $25K outstanding AND >90 days past InvoiceDate, plus the
        // contextualizing data we need to build a dense alert: friendly client name,
        // PM owner, and the client's broader AR posture (other open invoices + how
        // much of their AR is itself >90d).
        //
        // Notes for future maintainers:
        //   * The client master table in this Deltek install is `CL` (NOT ClientInfo).
        //     CL has inconsistent column-nullability metadata under MSDASQL, so we
        //     read it via OPENQUERY which forces a stable shape on the provider side.
        //   * AR has one row per WBS sub-phase per invoice — dedupe by (WBS1, Invoice).
        //   * DueDate is intentionally ignored (often NULL in KOR's data).
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
            ),
            ClientAR AS (
                -- Per-client open-AR rollup. Sums each client's outstanding balance
                -- across all invoices they have, plus splits by 90+ vs <=90d so the
                -- alert body can say 'pattern across the relationship' vs 'one-off'.
                -- Excludes invoices on a non-Resolved collections case so the rollup
                SELECT
                    a.ClientID,
                    COUNT(DISTINCT CONCAT(a.WBS1, '|', a.Invoice))           AS OpenInvoiceCount,
                    SUM(a.OutstandingBalance)                                 AS TotalOutstanding,
                    SUM(CASE WHEN DATEDIFF(DAY, a.InvoiceDate, GETDATE()) > 90
                             THEN a.OutstandingBalance ELSE 0 END)            AS Over90Outstanding
                FROM ARDeduped a
                LEFT JOIN Mcp.CollectionsCaseInvoice cci
                       ON cci.WBS1 = a.WBS1 AND cci.InvoiceNumber = a.Invoice
                LEFT JOIN Mcp.CollectionsCase cc
                       ON cc.Id = cci.CaseId
                WHERE a.OutstandingBalance > 0
                  AND (cci.Id IS NULL OR cc.Status = N'Resolved')
                GROUP BY a.ClientID
            ),
            Clients AS (
                -- CL has metadata-shape issues with 4-part naming; OPENQUERY
                -- materializes a stable shape on the OLE-DB provider side.
                SELECT ClientID, Name
                FROM OPENQUERY([DELTEK_VP], 'SELECT ClientID, Name FROM C0000052267P_1_KOR00000000.dbo.CL')
            )
            SELECT
                a.Invoice                                         AS InvoiceNumber,
                a.WBS1                                            AS WBS1,
                pr.Name                                           AS ProjectName,
                a.ClientID                                        AS ClientID,
                cl.Name                                           AS ClientName,
                pr.ProjMgr                                        AS PmEmployeeId,
                LTRIM(RTRIM(em.FirstName + ' ' + em.LastName))    AS PmName,
                ia.Currency                                       AS Currency,
                ia.OriginalAmount                                 AS OriginalAmount,
                a.OutstandingBalance                              AS OutstandingBalance,
                CONVERT(DATE, a.InvoiceDate)                      AS InvoiceDate,
                DATEDIFF(DAY, a.InvoiceDate, GETDATE())           AS DaysOutstanding,
                car.OpenInvoiceCount                              AS ClientOpenInvoiceCount,
                car.TotalOutstanding                              AS ClientTotalOutstanding,
                car.Over90Outstanding                             AS ClientOver90Outstanding
            FROM ARDeduped a
            INNER JOIN InvoiceAmounts ia
                ON a.WBS1 = ia.WBS1 AND a.Invoice = ia.Invoice
            LEFT JOIN [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.PR pr
                ON a.WBS1 = pr.WBS1 AND LTRIM(RTRIM(pr.WBS2)) = ''
            LEFT JOIN Clients cl
                ON cl.ClientID = a.ClientID
            LEFT JOIN [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.EMMain em
                ON em.Employee = pr.ProjMgr
            LEFT JOIN ClientAR car
                ON car.ClientID = a.ClientID
            LEFT JOIN Mcp.CollectionsCaseInvoice cci
                ON cci.WBS1 = a.WBS1 AND cci.InvoiceNumber = a.Invoice
            LEFT JOIN Mcp.CollectionsCase cc
                ON cc.Id = cci.CaseId
            WHERE a.OutstandingBalance >= 25000
              AND DATEDIFF(DAY, a.InvoiceDate, GETDATE()) > 90
              AND (cci.Id IS NULL OR cc.Status = N'Resolved')
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
                var invoice    = reader["InvoiceNumber"]?.ToString() ?? "";
                var wbs1       = reader["WBS1"]?.ToString() ?? "";
                var projName   = reader["ProjectName"]?.ToString() ?? "(unknown project)";
                var clientId   = reader["ClientID"]?.ToString() ?? "";
                var clientName = reader["ClientName"] is DBNull ? null : reader["ClientName"]?.ToString();
                var pmName     = reader["PmName"] is DBNull ? null : reader["PmName"]?.ToString();
                var currency   = reader["Currency"]?.ToString() ?? "";
                var origAmt    = reader["OriginalAmount"] is DBNull ? 0m : Convert.ToDecimal(reader["OriginalAmount"]);
                var outstand   = reader["OutstandingBalance"] is DBNull ? 0m : Convert.ToDecimal(reader["OutstandingBalance"]);
                var invDate    = reader["InvoiceDate"] is DateTime dt ? dt : (DateTime?)null;
                var daysOut    = reader["DaysOutstanding"] is DBNull ? 0 : Convert.ToInt32(reader["DaysOutstanding"]);
                var clOpenN    = reader["ClientOpenInvoiceCount"] is DBNull ? 0 : Convert.ToInt32(reader["ClientOpenInvoiceCount"]);
                var clTotal    = reader["ClientTotalOutstanding"] is DBNull ? 0m : Convert.ToDecimal(reader["ClientTotalOutstanding"]);
                var clOver90   = reader["ClientOver90Outstanding"] is DBNull ? 0m : Convert.ToDecimal(reader["ClientOver90Outstanding"]);

                // Friendly client identity. Prefer ClientInfo.Name. Fall back to the
                // raw ClientID only if the name is missing AND the ID isn't a GUID
                // (GUIDs are useless to humans). Last resort: project name only.
                var clientLabel = !string.IsNullOrWhiteSpace(clientName)
                    ? clientName
                    : (clientId.Length == 32 || clientId.Contains('-')
                        ? "(client name not in Deltek)"
                        : clientId);
                var pmLabel = string.IsNullOrWhiteSpace(pmName) ? "PM unset" : $"PM {pmName}";

                // Severity: high if >= $100K + > 120d, or any single >$200K, or >2 years old.
                //           medium otherwise.
                var severity = (outstand >= 100_000m && daysOut > 120) || outstand >= 200_000m || daysOut > 730
                    ? AlertSeverity.High
                    : AlertSeverity.Medium;

                var title = $"AR risk: {clientLabel} - {currency} {outstand:N0} outstanding {daysOut}d on {wbs1}";

                // Body assembly. Dense paragraph with all the numbers a COO would
                // want to make a call without opening Deltek.
                var ageNote = daysOut > 730 ? "Over 2 years old - likely a write-off candidate worth verifying with the client. "
                            : daysOut > 365 ? "Over 1 year outstanding - escalate beyond standard collections cadence. "
                                            : "Past the standard 90-day cadence - direct outreach to the client's AP contact recommended. ";

                // Client-AR posture sentence. Tells you whether this is a one-off or
                // a pattern. The math is on the SQL side; we just narrate.
                string posture;
                if (clOpenN <= 1)
                {
                    posture = $"This is the only currently-open invoice for {clientLabel}. ";
                }
                else
                {
                    var pctOver90 = clTotal > 0 ? (clOver90 / clTotal) : 0m;
                    posture = $"{clientLabel} has {clOpenN} open invoices totalling {currency} {clTotal:N0}, " +
                              $"of which {currency} {clOver90:N0} ({pctOver90:P0}) is also >90 days. " +
                              (pctOver90 >= 0.6m ? "Pattern across the whole relationship, not a one-off. "
                                                 : "Mostly current — this overdue invoice looks like an outlier in their account. ");
                }

                var body =
                    $"Invoice {invoice} on project {wbs1} ({projName}), {pmLabel}, for {clientLabel} is {daysOut} days outstanding " +
                    $"as of {DateTime.Today:yyyy-MM-dd}. " +
                    $"Outstanding balance: {currency} {outstand:N0} (original face value {currency} {origAmt:N0}). " +
                    $"Invoice date: {invDate:yyyy-MM-dd}. " +
                    posture +
                    ageNote;

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
