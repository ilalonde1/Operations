using System.Xml.Linq;

namespace Kor.Operations.Framework.Proof;

/// <summary>
/// W2 (Operations docs/codex/CODEX-CLAUDE-WEAKNESSES-RESPONSE.md:72, "a green proxy becomes a claim"): <c>kor testdiff</c>
/// compares two test runs BY NAME and says what the comparison does and does not establish. Found 19 Nov: "PdfToSafe 6 of
/// 591 failed, the same 6 as the base" was reported while 2 more cases had started in both runs and never finished (the blame
/// sequence listed 593; the trx counts only the 591 that reported). The trx itself said "The active test run was aborted".
/// The verdict is never plain "same":
///   INCOMPLETE      - either run was aborted (trx RunInfos), or a case started and never finished (blame sequence): what did
///                     not finish is outside the comparison, and the counts say how much. A skipped test is counted, not this.
///   NEW FAILURES    - a test failed in the new run that did not fail in the base, or a base test is missing from the new run.
///   NO NEW FAILURES - both runs complete, every base test present in the new run, and the failing set is a subset of the base's.
/// INCOMPLETE also when a side holds no result at all, or its ResultSummary/Counters total disagrees with its results.
/// Exit 0 only for NO NEW FAILURES; exit 1 for every other verdict AND for a usage error (a script reading the exit code alone
/// cannot tell those apart - only 0 means anything). COMPARES: test names and outcomes (trx UnitTestResult), the trx Counters
/// total against the results, the run's abort text (RunInfos), started-but-unfinished cases from a blame Sequence file when
/// given. DOES NOT COMPARE: what a test asserts, timings, output text, a test filtered out of both runs (a filter hides it from
/// both sides alike); an abort worded otherwise than vstest's English 'aborted' / 'crashed' / 'inactivity time' (RunInfo
/// outcome="Error" alone is not read: test output errors carry it too). Same-class fault it would miss: a test that silently
/// returns early (passes, asserts nothing) on one side.
/// </summary>
public static class TestDiff
{
    public sealed record Run(
        string Path,
        IReadOnlyList<(string Name, string Outcome)> All, // every UnitTestResult, in order; one name can repeat (theory rows with equal display names)
        int Defined,                                    // TestDefinitions/UnitTest count
        IReadOnlyList<string> Aborts,                   // RunInfos text naming an abort or a crash
        IReadOnlyList<string>? Unfinished,              // from the blame sequence; null = not measured
        int? CountersTotal = null)                      // ResultSummary/Counters total, as the trx states it; null = absent
    {
        /// <summary>Why this run cannot stand as complete beyond an abort or an unfinished case (review row 2267): no result at all
        /// (a filter that matched nothing reads as '0 of 0 failing'), or the trx's own counters disagreeing with its results.</summary>
        public IEnumerable<string> Gaps
        {
            get
            {
                if (Results == 0) yield return "NO RESULTS: the run holds no test result (a filter that matched nothing, or a build that ran none)";
                if (CountersTotal is { } t && t != Results) yield return $"COUNTERS: the trx says total={t} but holds {Results} results";
            }
        }
        private static bool IsFail(string o) => o is "Failed" or "Error" or "Timeout" or "Aborted";
        private static bool IsNotRun(string o) => o is "NotExecuted" or "Pending" or "InProgress" or "Disconnected";
        private static int Rank(string o) => IsFail(o) ? 3 : IsNotRun(o) ? 2 : o == "Passed" ? 0 : 1;

        public int Results => All.Count;
        /// <summary>By NAME, the worst of its results: a name fails if any result under it failed (review of 19 Nov: a dictionary keyed
        /// on name, last one winning, lost 3 of 1056 results of the kor suite and could have hidden a failure behind a later pass).</summary>
        public IReadOnlyDictionary<string, string> Outcomes =>
            All.GroupBy(r => r.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderByDescending(r => Rank(r.Outcome)).First().Outcome, StringComparer.Ordinal);
        public int RepeatedNames => All.GroupBy(r => r.Name, StringComparer.Ordinal).Count(g => g.Count() > 1);
        public IEnumerable<string> Failed => Outcomes.Where(kv => IsFail(kv.Value)).Select(kv => kv.Key);
        public IEnumerable<string> NotRun => Outcomes.Where(kv => IsNotRun(kv.Value)).Select(kv => kv.Key);
        public int PassedResults => All.Count(r => r.Outcome == "Passed");
        public int FailedResults => All.Count(r => IsFail(r.Outcome));
        public int NotRunResults => All.Count(r => IsNotRun(r.Outcome));
        public int OtherResults => Results - PassedResults - FailedResults - NotRunResults;
        /// <summary>A skipped test (NotExecuted) is a decision, not an unfinished run: only an abort or a started, unfinished case is.</summary>
        public bool Complete => Aborts.Count == 0 && (Unfinished is null || Unfinished.Count == 0) && !Gaps.Any();
    }

