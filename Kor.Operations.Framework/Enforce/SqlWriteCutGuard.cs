using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Enforce;

/// <summary>
/// Board row 1212 (Framework plan step 3, Ian "Go" 19 Nov; the retired look guard's db-write kind, checked on the ACTION):
/// a SQL write whose output is piped into a cut is refused before it runs. Lesson #132591 / oversight mistake 53: a 0-row
/// board UPDATE was read through `| tail -1`, which hid the truncation error, and was taken for success.
/// A brain.Check cannot hold it: checks match each command segment alone, so "a write piped into a cut" spans two segments.
/// COVERS: in a Bash or PowerShell command, a segment that writes - sqlcmd / Invoke-Sqlcmd running a script file (-i,
/// -InputFile) or whose text holds INSERT INTO, UPDATE .. SET, DELETE FROM, MERGE INTO, ALTER/DROP/CREATE TABLE; or any
/// Invoke-KorSqlScript run (a migration) - followed through single pipes (2&gt;&amp;1 kept in the pipe) to a cut: head, tail
/// (not `tail -n +N`, which drops a header and shows every line after it), Select-Object -First/-Last, grep -m N.
/// DOES NOT: see a write made by a script the command runs (python x.py, a .ps1 calling sqlcmd), a write through oq.py or
/// standards_db, a write whose output is cut by a later command reading a saved file (> out.txt; tail out.txt), or judge a
/// read (a SELECT piped to head passes).
/// SAME-CLASS FAULT IT WOULD MISS: `python -I tools/oq.py -i -q "UPDATE ..." | tail -1` - the write is inside oq.py's text.
/// </summary>
public static partial class SqlWriteCutGuard
{
    public const string GuardName = "kor-sql-write-cut";
    public const string RuleName = "a SQL write's result is read whole (board row 1212, lesson #132591)";

    public sealed record Refusal(string Write, string Cut);

    // The PROGRAM of the segment, not a mention (replay 19 Nov: 7 of 26 real sqlcmd commands were refused on `grep ...
    // Invoke-KorSqlScript.ps1 | head`, `ls ...`, a kor record naming it - all wrong): sqlcmd / Invoke-Sqlcmd as the first
    // word (a path before it allowed), or a run of Invoke-KorSqlScript (pwsh/powershell [-flags] [-File], &, ., or bare).
    private static readonly Regex Write = new(
        @"\A\s*(?:(?:""[^""\n]*[\\/]|'[^'\n]*[\\/]|[^\s""';&|]*[\\/])?(?:sqlcmd|Invoke-Sqlcmd)(?:\.exe)?[""']?\s(?=[\s\S]*?(?:\s-(?:i|InputFile)\s|\b(?:INSERT\s+INTO|UPDATE\s+\S+\s+SET|DELETE\s+FROM|MERGE\s+INTO|ALTER\s+TABLE|DROP\s+TABLE|CREATE\s+TABLE)\b))" +
        @"|(?:(?:pwsh|powershell)(?:\.exe)?(?:\s+-(?!File\b)\w+(?:\s+(?!-)[^\s""']+)?)*\s+(?:-File\s+)?|&\s*|\.\s+)?[""']?(?:[^\s""';&|]*[\\/])?Invoke-KorSqlScript(?:\.ps1)?[""']?(?:\s|\z))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Cut = new(
        @"\A\s*(?:head\b|tail\b(?!\s+-n\s*\+)|(?:Select-Object|select)\b(?=[^\n]*\s-(?:First|Last)\b)|grep\b(?=[^\n]*\s-m\s*\d))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The refusal for this command, or null. Never throws: a command it cannot read passes.</summary>
    public static Refusal? Decide(string tool, string command)
    {
        if (tool is not ("Bash" or "PowerShell") || string.IsNullOrEmpty(command)) return null;
        try
        {
            var shell = tool == "PowerShell" ? Shell.PowerShell : Shell.Bash;
            var neutral = ShellText.Neutralise(command, shell).Neutral;
            var text = command.Replace('\r', ' ');
            var pieces = new List<(string Text, bool PipeAfter)>();
            var start = 0;
            for (var i = 0; i <= neutral.Length; i++)
            {
                if (i < neutral.Length && !ShellText.IsSeparator(neutral[i], shell)) continue;
                if (i < neutral.Length && neutral[i] == '&' && i > 0 && neutral[i - 1] == '>') continue;   // 2>&1 is a redirect
                var pipe = i < neutral.Length && neutral[i] == '|' && (i + 1 >= neutral.Length || neutral[i + 1] != '|')
                           && (i == 0 || neutral[i - 1] != '|');
                pieces.Add((text[start..Math.Min(i, text.Length)], pipe));
                start = i + 1;
            }
            for (var k = 0; k < pieces.Count; k++)
            {
                if (!Write.IsMatch(pieces[k].Text)) continue;
                for (var j = k; pieces[j].PipeAfter && j + 1 < pieces.Count; j++)
                    if (Cut.IsMatch(pieces[j + 1].Text)) return new Refusal(pieces[k].Text.Trim(), pieces[j + 1].Text.Trim());
            }
            return null;
        }
        catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException or IndexOutOfRangeException) { return null; }
    }

    public static string Says(Refusal r) =>
        $"A SQL write read through a cut (board row 1212, lesson #132591): `{Clip(r.Write)}` piped into `{Clip(r.Cut)}`. A cut hides " +
        "the error or the 0-row count that says the write failed (mistake 53). Run it with its output whole, or read back what it " +
        "wrote (@@ROWCOUNT, or a SELECT of the row) uncut. `tail -n +N` (drops a header only) passes.";

    private static string Clip(string s) => s.Length <= 120 ? s : s[..117] + "...";
}
