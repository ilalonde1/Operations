namespace Kor.Operations.Framework.Capture;

/// <summary>
/// Where a T-SQL file's text is DATA and where it is CODE: <see cref="Neutralise"/> returns a copy of the same length (line
/// breaks kept, so line numbers hold) in which comments and string literals are blanked, so a rule matched on the copy never
/// matches prose or a quoted string. The SQL sibling of <see cref="ShellText"/> (board row 1192's lint, 3 Nov). Searched
/// first (3 Nov, Grep "(?i)(sql.*(comment|literal|neutral))|((comment|literal|neutral).*sql)" over Operations *.cs): kor held
/// only shell neutralisers (ShellText, ToolClassifier); the one SQL stripper, Kor.Operations.Mcp's
/// QueryKorDataTool.StripQuotesAndComments, is outside kor and collapses each region to one space (line numbers lost).
/// Modelled: <c>-- </c> to the end of the line; <c>/* */</c> NESTED (T-SQL nests them); <c>'...'</c> and <c>N'...'</c> with
/// <c>''</c> as the escape; <c>[...]</c> identifiers with <c>]]</c> (kept as code, but a quote or comment marker inside one
/// is not read as one); <c>"..."</c> is kept as code (an identifier under QUOTED_IDENTIFIER ON).
/// Not modelled: <c>"..."</c> as a string under QUOTED_IDENTIFIER OFF; sqlcmd's <c>:r</c> includes. A region never closed
/// is blanked to the end and reported in <see cref="Result.Unproved"/>.
/// </summary>
public static class SqlText
{
    public sealed record Result(string Neutral, string? Unproved);

    public static Result Neutralise(string sql)
    {
        var c = sql.ToCharArray();
        string? unproved = null;
        var i = 0;
        void Blank(int from, int to) { for (var k = from; k < to && k < c.Length; k++) if (c[k] != '\n' && c[k] != '\r') c[k] = ' '; }
        while (i < c.Length)
        {
            var ch = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (ch == '-' && next == '-')
            {
                var end = sql.IndexOf('\n', i);
                if (end < 0) end = sql.Length;
                Blank(i, end);
                i = end;
            }
            else if (ch == '/' && next == '*')
            {
                var start = i;
                var depth = 0;
                while (i < sql.Length)
                {
                    if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { depth++; i += 2; continue; }
                    if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/') { depth--; i += 2; if (depth == 0) break; continue; }
                    i++;
                }
                if (depth > 0) unproved ??= $"a /* comment opened at line {Line(sql, start)} is never closed";
                Blank(start, i);
            }
            else if (ch == '\'')
            {
                var start = i++;
                var closed = false;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                    if (sql[i] == '\'') { i++; closed = true; break; }
                    i++;
                }
                if (!closed) unproved ??= $"a string opened at line {Line(sql, start)} is never closed";
                Blank(start + 1, closed ? i - 1 : i);   // the quotes stay, so N'' reads as a literal in the copy
            }
            else if (ch == '[')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == ']' && i + 1 < sql.Length && sql[i + 1] == ']') { i += 2; continue; }
                    if (sql[i] == ']') { i++; break; }
                    i++;
                }
            }
            else i++;
        }
        return new Result(new string(c), unproved);
    }

    /// <summary>1-based line of <paramref name="index"/>.</summary>
    public static int Line(string text, int index)
    {
        var n = 1;
        for (var k = 0; k < index && k < text.Length; k++) if (text[k] == '\n') n++;
        return n;
    }
}
