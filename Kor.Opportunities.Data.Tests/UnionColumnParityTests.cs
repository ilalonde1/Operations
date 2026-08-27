using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// Guards the defect class behind "Load failed: SqlException: All queries
/// combined using a UNION, INTERSECT or EXCEPT operator must have an equal
/// number of expressions in their target lists" (SQL error 205).
///
/// SqlMajorProjectsInventoryStore.ListByCanonicalOrgAsync unions two CTEs:
/// ExistingPipeline (the inventory row, built from the shared AllColumns list)
/// and PortfolioWork (an IntelWork edge shaped to match, column by column).
/// The UNION ALL is positional, so the two SELECT lists must stay the same
/// length. On 2026-08-27 they did not: five lifecycle columns
/// (OwnerStaffId, OwnedAtUtc, DismissedAtUtc, DismissedBy, DismissedReason)
/// were added to AllColumns but not to PortfolioWork, and EVERY org dossier
/// failed to load. The compiler cannot see this — the SQL is a string — and no
/// unit test covered it, so it reached the user.
///
/// This scans the source rather than the database so it fails in a plain
/// `dotnet test`, with no connection string and no live server.
/// </summary>
public sealed class UnionColumnParityTests
{
    private static string StorePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Opportunities.Data")))
        {
            dir = dir.Parent!;
        }

        Assert.NotNull(dir);
        return Path.Combine(
            dir!.FullName, "Kor.Opportunities.Data", "MajorProjects", "SqlMajorProjectsInventoryStore.cs");
    }

    [Fact]
    public void ListByCanonicalOrg_union_branches_select_the_same_number_of_columns()
    {
        var path = StorePath();
        Assert.True(File.Exists(path), $"Expected the store at {path}");
        var source = File.ReadAllText(path);

        var allColumns = VerbatimStringAfter(source, "private const string AllColumns = @\"");
        var methodAt = source.IndexOf(
            "ListByCanonicalOrgAsync(long canonicalOrgId", StringComparison.Ordinal);
        Assert.True(methodAt > 0, "ListByCanonicalOrgAsync not found — did the method get renamed?");

        var template = VerbatimStringAfter(source, "var sql = $@\"", methodAt);
        var sql = StripLineComments(
            template.Replace("{AllColumns}", allColumns, StringComparison.Ordinal));

        var existing = SelectListOf(sql, "ExistingPipeline");
        var portfolio = SelectListOf(sql, "PortfolioWork");

        Assert.Equal(existing.Count, portfolio.Count);

        // Positional union: matching counts alone would still let a rename slip
        // a column into the wrong slot, so check the aliases line up too.
        for (var i = 0; i < existing.Count; i++)
        {
            Assert.Equal(existing[i], portfolio[i]);
        }
    }

    /// <summary>
    /// Drops SQL line comments. Prose in a `--` comment routinely contains
    /// commas, and the column scanner would otherwise count them as separators.
    /// Quote-aware so a literal containing "--" survives.
    /// </summary>
    private static string StripLineComments(string sql)
    {
        var sb = new System.Text.StringBuilder(sql.Length);
        var inString = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (ch == '\'')
            {
                inString = !inString;
            }

            if (!inString && ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                sb.Append('\n');
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>Contents of the C# verbatim string that begins at <paramref name="marker"/>.</summary>
    private static string VerbatimStringAfter(string source, string marker, int from = 0)
    {
        var i = source.IndexOf(marker, from, StringComparison.Ordinal);
        Assert.True(i >= 0, $"Marker not found: {marker}");
        i += marker.Length;

        var sb = new System.Text.StringBuilder();
        while (i < source.Length)
        {
            if (source[i] == '"')
            {
                if (i + 1 < source.Length && source[i + 1] == '"')
                {
                    sb.Append('"');   // "" is an escaped quote inside @"..."
                    i += 2;
                    continue;
                }

                break;
            }

            sb.Append(source[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Column names of the named CTE's SELECT list, in order. Scans with a paren
    /// depth counter rather than a regex — the list is full of nested CASE and
    /// CAST calls whose commas must not be counted.
    /// </summary>
    private static List<string> SelectListOf(string sql, string cteName)
    {
        var start = sql.IndexOf(cteName + " AS (", StringComparison.Ordinal);
        Assert.True(start >= 0, $"CTE not found: {cteName}");
        start = sql.IndexOf('(', start);

        var depth = 0;
        var end = start;
        while (end < sql.Length)
        {
            if (sql[end] == '(')
            {
                depth++;
            }
            else if (sql[end] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }

            end++;
        }

        var body = sql.Substring(start + 1, end - start - 1);

        var selectAt = body.IndexOf("SELECT", StringComparison.Ordinal) + "SELECT".Length;
        var fromAt = IndexOfAtDepthZero(body, "FROM", selectAt);
        Assert.True(fromAt > selectAt, $"No top-level FROM in CTE {cteName}");

        var list = body.Substring(selectAt, fromAt - selectAt);

        var columns = new List<string>();
        depth = 0;
        var current = new System.Text.StringBuilder();
        foreach (var ch in list)
        {
            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
            }

            if (ch == ',' && depth == 0)
            {
                columns.Add(ColumnNameOf(current.ToString()));
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.ToString().Trim().Length > 0)
        {
            columns.Add(ColumnNameOf(current.ToString()));
        }

        return columns;
    }

    private static int IndexOfAtDepthZero(string text, string token, int from)
    {
        var depth = 0;
        for (var i = from; i <= text.Length - token.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
            }
            else if (depth == 0
                     && string.CompareOrdinal(text, i, token, 0, token.Length) == 0
                     && (i == 0 || char.IsWhiteSpace(text[i - 1]))
                     && (i + token.Length >= text.Length || char.IsWhiteSpace(text[i + token.Length])))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The alias if the expression has one, otherwise the bare column name.</summary>
    private static string ColumnNameOf(string expression)
    {
        var text = expression.Trim();
        var asAt = text.LastIndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
        if (asAt >= 0)
        {
            text = text.Substring(asAt + 4);
        }

        text = text.Trim();
        var dot = text.LastIndexOf('.');
        if (dot >= 0)
        {
            text = text.Substring(dot + 1);
        }

        return text.Trim();
    }
}
