#nullable enable
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// The single read-only SQL execute tool the AI uses to query KOR's data.
///
/// Connects to the local SQL Server instance whose connection string lives
/// in McpOptions.SqlConnectionString. That instance hosts the writable
/// KorMcp / KorTransmittals / etc. databases AND the read-only DELTEK_VP
/// linked server, so one tool covers both the AI's working memory and the
/// Deltek source-of-truth.
///
/// Safety rails (defence in depth, since the AI generates SQL):
///   • SELECT-only check — the SQL must start with SELECT or WITH; rejects
///     INSERT/UPDATE/DELETE/EXEC/DDL/etc.
///   • CommandTimeout = McpOptions.SqlQueryTimeoutSeconds. Caps DB-side cost.
///   • Row cap = McpOptions.SqlQueryRowCap. Tool result truncates after this
///     many rows so a "SELECT * FROM PR" can't blow the LLM context budget.
///   • Audit log records the SQL the AI wrote, the row count, the duration,
///     and any error (regardless of success/failure). One row in Mcp.AuditLog
///     per call, fire-and-forget.
///
/// The tool returns JSON to the LLM: {"columns":[...],"rows":[[...],...],
/// "rowCount":N,"truncated":true|false,"durationMs":N}.
/// </summary>
[McpServerToolType]
public sealed class QueryKorDataTool
{
    private readonly IOptions<McpOptions> _options;
    private readonly AuditLogger _audit;
    private readonly ILogger<QueryKorDataTool> _logger;

