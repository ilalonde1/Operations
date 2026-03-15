#nullable enable
using System;
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.Financials
{
    internal static class DeltekSchemaDumper
    {
        public sealed record DumpResult(string OutputDirectory, int TableCount, int ColumnCount);

        public static Task<DumpResult> DumpAsync(CancellationToken cancelToken)
        {
            return Task.Run(() =>
            {
                cancelToken.ThrowIfCancellationRequested();

                var outDir = Path.Combine(
                    Path.GetTempPath(),
                    "Kor.Transmittals.DeltekSchemaDump",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

                Directory.CreateDirectory(outDir);

                var tablesCsv = Path.Combine(outDir, "tables.csv");
                var colsCsv = Path.Combine(outDir, "columns.csv");

                using var cn = CreateDeltekConnection();
                cn.Open();

                var tables = cn.GetSchema("Tables");
                var tableRows = tables.Rows.Cast<DataRow>()
                    .Where(r =>
                    {
                        var t = r["TABLE_TYPE"]?.ToString() ?? "";
                        return string.Equals(t, "TABLE", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(t, "VIEW", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                using (var w = new StreamWriter(tablesCsv, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    w.WriteLine("TABLE_CAT,TABLE_SCHEM,TABLE_NAME,TABLE_TYPE");
                    foreach (var r in tableRows)
                    {
                        cancelToken.ThrowIfCancellationRequested();
                        w.WriteLine(string.Join(",",
                            Csv(r["TABLE_CAT"]),
                            Csv(r["TABLE_SCHEM"]),
                            Csv(r["TABLE_NAME"]),
                            Csv(r["TABLE_TYPE"])));
                    }
                }

                var columnCount = 0;
                using (var w = new StreamWriter(colsCsv, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    w.WriteLine("TABLE_CAT,TABLE_SCHEM,TABLE_NAME,COLUMN_NAME,ORDINAL_POSITION,DATA_TYPE,TYPE_NAME,COLUMN_SIZE,DECIMAL_DIGITS,NULLABLE");

                    foreach (var t in tableRows)
                    {
                        cancelToken.ThrowIfCancellationRequested();

                        var tableCat = t["TABLE_CAT"]?.ToString();
                        var tableSchem = t["TABLE_SCHEM"]?.ToString();
                        var tableName = t["TABLE_NAME"]?.ToString();
                        if (string.IsNullOrWhiteSpace(tableName))
                            continue;

                        // Restrictions: TABLE_CAT, TABLE_SCHEM, TABLE_NAME, COLUMN_NAME
                        var cols = cn.GetSchema("Columns", new[] { tableCat, tableSchem, tableName, null });
                        foreach (DataRow r in cols.Rows)
                        {
                            cancelToken.ThrowIfCancellationRequested();
                            columnCount++;

                            w.WriteLine(string.Join(",",
                                Csv(r["TABLE_CAT"]),
                                Csv(r["TABLE_SCHEM"]),
                                Csv(r["TABLE_NAME"]),
                                Csv(r["COLUMN_NAME"]),
                                Csv(r["ORDINAL_POSITION"]),
                                Csv(r["DATA_TYPE"]),
                                Csv(r["TYPE_NAME"]),
                                Csv(r["COLUMN_SIZE"]),
                                Csv(r["DECIMAL_DIGITS"]),
                                Csv(r["NULLABLE"])));
                        }
                    }
                }

                // Also dump the raw connection details (without secrets) to help correlate.
                File.WriteAllText(
                    Path.Combine(outDir, "connection_info.txt"),
                    BuildConnectionInfo(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                return new DumpResult(outDir, tableRows.Count, columnCount);
            }, cancelToken);
        }

        private static OdbcConnection CreateDeltekConnection()
        {
            var options = ((global::Kor.Operations.OperationsApp)System.Windows.Application.Current).Services.GetRequiredService<DeltekOdbcOptions>();
            // Prefer the explicit DSN builder setting if present (it may include driver params).
            var raw = options.OdbcDsn;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var b = new OdbcConnectionStringBuilder(raw);
                return new OdbcConnection(b.ConnectionString);
            }

            // Fallback to the same convention used by FinancialsService.
            var dsn = string.IsNullOrWhiteSpace(options.Dsn) ? "Deltek" : options.Dsn;
            var user = options.User ?? string.Empty;
            var pwd = options.Password ?? string.Empty;

            var csb = new OdbcConnectionStringBuilder();
            csb["DSN"] = dsn;
            if (!string.IsNullOrWhiteSpace(user))
                csb["UID"] = user;
            if (!string.IsNullOrWhiteSpace(pwd))
                csb["PWD"] = pwd;

            return new OdbcConnection(csb.ConnectionString);
        }

        private static string BuildConnectionInfo()
        {
            var options = ((global::Kor.Operations.OperationsApp)System.Windows.Application.Current).Services.GetRequiredService<DeltekOdbcOptions>();
            var sb = new StringBuilder();

            sb.AppendLine("Generated: " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            sb.AppendLine("Vp.Dsn: " + (string.IsNullOrWhiteSpace(options.Dsn) ? "(null)" : options.Dsn));
            sb.AppendLine("Vp.User set: " + (!string.IsNullOrWhiteSpace(options.User)).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Vp.Password set: " + (!string.IsNullOrWhiteSpace(options.Password)).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("DeltekOdbcDsn set: " + (!string.IsNullOrWhiteSpace(options.OdbcDsn)).ToString(CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        private static string Csv(object? v)
        {
            if (v == null || v is DBNull)
                return "";

            var s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}


