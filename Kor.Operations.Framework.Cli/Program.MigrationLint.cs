using System.Text;
using Kor.Operations.Framework.Records;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// <c>kor migration-lint &lt;file.sql&gt; [--cutoff N]</c> | <c>kor migration-lint - --name NNN_x.sql [--cutoff N]</c> (the SQL on
/// stdin, named - Framework tools/kor_review.py sends each db/*.sql AS COMMITTED this way). Board row 1192: one line per
/// finding and exit 1, or nothing and exit 0 (<see cref="MigrationLint"/> holds the rules; no copy of them anywhere else).
/// <c>--cutoff 0</c> lints every numbered file, the way the tests prove the rules are not blind on 171-176.
/// </summary>
public static partial class Program
{
    private static int MigrationLintVerb(Options o, Func<string?> stdin, TextWriter output)
    {
        var path = o.Positional.ElementAtOrDefault(1) ?? throw new ArgumentException("migration-lint needs a .sql file (or - with --name)");
        var cutoff = o.Get("--cutoff") is null ? MigrationLint.Cutoff
            : o.Int("--cutoff") ?? throw new ArgumentException($"--cutoff takes a whole number, not '{o.Get("--cutoff")}'");
        string name, sql;
        if (path == "-")
        {
            name = o.Get("--name") ?? throw new ArgumentException("migration-lint - (stdin) needs --name <file name>");
            sql = stdin() ?? throw new ArgumentException("migration-lint -: nothing on stdin");
        }
        else
        {
            if (!File.Exists(path)) throw new ArgumentException($"migration-lint: no file {path}");
            name = path;
            sql = File.ReadAllText(path, Encoding.UTF8);
        }
        var findings = MigrationLint.Lint(name, sql, cutoff);
        foreach (var f in findings) output.WriteLine(f);
        return findings.Count == 0 ? 0 : 1;
    }
}
