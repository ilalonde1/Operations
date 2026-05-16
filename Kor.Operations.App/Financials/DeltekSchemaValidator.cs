#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Kor.Operations.Financials.Loaders;
using Serilog;

namespace Kor.Operations.Financials;

/// <summary>
/// Verifies that the columns the financial loaders depend on actually exist in
/// the live Deltek catalog. A Deltek upgrade that renames or drops a column
/// would otherwise produce silent NULLs and zero-valued tiles. This is a
/// preflight contract check, run once per process per catalog and surfaced to
/// the user as a banner if drift is detected.
/// </summary>
internal static class DeltekSchemaValidator
{
    /// <summary>
    /// (Table, Column) pairs the financial loaders rely on. Update this list
    /// whenever a loader adds a new dependency. The cost of a false-positive
    /// (a missing column that's actually optional) is much lower than the cost
    /// of silent NULL data; err on the side of including a column here.
    /// </summary>
    internal static readonly IReadOnlyList<(string Table, string Column)> ExpectedColumns =
        new[]
        {
            ("AR", "WBS1"),
            ("AR", "InvBalanceSourceCurrency"),
            ("AR", "InvoiceDate"),
            ("AR", "DueDate"),
            ("PR", "WBS1"),
            ("PR", "WBS2"),
            ("PR", "Org"),
            ("PR", "Name"),
            ("PR", "ProjMgr"),
            ("PRSummaryMain", "WBS1"),
            ("PRSummaryMain", "Period"),
            ("PRSummaryMain", "BilledFee"),
            ("PRSummaryMain", "Revenue"),
            ("PRSummaryMain", "Billed"),
            ("PRSummaryMain", "Unbilled"),
            ("PRSummaryMain", "AR"),
            ("LedgerAR", "TransType"),
            ("LedgerAR", "Amount"),
            ("LedgerAR", "Account"),
            ("LedgerAR", "Period"),
            ("CFGBanks", "Account"),
            ("CFGBanks", "Org"),
            ("CFGBanks", "Company"),
            ("GLSummary", "Account"),
            ("GLSummary", "Period"),
            ("EMMain", "Employee"),
            ("EMMain", "FirstName"),
            ("EMMain", "LastName"),
            ("EMCompany", "Employee"),
            ("EMCompany", "Status"),
            ("tkDetail", "WBS1"),
            ("tkDetail", "RegHrs"),
            ("tkDetail", "LaborCode"),
            ("tkDetail", "Employee"),
        };

    private static readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<string>>>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the qualified names (e.g., "AR.InvBalanceSourceCurrency") of
    /// columns the financial loaders expect but the live catalog does not
    /// have. Empty list = clean. Memoized per catalog for the process
    /// lifetime; restart the app to force re-validation.
    /// </summary>
    public static Task<IReadOnlyList<string>> ValidateAsync(
        OdbcConnection cn,
        string catalog,
        CancellationToken ct)
    {
        var key = (catalog ?? string.Empty).Trim();
        return _cache.GetOrAdd(key, _ =>
            new Lazy<Task<IReadOnlyList<string>>>(() => RunValidationAsync(cn, key, ct))
        ).Value;
    }

    private static async Task<IReadOnlyList<string>> RunValidationAsync(
        OdbcConnection cn,
        string catalog,
        CancellationToken ct)
    {
        try
        {
            var resolved = DeltekCatalogValidator.ResolveCatalog(catalog);
            var expectedTables = ExpectedColumns
                .Select(c => c.Table)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Pull every column on every expected table once, then bucket in memory.
            // Cheaper than N round-trips and keeps the SQL trivial / injection-safe
            // (table names come from a hard-coded constant set, not user input).
            var inList = string.Join(",", expectedTables.Select(t => $"'{t.Replace("'", "''")}'"));
            cmd.CommandText = $@"
SELECT TABLE_NAME, COLUMN_NAME
FROM [{resolved}].INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ({inList});";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel; may race with disposal */ } });
            using var r = await Task.Run(() => cmd.ExecuteReader(), ct).ConfigureAwait(false);
            while (await Task.Run(() => r.Read(), ct).ConfigureAwait(false))
            {
                var t = (r.GetValue(0)?.ToString() ?? string.Empty).Trim();
                var col = (r.GetValue(1)?.ToString() ?? string.Empty).Trim();
                if (t.Length > 0 && col.Length > 0) found.Add($"{t}.{col}");
            }

            var missing = ExpectedColumns
                .Select(c => $"{c.Table}.{c.Column}")
                .Where(qn => !found.Contains(qn))
                .ToList();

            if (missing.Count > 0)
            {
                Log.ForContext(typeof(DeltekSchemaValidator)).Warning(
                    "Deltek schema drift detected on catalog {Catalog}: missing {MissingCount} columns: {Missing}",
                    catalog, missing.Count, string.Join(", ", missing));
            }

            return missing;
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(DeltekSchemaValidator)).Warning(ex,
                "Schema validation could not run on catalog {Catalog}; assuming clean. {ErrorType}: {ErrorMessage}",
                catalog, ex.GetType().Name, ex.Message);
            return Array.Empty<string>();
        }
    }
}