    public sealed record Result(string Verdict, int Exit, IReadOnlyList<string> Lines);

    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static Run ReadTrx(string trxPath, string? sequencePath = null)
    {
        var doc = XDocument.Load(trxPath);
        var all = doc.Descendants(Ns + "UnitTestResult")
            .Select(r => ((string?)r.Attribute("testName") ?? "", (string?)r.Attribute("outcome") ?? "Unknown")).ToList();
        var defined = doc.Descendants(Ns + "UnitTest").Count();
        var aborts = doc.Descendants(Ns + "RunInfo")
            .Select(ri => ri.Element(Ns + "Text")?.Value ?? "")
            .Where(t => t.Contains("aborted", StringComparison.OrdinalIgnoreCase) || t.Contains("crashed", StringComparison.OrdinalIgnoreCase)
                     || t.Contains("inactivity time", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.ReplaceLineEndings(" ").Trim())
            .Distinct().ToList();
        var counters = doc.Descendants(Ns + "Counters").FirstOrDefault();
        int? total = int.TryParse((string?)counters?.Attribute("total"), out var t) ? t : null;
        return new Run(trxPath, all, defined, aborts, sequencePath is null ? null : ReadUnfinished(sequencePath), total);
    }

    /// <summary>A blame collector's Sequence_*.xml: every test it saw start, in order; Completed="False" = started, never finished.</summary>
    public static IReadOnlyList<string> ReadUnfinished(string sequencePath) =>
        XDocument.Load(sequencePath).Descendants().Where(e => e.Name.LocalName == "Test")
            .Where(e => string.Equals((string?)e.Attribute("Completed"), "False", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("Name") ?? "").Distinct().ToList();

    public static Result Compare(Run b, Run n)
    {
        var lines = new List<string>();
        void Side(string label, Run r)
        {
            lines.Add($"{label}: {r.Path}");
            lines.Add($"  {r.Results} results for {r.Defined} defined tests (a theory's data rows each give a result): {r.PassedResults} passed, "
                      + $"{r.FailedResults} failed, {r.NotRunResults} skipped/not run, {r.OtherResults} other outcome");
            lines.Add($"  by name: {r.Outcomes.Count} distinct names, {r.RepeatedNames} with more than one result (a name fails if any of its results failed)");
            lines.Add(r.Unfinished is null
                ? "  started-but-unfinished: NOT MEASURED (no blame sequence given; a hung case is invisible to the trx counts)"
                : $"  started-but-unfinished: {r.Unfinished.Count}" + string.Concat(r.Unfinished.Select(u => "\n    " + u)));
            foreach (var a in r.Aborts) lines.Add("  RUN ABORTED: " + a);
            foreach (var g in r.Gaps) lines.Add("  " + g);
        }
        Side("base", b);
        Side("new ", n);

        var bFail = b.Failed.ToHashSet(StringComparer.Ordinal);
        var nFail = n.Failed.ToHashSet(StringComparer.Ordinal);
        var newFail = nFail.Where(t => !bFail.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var fixedNow = bFail.Where(t => !nFail.Contains(t) && n.Outcomes.ContainsKey(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var vanished = b.Outcomes.Keys.Where(t => !n.Outcomes.ContainsKey(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var added = n.Outcomes.Keys.Count(t => !b.Outcomes.ContainsKey(t));
        var addedFailing = n.Outcomes.Keys.Where(t => !b.Outcomes.ContainsKey(t) && nFail.Contains(t)).ToList();

        lines.Add($"failures in both: {nFail.Count(bFail.Contains)}; only in new: {newFail.Count}; failing in base, passing in new: {fixedNow.Count}");
        lines.Add($"tests only in new: {added} ({addedFailing.Count} of them failing); base tests missing from new: {vanished.Count}");
        foreach (var t in newFail) lines.Add("  NEW FAILURE: " + t);
        foreach (var t in vanished.Take(50)) lines.Add("  MISSING FROM NEW: " + t);
        if (vanished.Count > 50) lines.Add($"  ... and {vanished.Count - 50} more missing");

        string verdict;
        if (!b.Complete || !n.Complete)
            verdict = "INCOMPLETE - " + string.Join("; ", new[] { ("base", b), ("new", n) }.Where(x => !x.Item2.Complete).Select(x =>
                $"{x.Item1}: {x.Item2.Aborts.Count} abort message(s), {x.Item2.Unfinished?.Count ?? 0} started and unfinished"
                + string.Concat(x.Item2.Gaps.Select(g => ", " + g.Split(':')[0]))))
                + ". What did not finish is NOT compared; do not call these runs the same.";
        else if (newFail.Count > 0 || vanished.Count > 0)
            verdict = $"NEW FAILURES - {newFail.Count} failing only in new, {vanished.Count} base tests missing from new";
        else
            verdict = $"NO NEW FAILURES - {nFail.Count} of {n.Outcomes.Count} test names failing in new, every one also failing in base ({bFail.Count} of {b.Outcomes.Count}); "
                      + (b.Unfinished is null || n.Unfinished is null ? "hung cases NOT MEASURED (no blame sequence on both sides)" : "no case left unfinished");
        lines.Add("VERDICT: " + verdict);
        return new Result(verdict, verdict.StartsWith("NO NEW FAILURES", StringComparison.Ordinal) ? 0 : 1, lines);
    }
}
