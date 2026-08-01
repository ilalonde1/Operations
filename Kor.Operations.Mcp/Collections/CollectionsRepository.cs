using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Collections;

public sealed class CollectionsRepository
{
    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<CollectionsRepository> _logger;

    public CollectionsRepository(IOptions<McpOptions> options, ILogger<CollectionsRepository> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<IReadOnlyList<CollectionsCaseRow>> GetAllAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT cc.Id, cc.ClientID, cc.Status, cc.OpenedAt, cc.OpenedBy,
       cc.LastUpdatedAt, cc.LastUpdatedBy, cc.ResolvedAt, cc.LegalAmount, cc.Notes,
       (SELECT COUNT(*) FROM Mcp.CollectionsCaseInvoice cci WHERE cci.CaseId = cc.Id) AS InvoiceCount,
       cc.LienExpiryDate
FROM Mcp.CollectionsCase cc
ORDER BY cc.OpenedAt DESC;";

        return QueryCasesAsync(sql, null, ct);
    }

    public Task<IReadOnlyList<CollectionsCaseRow>> GetActiveAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT cc.Id, cc.ClientID, cc.Status, cc.OpenedAt, cc.OpenedBy,
       cc.LastUpdatedAt, cc.LastUpdatedBy, cc.ResolvedAt, cc.LegalAmount, cc.Notes,
       (SELECT COUNT(*) FROM Mcp.CollectionsCaseInvoice cci WHERE cci.CaseId = cc.Id) AS InvoiceCount,
       cc.LienExpiryDate
FROM Mcp.CollectionsCase cc
WHERE cc.Status <> N'Resolved'
ORDER BY cc.OpenedAt DESC;";

        return QueryCasesAsync(sql, null, ct);
    }

    // Flat list of (WBS1, Invoice) pairs on every non-Resolved case. Drives
    // the WPF Financials layer's "of which in collections" segregation: it
    // can subtract these invoices' AR balance from the headline Outstanding
    // KPI without N+1 lookups against /collections/{id}.
    public async Task<IReadOnlyList<ActiveCaseInvoiceRow>> GetActiveCaseInvoicesAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT cc.Id, cc.ClientID, cc.Status, cci.WBS1, cci.InvoiceNumber
