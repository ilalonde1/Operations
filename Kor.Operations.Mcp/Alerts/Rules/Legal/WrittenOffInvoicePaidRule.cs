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
public sealed class WrittenOffInvoicePaidRule : LegalRuleBase
{
    public WrittenOffInvoicePaidRule(IOptions<McpOptions> options, ILogger<WrittenOffInvoicePaidRule> logger)
        : base(options, logger)
    {
    }

    public override string RuleId => "written-off-invoice-paid";

    // Per-invoice grain: WriteOff cases can cover N invoices, each may
    // pay back independently. ARDeduped collapses AR's per-WBS duplicate
    // rows so we get one balance per (WBS1, Invoice).
    protected override string Sql => @"
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

    protected override RichAlert MapRow(SqlDataReader reader)
    {
        var caseId = reader.GetInt64(0);
        var clientId = reader.GetString(1);
        var clientName = reader.IsDBNull(2) ? null : reader.GetString(2);
        var wbs1 = reader.GetString(3);
        var invoiceNumber = reader.GetString(4);
        // ResolvedAt + InvoiceDate kept off the projection list — not used in the
        // alert body, but they're handy when troubleshooting from the same SELECT.
        _ = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
        var balance = reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetValue(6));
        _ = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7);

        var label = string.IsNullOrWhiteSpace(clientName) ? clientId : clientName;

        var title = $"Written-off invoice paid: {label} - Invoice {invoiceNumber}";
        var body =
            // OutstandingBalance is InvBalanceSourceCurrency — the invoice's
            // own currency (CAD for BC orgs, USD for LA/SD), NOT a single
            // firm-wide currency. Body says so explicitly to avoid the
            // reader assuming CAD when an LA/SD invoice is USD.
            $"Invoice {invoiceNumber} on {wbs1} for {label} was written off (case #{caseId}) but its current AR balance is {balance:N2} (source currency). " +
            "That means the client paid for something we wrote off — accounting needs to reconcile the recovery. " +
            "Consider reversing the write-off in the books and reviewing whether other invoices on this case may also recover.";

        return new RichAlert(
            RuleId: RuleId,
            Section: Section,
            Severity: AlertSeverity.High,
            Title: title,
            Body: body,
            Subject: $"{wbs1}|{invoiceNumber}");
    }
}
