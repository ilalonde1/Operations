using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Alerts.Rules.Legal;

/// <summary>
/// Fires when an invoice on a WriteOff case has its AR balance drop to
/// $0 — meaning the client paid us for something we'd written off as
/// uncollectable. High-severity surprise revenue worth investigating
/// (revenue recovery posting, accounting reconciliation, possibly
/// reversing the write-off in the books).
/// </summary>
public sealed class WrittenOffInvoicePaidRule : IAlertRule
{
    public string RuleId => "written-off-invoice-paid";
    public AlertSection Section => AlertSection.CashAndFinancials;

    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<WrittenOffInvoicePaidRule> _logger;

    public WrittenOffInvoicePaidRule(IOptions<McpOptions> options, ILogger<WrittenOffInvoicePaidRule> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RichAlert>> RunAsync(CancellationToken ct)
    {
        // Per-invoice grain: WriteOff cases can cover N invoices, each may
        // pay back independently. ARDeduped collapses AR's per-WBS duplicate
        // rows so we get one balance per (WBS1, Invoice).
        const string sql = @"
WITH Clients AS (
    SELECT ClientID, Name
    FROM OPENQUERY([DELTEK_VP], 'SELECT ClientID, Name FROM C0000052267P_1_KOR00000000.dbo.CL')
),
ARDeduped AS (
    SELECT WBS1, Invoice,
           MAX(InvoiceDate) AS InvoiceDate,
           MAX(InvBalanceSourceCurrency) AS OutstandingBalance
    FROM [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.AR
    GROUP BY WBS1, Invoice
)
SELECT cc.Id AS CaseId,
       cc.ClientID,
       cl.Name AS ClientName,
       cci.WBS1,
       cci.InvoiceNumber,
       cc.ResolvedAt,
       a.OutstandingBalance,
       a.InvoiceDate
FROM Mcp.CollectionsCase cc
INNER JOIN Mcp.CollectionsCaseInvoice cci ON cci.CaseId = cc.Id
LEFT JOIN ARDeduped a ON a.WBS1 = cci.WBS1 AND a.Invoice = cci.InvoiceNumber
LEFT JOIN Clients cl ON cl.ClientID = cc.ClientID
WHERE cc.Status = N'WriteOff'
  AND a.OutstandingBalance IS NOT NULL
  AND a.OutstandingBalance <= 0
ORDER BY cci.WBS1, cci.InvoiceNumber;";

        var alerts = new List<RichAlert>();
        try
        {
            await using var cn = new SqlConnection(_options.Value.SqlConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var caseId = reader.GetInt64(0);
                var clientId = reader.GetString(1);
                var clientName = reader.IsDBNull(2) ? null : reader.GetString(2);
                var wbs1 = reader.GetString(3);
                var invoiceNumber = reader.GetString(4);
                var resolvedAt = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
                var balance = reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetValue(6));
                var invoiceDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7);

                var label = string.IsNullOrWhiteSpace(clientName) ? clientId : clientName;

                var title = $"Written-off invoice paid: {label} - Invoice {invoiceNumber}";
                var body =
                    $"Invoice {invoiceNumber} on {wbs1} for {label} was written off (case #{caseId}) but its current AR balance is ${balance:N2}. " +
                    "That means the client paid for something we wrote off — accounting needs to reconcile the recovery. " +
                    "Consider reversing the write-off in the books and reviewing whether other invoices on this case may also recover.";

                alerts.Add(new RichAlert(
                    RuleId: RuleId,
                    Section: Section,
                    Severity: AlertSeverity.High,
                    Title: title,
                    Body: body,
                    Subject: $"{wbs1}|{invoiceNumber}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WrittenOffInvoicePaidRule failed; emitting zero alerts.");
        }

        return alerts;
    }
}
