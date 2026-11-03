using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Records;

/// <summary>
/// Board row 1192's third clause (3 Nov): a migration numbered above <see cref="Cutoff"/> (177, the one that added
/// migration.usp_MigrationProbe - KOR.Drafter db/177_AProbeNeverDoomsItsRun.sql's header, "THE LINT FOR LATER") is refused
/// when:
/// <list type="bullet">
/// <item>L1 - its header (the comments before its first statement) has no line starting <c>PROBES:</c>
/// ("through migration.usp_MigrationProbe" or "none");</item>
/// <item>L2 - its CODE hand-writes probe handling: BEGIN CATCH together with EXECUTE AS / EXEC AS or sp_executesql, or any
/// XACT_STATE() read, while the code never names migration.usp_MigrationProbe.</item>
/// </list>
/// L2 reads the <see cref="SqlText.Neutralise"/> copy, so comments and string literals never match - neither the hits
/// nor the name that excuses them. A file whose number cannot be read from its name (NNN_...) is not a numbered
/// migration and is not linted; a neutraliser that could not prove the copy (a region never closed) is a finding too.
/// Not covered: probe handling inside a string run by sp_executesql (blanked as data); a hand-written loop in a file that
/// also calls the procedure once (177's own stated miss); a PROBES: line that lies.
/// </summary>
public static class MigrationLint
{
    public const int Cutoff = 177;

    public const string Proc = "migration.usp_MigrationProbe";

    private static readonly Regex Number = new(@"^(\d+)_", RegexOptions.CultureInvariant);
    private static readonly Regex Catch = new(@"\bBEGIN\s+CATCH\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExecAs = new(@"\bEXEC(?:UTE)?\s+AS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SpExec = new(@"\bsp_executesql\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Xact = new(@"\bXACT_STATE\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ProcName = new(@"\[?\bmigration\]?\s*\.\s*\[?usp_MigrationProbe\b\]?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The number in a migration's file name (NNN_...), or null.</summary>
    public static int? NumberOf(string fileName)
    {
        var m = Number.Match(Path.GetFileName(fileName));
        return m.Success && int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>One line per finding ("&lt;file&gt;: L1 ..."); empty = passes (or not a migration above the cut-off).</summary>
    public static IReadOnlyList<string> Lint(string fileName, string sql, int cutoff = Cutoff)
    {
        var name = Path.GetFileName(fileName);
        var n = NumberOf(name);
        if (n is null || n <= cutoff) return Array.Empty<string>();
        var findings = new List<string>();
        var (neutral, unproved) = SqlText.Neutralise(sql);
        if (unproved is not null) findings.Add($"{name}: L0 the SQL could not be read as code and comments ({unproved}) - fix it so L1/L2 can be checked");
        // L1: the header = the text before the first character of code
        var first = neutral.TakeWhile(char.IsWhiteSpace).Count();
        var header = sql[..first];
        // a header line is read past its comment markers: "    PROBES:" in a /* */ block, "-- PROBES:" as a line comment
        if (!header.Split('\n').Any(l => l.TrimStart(' ', '\t', '-', '/', '*').StartsWith("PROBES:", StringComparison.Ordinal)))
            findings.Add($"{name}: L1 its header has no line starting 'PROBES:' (\"PROBES: through {Proc}\" or \"PROBES: none\")");
        // L2: hand-written probe handling in the code, the procedure never named in the code
        if (!ProcName.IsMatch(neutral))
        {
            var catches = Lines(Catch, neutral, sql);
            var wraps = Lines(ExecAs, neutral, sql).Concat(Lines(SpExec, neutral, sql)).Distinct().OrderBy(x => x).ToList();
            var xact = Lines(Xact, neutral, sql);
            var why = new List<string>();
            if (catches.Count > 0 && wraps.Count > 0)
                why.Add($"BEGIN CATCH (line {Join(catches)}) with EXECUTE AS / sp_executesql (line {Join(wraps)})");
            if (xact.Count > 0) why.Add($"XACT_STATE() (line {Join(xact)})");
            if (why.Count > 0)
                findings.Add($"{name}: L2 hand-written probe handling - {string.Join("; ", why)} - and the code never names {Proc}: run each probe through it");
        }
        return findings;
    }

    private static List<int> Lines(Regex r, string neutral, string sql) =>
        r.Matches(neutral).Select(m => SqlText.Line(sql, m.Index)).Distinct().ToList();

    private static string Join(List<int> lines) =>
        lines.Count <= 6 ? string.Join(", ", lines) : string.Join(", ", lines.Take(6)) + $" and {lines.Count - 6} more";
}
