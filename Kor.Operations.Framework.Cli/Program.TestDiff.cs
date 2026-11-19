using Kor.Operations.Framework.Proof;

namespace Kor.Operations.Framework.Cli;

/// <summary>W2: <c>kor testdiff</c> - the rules, what it compares and what it does not are <see cref="TestDiff"/>'s header.</summary>
public static partial class Program
{
    private static int TestDiffVerb(Options o, TextWriter output)
    {
        var files = o.Positional.Skip(1).ToList();
        if (files.Count != 2) throw new ArgumentException("testdiff needs <base.trx> <new.trx>");
        foreach (var f in files.Concat(new[] { o.Get("--base-sequence"), o.Get("--new-sequence") }).OfType<string>())
            if (!File.Exists(f)) throw new ArgumentException($"testdiff: no such file: {f}");
        var r = TestDiff.Compare(TestDiff.ReadTrx(files[0], o.Get("--base-sequence")), TestDiff.ReadTrx(files[1], o.Get("--new-sequence")));
        foreach (var l in r.Lines) output.WriteLine(l);
        return r.Exit;
    }
}
