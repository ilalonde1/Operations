using System.Text;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Enforce;

public static partial class SqlWriteCutGuard
{
    public sealed record BudgetRefusal(string Sql, int? Row, string Reason);
    private const RegexOptions BudgetRegex = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Identifier = @"\w+(?:\s*\.\s*\w+)*";

    /// <summary>
    /// The budget extension to this SQL action guard. Reads literal sqlcmd/Invoke-Sqlcmd queries and input files,
    /// Invoke-KorSqlScript -Path, and oq.py -q/-i; shell wrappers use the existing CommandSegments walker.
    /// UPDATE/MERGE setting oversight.Commitment.Budget needs a row-specific typed approval. INSERT stays allowed.
    /// Unknown row sets fail closed: narrow to a literal target Id before seeking approval. SQL strings/comments do
    /// not count as writes. Does not interpret arbitrary application code, stored procedures, dynamic SQL or SQLCMD
    /// includes/variables. A Python SqlClient write or a stored procedure raising Budget still escapes this hook.
    /// </summary>
    public static BudgetRefusal? DecideBudget(string tool, string command, string? cwd, Func<int, bool> approved)
    {
        if (tool is not ("Bash" or "PowerShell") || string.IsNullOrWhiteSpace(command)) return null;
        try
        {
            var dir = cwd;
            foreach (var seg in CommandSegments.Of(command, tool == "PowerShell" ? Shell.PowerShell : Shell.Bash, cwd))
            {
                var (words, _) = DrafterPythonGuard.Parse(seg.Text, seg.Shell);
                if (words.Count == 0) continue;
                if (words[0] == "&" && words.Count > 1) words.RemoveAt(0);
                var program = Path.GetFileNameWithoutExtension(words[0].Replace('\\', '/')).ToLowerInvariant();
                if (program is "cd" or "set-location" or "sl" or "pushd" && words.Count > 1)
                {
                    dir = CommandSegments.ResolvePath(words[^1], dir ?? Environment.CurrentDirectory);
                    continue;
                }
                var sqlClient = program is "sqlcmd" or "invoke-sqlcmd";
                var oqStart = program is "python" or "python3" or "py" ? OqArgumentsStart(words) : -1;
                var oq = oqStart >= 0;
                var script = program == "invoke-korsqlscript" || program is "powershell" or "pwsh" && words.Skip(1).Any(w => Path.GetFileNameWithoutExtension(w.Replace('\\', '/')).Equals("Invoke-KorSqlScript", StringComparison.OrdinalIgnoreCase));
                if (!sqlClient && !oq && !script) continue;
                for (var i = oq ? oqStart : 1; i < words.Count; i++)
                {
                    // sqlcmd (also oq.py's forwarded arguments) is case-sensitive: -I is a switch, -i is a file.
                    // PowerShell parameter names remain case-insensitive.
                    var sqlcmdFlags = program == "sqlcmd" || oq;
                    var flag = sqlcmdFlags ? words[i] : words[i].ToLowerInvariant();
                    var file = sqlcmdFlags ? flag == "-i" : flag is "-i" or "-inputfile" || script && flag == "-path";
                    var query = sqlcmdFlags ? flag is "-q" or "-Q" : flag is "-q" or "-query";
                    if (!file && !query) continue;
                    if (++i >= words.Count) return new(seg.Text, null, "SQL input is missing; cannot check Budget writes");
                    string sql;
                    if (file)
                    {
                        var path = CommandSegments.ResolvePath(words[i], dir ?? Environment.CurrentDirectory);
                        if (path is null || !File.Exists(path) || new FileInfo(path).Length > CommandSegments.MaxScriptBytes)
                            return new(seg.Text, null, "SQL input file cannot be inspected; cannot check Budget writes");
                        sql = File.ReadAllText(path);
                    }
                    else
                    {
                        sql = words[i];
                        if (sql.StartsWith('$') || sql.StartsWith('@')) return new(seg.Text, null, "SQL input is not literal; cannot check Budget writes");
                    }
                    if (BudgetSql(sql, approved) is { } refusal) return refusal;
                }
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or RegexMatchTimeoutException or NotSupportedException)
        { return new("SQL input", null, "SQL inspection failed; cannot check Budget writes"); }
    }

    private static int OqArgumentsStart(IReadOnlyList<string> words)
    {
        var i = 1;
        for (; i < words.Count; i++)
        {
            var arg = words[i];
            if (arg == "--") { i++; break; }
            // -c and -m execute code/modules, not a script named by a later argument.
            if (arg is "-" or "-c" or "-m" || arg.StartsWith("-c", StringComparison.Ordinal) || arg.StartsWith("-m", StringComparison.Ordinal)) return -1;
            if (arg is "-X" or "-W" or "--check-hash-based-pycs") { i++; continue; }
            if (arg.StartsWith('-')) continue; // -I, -u, -Xutf8, py -3, etc.
            break;
        }
        return i < words.Count && Path.GetFileName(words[i].Replace('\\', '/')).Equals("oq.py", StringComparison.OrdinalIgnoreCase)
            ? i + 1 : -1;
    }

    private static BudgetRefusal? BudgetSql(string sql, Func<int, bool> approved)
    {
        var neutral = SqlIdentifiers(sql);
        foreach (var statement in neutral.Split(';'))
        {
            var merge = Regex.Match(statement, @"\bMERGE\s+(?:INTO\s+)?(?<target>" + Identifier + @")\b", BudgetRegex, TimeSpan.FromSeconds(1));
            var updates = Regex.Matches(statement, @"\bUPDATE\s+(?:TOP\s*\([^)]*\)\s*(?:PERCENT\s+)?)?(?<target>" + Identifier + @")(?:\s+WITH\s*\([^)]*\))?\s+SET\b", BudgetRegex, TimeSpan.FromSeconds(1));
            var update = updates.Count == 0 ? Match.Empty : updates[0];
            var target = merge.Success ? merge.Groups["target"].Value : update.Groups["target"].Value;
            if (target.Length == 0) continue;
            var table = Regex.Replace(target, @"\s+", "");
            var board = table.Equals("oversight.Commitment", StringComparison.OrdinalIgnoreCase) || table.EndsWith(".oversight.Commitment", StringComparison.OrdinalIgnoreCase);
            if (!board && update.Success)
            {
                // UPDATE c SET ... FROM [db.]oversight.Commitment [AS] c
                board = Regex.IsMatch(statement, @"\b(?:FROM|JOIN)\s+(?:\w+\s*\.\s*)?oversight\s*\.\s*Commitment\s+(?:AS\s+)?" + Regex.Escape(table) + @"\b", BudgetRegex, TimeSpan.FromSeconds(1));
            }
            // An updatable CTE or a more complex alias: recognise the protected table but never guess its row scope.
            var uncertainTarget = !board && update.Success && Regex.IsMatch(statement, @"\boversight\s*\.\s*Commitment\b", BudgetRegex, TimeSpan.FromSeconds(1));
            board |= uncertainTarget;
            if (!board) continue;
            var sets = Regex.Matches(statement, @"\bSET\s+", BudgetRegex, TimeSpan.FromSeconds(1));
            if (!sets.Any(m => Regex.IsMatch(Assignments(statement[(m.Index + m.Length)..]), @"(?:^|,|=)\s*(?:\w+\s*\.\s*)?Budget\s*(?:=|\+=)", BudgetRegex, TimeSpan.FromSeconds(1)))) continue;
            int? row = null;
            if (update.Success && !merge.Success && !uncertainTarget && updates.Count == 1)
            {
                var where = Regex.Match(statement, @"\bWHERE\s+(?:(?<qualifier>\w+)\s*\.\s*)?Id\s*=\s*(?<row>\d+)\s*$", BudgetRegex, TimeSpan.FromSeconds(1));
                var qualified = where.Groups["qualifier"].Success;
                var targetName = table.Split('.')[^1];
                var qualifierIsTarget = qualified ? where.Groups["qualifier"].Value.Equals(targetName, StringComparison.OrdinalIgnoreCase)
                    : !Regex.IsMatch(statement, @"\bFROM\b", BudgetRegex, TimeSpan.FromSeconds(1));
                if (where.Success && qualifierIsTarget && int.TryParse(where.Groups["row"].Value, out var id) && id > 0) row = id;
            }
            else if (merge.Success && !Regex.IsMatch(statement, @"\bNOT\s+MATCHED\s+BY\s+SOURCE\b", BudgetRegex, TimeSpan.FromSeconds(1)))
            {
                var alias = Regex.Match(statement[(merge.Index + merge.Length)..], @"^\s+(?:AS\s+)?(?<alias>\w+)\s+USING\b", BudgetRegex, TimeSpan.FromSeconds(1));
                var targetName = alias.Success ? alias.Groups["alias"].Value : table.Split('.')[^1];
                var on = Regex.Match(statement, @"\bON\s+(?<qualifier>\w+)\s*\.\s*Id\s*=\s*(?<row>\d+)\s+WHEN\b", BudgetRegex, TimeSpan.FromSeconds(1));
                if (on.Success && on.Groups["qualifier"].Value.Equals(targetName, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(on.Groups["row"].Value, out var id) && id > 0) row = id;
            }
            if (row is null || !approved(row.Value))
                return new(sql, row, row is null ? "the affected row set is not a single literal target Id; narrow the write first" : "Ian has not typed approval for this row's Budget change");
        }
        return null;
    }

    private static string Assignments(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            else if (depth == 0 && (i == 0 || !char.IsLetterOrDigit(text[i - 1]))
                     && Regex.IsMatch(text[i..], @"\A(?:FROM|WHERE|WHEN|OUTPUT|OPTION)\b", BudgetRegex, TimeSpan.FromSeconds(1)))
                return text[..i];
        }
        return text;
    }

    // Preserve identifiers and punctuation; remove string values and comments before looking for assignments/row IDs.
    private static string SqlIdentifiers(string sql)
    {
        var result = new StringBuilder();
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            { while (i < sql.Length && sql[i] != '\n') i++; result.Append(' '); continue; }
            if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var depth = 1; i += 2;
                while (i < sql.Length && depth > 0)
                {
                    if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*') { depth++; i += 2; }
                    else if (i + 1 < sql.Length && sql[i] == '*' && sql[i + 1] == '/') { depth--; i += 2; }
                    else i++;
                }
                i--; result.Append(' '); continue;
            }
            if (sql[i] == '\'')
            {
                result.Append(" __literal__ ");
                for (i++; i < sql.Length; i++)
                    if (sql[i] == '\'') { if (i + 1 < sql.Length && sql[i + 1] == '\'') i++; else break; }
                continue;
            }
            if (sql[i] is '[' or '"')
            {
                var end = sql[i] == '[' ? ']' : '"';
                for (i++; i < sql.Length && sql[i] != end; i++) result.Append(sql[i]);
                continue;
            }
            result.Append(sql[i]);
        }
        return result.ToString();
    }

    public static string SaysBudget(BudgetRefusal r) =>
        $"Budget change refused on oversight.Commitment{(r.Row is { } row ? $" row {row}" : "")}: {r.Reason}. " +
        $"Ian must type IAN-APPROVED-BUDGET {(r.Row is { } id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "<row>")} as its own line in this session (valid for 12 hours). New-row INSERT budgets are allowed.";
}