FROM Mcp.CollectionsCaseInvoice cci
INNER JOIN Mcp.CollectionsCase cc ON cc.Id = cci.CaseId
WHERE cc.Status <> N'Resolved'
ORDER BY cci.WBS1, cci.InvoiceNumber;";

        var rows = new List<ActiveCaseInvoiceRow>();
        try
        {
            await using var cn = new SqlConnection(_options.Value.SqlConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(new ActiveCaseInvoiceRow(
                    CaseId: reader.GetInt64(0),
                    ClientID: reader.GetString(1),
                    Status: reader.GetString(2),
                    WBS1: reader.GetString(3),
                    InvoiceNumber: reader.GetString(4)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetActiveCaseInvoicesAsync failed; returning empty list.");
        }

        return rows;
    }

    public async Task<CollectionsCaseRow?> GetActiveByClientAsync(string clientId, CancellationToken ct)
    {
        const string sql = @"
SELECT cc.Id, cc.ClientID, cc.Status, cc.OpenedAt, cc.OpenedBy,
       cc.LastUpdatedAt, cc.LastUpdatedBy, cc.ResolvedAt, cc.LegalAmount, cc.Notes,
       (SELECT COUNT(*) FROM Mcp.CollectionsCaseInvoice cci WHERE cci.CaseId = cc.Id) AS InvoiceCount,
       cc.LienExpiryDate
FROM Mcp.CollectionsCase cc
WHERE cc.ClientID = @ClientID
  AND cc.Status <> N'Resolved';";

        var rows = await QueryCasesAsync(
            sql,
            cmd => cmd.Parameters.AddWithValue("@ClientID", clientId),
            ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<IReadOnlyList<ClientArInvoiceRow>> GetOpenArByClientAsync(
        string clientId, CancellationToken ct)
    {
        // Sanitize ClientID — Deltek IDs are alphanumeric (e.g. "CL00439" or
        // 32-char GUID-without-dashes). We embed the value into an OPENQUERY
        // string literal so it must be safe to interpolate; reject anything
        // outside [A-Za-z0-9_-].
        if (string.IsNullOrEmpty(clientId)
            || !System.Text.RegularExpressions.Regex.IsMatch(clientId, "^[A-Za-z0-9_-]+$"))
        {
            return Array.Empty<ClientArInvoiceRow>();
        }

        // Performance: previous CTE-based version did multiple cross-linked-server
        // round-trips (ARDeduped + InvoiceAmounts + PR), and the optimizer couldn't
        // push the ClientID filter through. ~30s on KOR's 5-invoice clients.
        // OPENQUERY runs the full AR+LedgerAR+PR join remotely on Deltek in one
        // round-trip; we only LEFT JOIN to local Mcp.* tables here. ~1-2s.
        var inner = $@"
WITH ARDeduped AS (
    SELECT WBS1, Invoice, MAX(InvoiceDate) AS InvoiceDate, MAX(InvBalanceSourceCurrency) AS OutstandingBalance
    FROM C0000052267P_1_KOR00000000.dbo.AR
    WHERE ClientID = '{clientId}'
    GROUP BY WBS1, Invoice
),
InvoiceAmounts AS (
    SELECT la.WBS1, la.Invoice,
           MAX(la.TransactionCurrencyCode) AS Currency,
           ABS(SUM(la.Amount)) AS OriginalAmount
    FROM C0000052267P_1_KOR00000000.dbo.LedgerAR la
    INNER JOIN C0000052267P_1_KOR00000000.dbo.AR a
        ON a.WBS1 = la.WBS1 AND a.Invoice = la.Invoice AND a.ClientID = '{clientId}'
    WHERE la.TransType = 'IN'
    GROUP BY la.WBS1, la.Invoice
)
SELECT a.WBS1, a.Invoice, pr.Name AS ProjectName,
       ia.Currency, ia.OriginalAmount, a.OutstandingBalance,
       CONVERT(DATE, a.InvoiceDate) AS InvoiceDate,
       DATEDIFF(DAY, a.InvoiceDate, GETDATE()) AS DaysOutstanding
FROM ARDeduped a
INNER JOIN InvoiceAmounts ia ON a.WBS1 = ia.WBS1 AND a.Invoice = ia.Invoice
LEFT JOIN C0000052267P_1_KOR00000000.dbo.PR pr ON a.WBS1 = pr.WBS1 AND LTRIM(RTRIM(pr.WBS2)) = ''
WHERE a.OutstandingBalance > 0";

        // Wrap inner for OPENQUERY — every single quote in inner needs to be
        // doubled to survive the outer string literal.
        var quoted = inner.Replace("'", "''");
        var sql = $@"
SELECT t.WBS1, t.Invoice AS InvoiceNumber, t.ProjectName, t.Currency,
       t.OriginalAmount, t.OutstandingBalance, t.InvoiceDate, t.DaysOutstanding,
       cc.Id AS ActiveCaseId
FROM OPENQUERY([DELTEK_VP], '{quoted}') AS t
LEFT JOIN Mcp.CollectionsCaseInvoice cci
    ON cci.WBS1 = t.WBS1 AND cci.InvoiceNumber = t.Invoice
LEFT JOIN Mcp.CollectionsCase cc
    ON cc.Id = cci.CaseId AND cc.Status <> N'Resolved'
ORDER BY t.InvoiceDate ASC, t.Invoice ASC;";

        var rows = new List<ClientArInvoiceRow>();
        try
        {
            await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(new ClientArInvoiceRow(
                    WBS1: reader.GetString(0),
                    InvoiceNumber: reader.GetString(1),
                    ProjectName: reader.IsDBNull(2) ? null : reader.GetString(2),
                    Currency: reader.IsDBNull(3) ? "" : reader.GetString(3),
                    OriginalAmount: reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                    OutstandingBalance: reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                    InvoiceDate: reader.GetDateTime(6),
                    DaysOutstanding: reader.GetInt32(7),
                    ActiveCaseId: reader.IsDBNull(8) ? null : reader.GetInt64(8)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query open AR for client {ClientId}.", clientId);
        }

        return rows;
    }

    public async Task<long> InsertAsync(
        string clientId,
        CollectionsCaseStatus status,
        decimal? legalAmount,
        string? notes,
        IReadOnlyList<InvoiceRef>? invoices,
        DateTime? lienExpiryDate,
        string openedBy,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO Mcp.CollectionsCase
    (ClientID, Status, OpenedBy, LastUpdatedBy, LegalAmount, Notes, LienExpiryDate)
OUTPUT INSERTED.Id
VALUES
    (@ClientID, @Status, @OpenedBy, @OpenedBy, @LegalAmount, @Notes, @LienExpiryDate);";

        await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using var tx = conn.BeginTransaction();
        try
        {
            await using var cmd = new SqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@Status", status.ToString());
            cmd.Parameters.AddWithValue("@OpenedBy", openedBy);
            cmd.Parameters.AddWithValue("@LegalAmount", (object?)legalAmount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LienExpiryDate", (object?)lienExpiryDate ?? DBNull.Value);
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            var caseId = Convert.ToInt64(result);
            await ReplaceInvoicesInternalAsync(tx, caseId, invoices ?? Array.Empty<InvoiceRef>(), openedBy, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return caseId;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task UpdateAsync(
        long id,
        CollectionsCaseStatus status,
        decimal? legalAmount,
        string? notes,
        IReadOnlyList<InvoiceRef>? invoices,
        DateTime? lienExpiryDate,
        string updatedBy,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE Mcp.CollectionsCase
SET Status = @Status,
    LegalAmount = @LegalAmount,
    Notes = @Notes,
    LienExpiryDate = @LienExpiryDate,
    LastUpdatedAt = SYSUTCDATETIME(),
    LastUpdatedBy = @UpdatedBy,
    ResolvedAt = CASE
        WHEN @Status = N'Resolved' THEN COALESCE(ResolvedAt, SYSUTCDATETIME())
        ELSE NULL
    END
WHERE Id = @Id;";

        await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using var tx = conn.BeginTransaction();
        try
        {
            await using var cmd = new SqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Status", status.ToString());
            cmd.Parameters.AddWithValue("@LegalAmount", (object?)legalAmount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LienExpiryDate", (object?)lienExpiryDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await ReplaceInvoicesInternalAsync(tx, id, invoices ?? Array.Empty<InvoiceRef>(), updatedBy, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CollectionsCaseDetailRow?> GetByIdAsync(long id, CancellationToken ct)
    {
        const string headerSql = @"
SELECT cc.Id, cc.ClientID, cc.Status, cc.OpenedAt, cc.OpenedBy,
       cc.LastUpdatedAt, cc.LastUpdatedBy, cc.ResolvedAt, cc.LegalAmount, cc.Notes,
       (SELECT COUNT(*) FROM Mcp.CollectionsCaseInvoice cci WHERE cci.CaseId = cc.Id) AS InvoiceCount,
       cc.LienExpiryDate
FROM Mcp.CollectionsCase cc
WHERE cc.Id = @Id;";

        const string invoiceSql = @"
SELECT Id, CaseId, WBS1, InvoiceNumber, AddedAt, AddedBy
FROM Mcp.CollectionsCaseInvoice
WHERE CaseId = @Id
ORDER BY AddedAt;";

        try
        {
            await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            CollectionsCaseRow? header;
            await using (var cmd = new SqlCommand(headerSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return null;
                }

                header = ReadRow(reader);
            }

            var invoices = new List<CollectionsCaseInvoiceRow>();
            await using (var cmd = new SqlCommand(invoiceSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    invoices.Add(ReadInvoiceRow(reader));
                }
            }

            return new CollectionsCaseDetailRow(header, invoices);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query collections case detail for {Id}.", id);
            return null;
        }
    }

    private static async Task ReplaceInvoicesInternalAsync(
        SqlTransaction tx,
        long caseId,
        IReadOnlyList<InvoiceRef> invoices,
        string addedBy,
        CancellationToken ct)
    {
        var conn = tx.Connection ?? throw new InvalidOperationException("Transaction connection is not available.");

        const string deleteSql = @"
DELETE FROM Mcp.CollectionsCaseInvoice
WHERE CaseId = @CaseId;";

        await using (var cmd = new SqlCommand(deleteSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("@CaseId", caseId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        const string insertSql = @"
INSERT INTO Mcp.CollectionsCaseInvoice
    (CaseId, WBS1, InvoiceNumber, AddedBy)
VALUES
    (@CaseId, @WBS1, @InvoiceNumber, @AddedBy);";

        foreach (var invoice in invoices)
        {
            await using var cmd = new SqlCommand(insertSql, conn, tx);
            cmd.Parameters.AddWithValue("@CaseId", caseId);
            cmd.Parameters.AddWithValue("@WBS1", invoice.WBS1);
            cmd.Parameters.AddWithValue("@InvoiceNumber", invoice.InvoiceNumber);
            cmd.Parameters.AddWithValue("@AddedBy", addedBy);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<CollectionsCaseRow>> QueryCasesAsync(
        string sql,
        Action<SqlCommand>? configure,
        CancellationToken ct)
    {
        var rows = new List<CollectionsCaseRow>();
        try
        {
            await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            configure?.Invoke(cmd);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(ReadRow(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query collections cases.");
        }

        return rows;
    }

    private static CollectionsCaseRow ReadRow(SqlDataReader reader)
        => new(
            Id: reader.GetInt64(0),
            ClientID: reader.GetString(1),
            Status: reader.GetString(2),
            OpenedAt: reader.GetDateTime(3),
            OpenedBy: reader.GetString(4),
            LastUpdatedAt: reader.GetDateTime(5),
            LastUpdatedBy: reader.GetString(6),
            ResolvedAt: reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            LegalAmount: reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            Notes: reader.IsDBNull(9) ? null : reader.GetString(9),
            InvoiceCount: reader.GetInt32(10),
            LienExpiryDate: reader.IsDBNull(11) ? null : reader.GetDateTime(11));

    private static CollectionsCaseInvoiceRow ReadInvoiceRow(SqlDataReader reader)
        => new(
            Id: reader.GetInt64(0),
            CaseId: reader.GetInt64(1),
            WBS1: reader.GetString(2),
            InvoiceNumber: reader.GetString(3),
            AddedAt: reader.GetDateTime(4),
            AddedBy: reader.GetString(5));
}