    public QueryKorDataTool(
        IOptions<McpOptions> options,
        AuditLogger audit,
        ILogger<QueryKorDataTool> logger)
    {
        _options = options;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "query_kor_data")]
    [Description(
        "Run a read-only SELECT query against KOR's SQL Server. " +
        "The connection reaches both KOR's local databases AND Deltek Vantagepoint via the DELTEK_VP linked server. " +
        "For Deltek tables, use 4-part naming: [DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.<TableName>. " +
        "For local tables, use regular 3-part naming. " +
        "Only SELECT and WITH (CTE) statements are allowed; any DML/DDL/EXEC will be rejected. " +
        "Results are capped at " + nameof(McpOptions.SqlQueryRowCap) + " rows — if you need more, refine the WHERE clause. " +
        "Returns JSON with columns, rows, rowCount, truncated flag, and durationMs. " +
        "Read-only enforcement is multi-layered (prefix check + batch-statement detection + write-keyword scan over stripped SQL). " +
        "SELECT ... INTO, WITH ... DELETE/UPDATE/INSERT/MERGE, EXEC, GRANT, BULK, DBCC, OPENQUERY/OPENROWSET/OPENDATASOURCE, etc. are all rejected before reaching SQL Server.")]
    public async Task<string> QueryKorDataAsync(
        [Description("The T-SQL SELECT statement to execute. Must begin with SELECT or WITH.")] string sql,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var opts = _options.Value;
        string? errorMessage = null;
        int rowCount = 0;

        try
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                errorMessage = "SQL is required.";
                return ToolErrorEnvelope.Validation("query_kor_data", errorMessage, (int)sw.ElapsedMilliseconds);
            }

            // SELECT-only gate. Strips line + block comments and leading whitespace
            // before checking the first keyword so prefix-comment payloads like
            // "/* x */ DELETE ..." can't slip through. Also rejects sequencing
            // attacks (";" followed by another statement) since SqlCommand is
            // happy to execute multi-statement batches.
            var trimmed = StripLeadingNoise(sql);
            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Only SELECT and WITH statements are permitted.";
                return ToolErrorEnvelope.Validation("query_kor_data", errorMessage, (int)sw.ElapsedMilliseconds);
            }

            if (ContainsBatchedStatement(trimmed))
            {
                errorMessage = "Multi-statement SQL is not permitted; submit one SELECT/WITH at a time.";
                return ToolErrorEnvelope.Validation("query_kor_data", errorMessage, (int)sw.ElapsedMilliseconds);
            }

            var passThrough = FindForbiddenPassThroughToken(trimmed);
            if (passThrough != null)
            {
                errorMessage = $"{passThrough} pass-through is not permitted in this read-only gateway. Use direct SELECT/WITH queries only.";
                return ToolErrorEnvelope.Validation("query_kor_data", errorMessage, (int)sw.ElapsedMilliseconds);
            }

            var forbidden = FindForbiddenWriteToken(trimmed);
            if (forbidden != null)
            {
                errorMessage = $"SQL contains a write/admin keyword '{forbidden}'. Only read-only SELECT queries are permitted.";
                return ToolErrorEnvelope.Validation("query_kor_data", errorMessage, (int)sw.ElapsedMilliseconds);
            }

            if (!opts.IsConfigured)
            {
                errorMessage = "MCP service not configured (missing SqlConnectionString).";
                return ToolErrorEnvelope.Validation("query_kor_data", errorMessage, (int)sw.ElapsedMilliseconds);
            }

            await using var cn = new SqlConnection(opts.SqlConnectionString);
            await cn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = opts.SqlQueryTimeoutSeconds;
            cmd.CommandType = CommandType.Text;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var columns = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns[i] = reader.GetName(i);
            }

            var rows = new List<object?[]>();
            var truncated = false;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (rows.Count >= opts.SqlQueryRowCap)
                {
                    truncated = true;
                    break;
                }

                var row = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : NormalizeValue(reader.GetValue(i));
                }
                rows.Add(row);
            }
            rowCount = rows.Count;
            sw.Stop();

            var result = new
            {
                columns,
                rows,
                rowCount,
                truncated,
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            return JsonSerializer.Serialize(result);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("query_kor_data", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "query_kor_data failed.");
            return ToolErrorEnvelope.FromException("query_kor_data", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            // Fire-and-forget audit row. The request itself never waits on SQL.
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "query_kor_data",
                InputJson: TruncateForAudit(sql),
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string StripLeadingNoise(string sql)
    {
        var s = sql.AsSpan().TrimStart();
        // Strip leading line ("-- ...") AND block ("/* ... */") comments so the
        // first-keyword check sees real SQL. Loop because callers may chain
        // them (e.g. "/* a */ -- b\n SELECT ...").
        while (true)
        {
            if (s.StartsWith("--"))
            {
                var newline = s.IndexOfAny('\n', '\r');
                s = newline < 0 ? ReadOnlySpan<char>.Empty : s[(newline + 1)..].TrimStart();
                continue;
            }

            if (s.StartsWith("/*"))
            {
                var close = s.IndexOf("*/");
                // Unterminated block comment — treat the rest of the input as
                // commented out so the SELECT/WITH check fails cleanly.
                s = close < 0 ? ReadOnlySpan<char>.Empty : s[(close + 2)..].TrimStart();
                continue;
            }

            break;
        }
        return s.ToString();
    }

    /// <summary>
    /// Returns true if the SQL appears to contain more than one statement.
    /// Walks the text skipping over string literals + comments and reports any
    /// `;` that's followed by non-whitespace, non-comment content. Trailing `;`
    /// at end of input is allowed because that's a stylistic terminator, not
    /// a sequencing attack.
    /// </summary>
    private static bool ContainsBatchedStatement(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return false;
        }

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            // Single-quoted string literal — skip past, doubling '' is the SQL
            // escape so a doubled quote inside doesn't terminate.
            if (c == '\'')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '\'')
                    {
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Double-quoted identifier/string — skip past, doubling "" is the
            // SQL escape so a doubled quote inside doesn't terminate.
            if (c == '"')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '"' && i + 1 < sql.Length && sql[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '"')
                    {
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Bracketed identifier [foo] — skip to closing bracket.
            if (c == '[')
            {
                while (i < sql.Length && sql[i] != ']')
                {
                    i++;
                }
                continue;
            }

            // Line comment — skip to end-of-line.
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n' && sql[i] != '\r')
                {
                    i++;
                }
                continue;
            }

            // Block comment — skip to */.
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }
                i++;
                continue;
            }

            if (c == ';')
            {
                // Allow trailing whitespace/comments after the terminator —
                // anything else is a second statement we won't run.
                for (var j = i + 1; j < sql.Length; j++)
                {
                    var t = sql[j];
                    if (char.IsWhiteSpace(t))
                    {
                        continue;
                    }
                    if (t == '-' && j + 1 < sql.Length && sql[j + 1] == '-')
                    {
                        while (j < sql.Length && sql[j] != '\n' && sql[j] != '\r')
                        {
                            j++;
                        }
                        continue;
                    }
                    if (t == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        j += 2;
                        while (j + 1 < sql.Length && !(sql[j] == '*' && sql[j + 1] == '/'))
                        {
                            j++;
                        }
                        j++;
                        continue;
                    }

                    return true;
                }

                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// After the SELECT/WITH prefix and batch-statement checks pass, scan
    /// the statement body for mutation/admin keywords that can ride INSIDE
    /// a single statement (the audit Finding 1 risk class):
    ///   - WITH cte AS (...) DELETE/UPDATE/INSERT/MERGE ...  CTE consumer is a write
    ///   - SELECT ... INTO ...  server-side table copy
    ///   - EXEC sp_...  /  EXECUTE ...  invokes anything
    ///   - GRANT / REVOKE / DENY / DBCC / SHUTDOWN / BACKUP / RESTORE / BULK INSERT
    /// Strips string literals (single-quoted), double-quoted regions,
    /// bracketed identifiers ([...]), line comments (--...), and block
    /// comments (/* ... */) BEFORE scanning,
    /// so `WHERE Description = 'DROP'` does not false-positive.
    /// </summary>
    private static string? FindForbiddenWriteToken(string sql)
    {
        var stripped = StripQuotesAndComments(sql);
        var pattern = @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|INTO|GRANT|REVOKE|DENY|DBCC|SHUTDOWN|BACKUP|RESTORE|BULK|SP_)\b";
        var match = System.Text.RegularExpressions.Regex.Match(
            stripped,
            pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Pass-through rowset providers can hide a second SQL dialect inside a
    /// string literal (for example OPENQUERY(..., 'DELETE ...')). Ban them
    /// before quote/comment stripping so the inner text cannot disappear from
    /// the read-only gate. Token boundaries prevent aliases such as
    /// OPENROWSETXYZ from matching.
    /// </summary>
    private static string? FindForbiddenPassThroughToken(string sql)
    {
        var pattern = @"\b(OPENQUERY|OPENROWSET|OPENDATASOURCE)\b";
        var match = System.Text.RegularExpressions.Regex.Match(
            sql,
            pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Whole-text strip of string literals, double-quoted regions, bracketed
    /// identifiers, and line + block comments. Used by the second-stage
    /// write-keyword scan. Different from StripLeadingNoise which only walks
    /// the prefix.
    /// </summary>
    private static string StripQuotesAndComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (c == '\'')
            {
                // Single-quoted literal - skip past, doubled '' is the SQL escape.
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                    if (sql[i] == '\'') { i++; break; }
                    i++;
                }
                sb.Append(' ');
                continue;
            }
            if (c == '"')
            {
                // Double-quoted identifier/string - skip past, doubled "" is the SQL escape.
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '"' && i + 1 < sql.Length && sql[i + 1] == '"') { i += 2; continue; }
                    if (sql[i] == '"') { i++; break; }
                    i++;
                }
                sb.Append(' ');
                continue;
            }
            if (c == '[')
            {
                // Bracketed identifier - skip to closing ].
                while (i < sql.Length && sql[i] != ']') i++;
                if (i < sql.Length) i++;
                sb.Append(' ');
                continue;
            }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                // Line comment - skip to EOL.
                while (i < sql.Length && sql[i] != '\n' && sql[i] != '\r') i++;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                // Block comment - skip to */.
                i += 2;
                while (i < sql.Length - 1 && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                if (i < sql.Length - 1) i += 2;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static object? NormalizeValue(object? value)
    {
        // Make types JSON-friendly: dates as ISO strings; decimals as decimal (System.Text.Json
        // serializes decimals exactly); byte arrays elided.
        return value switch
        {
            null => null,
            DateTime dt => dt.ToString("o"),
            DateTimeOffset dto => dto.ToString("o"),
            byte[] => "[binary]",
            _ => value,
        };
    }

    private static string? TruncateForAudit(string? sql)
    {
        if (sql == null) return null;
        const int cap = 4000;
        return sql.Length <= cap ? sql : sql[..cap] + " /* ... truncated for audit ... */";
    }
}
