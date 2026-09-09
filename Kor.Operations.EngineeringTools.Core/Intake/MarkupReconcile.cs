using Kor.Operations.EngineeringTools.PdfToSafe;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// THE DRAFTER'S REPLY IS BESIDE THE THING IT ANSWERS (foundation §3.1, the loop closed). On the
/// share the engineer writes a round of mark-ups ("MB-5 … mark-ups.pdf"), the drafter replies on
/// the same file — a tick beside each item done, words beside one that could not be — and saves it
/// back-checked; then the next round. So each of the engineer's items is answered by the nearest
/// annotation another author put within an inch of it on the paper: a tick is done, words are a
/// reply to read, nothing is open. Words of the drafter's beside no item are unprompted, and worth
/// the engineer's eye for the same reason.
/// </summary>
public static class MarkupReconcile
{
    public enum Outcome { Done, Replied, Open }

    public sealed record Result(MarkupList.Item Item, Outcome Outcome, string? Reply, string? ReplyAuthor);

    public sealed record PageResult(int Page, string? Sheet, string Engineer, IReadOnlyList<Result> Results, IReadOnlyList<MarkupList.Item> Unprompted)
    {
        public int Done => Results.Count(r => r.Outcome == Outcome.Done);
        public int Replied => Results.Count(r => r.Outcome == Outcome.Replied);
        public int Open => Results.Count(r => r.Outcome == Outcome.Open);
    }

    /// <summary>A reply sits within an inch on the paper of the item it answers.</summary>
    public const double ReplyReachPts = 72;

    /// <summary>The engineer is the author of most of the round's annotations.</summary>
    public static string EngineerOf(SheetRecord round)
    {
        ArgumentNullException.ThrowIfNull(round);
        var all = round.Annotations.Count > 0 ? round.Annotations : round.Markup;
        return all.GroupBy(a => a.Author, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";
    }

    public static PageResult Reconcile(SheetRecord round, SheetRecord backChecked, string? engineer = null)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(backChecked);
        engineer ??= EngineerOf(round);
        double mmPerPt = (round.ScaleDenominator ?? 1) * PdfToSafeConstants.PointsToMm;
        double reachMm = ReplyReachPts * mmPerPt;

        var items = MarkupList.Build(round)
            .Where(i => i.Author.Equals(engineer, StringComparison.OrdinalIgnoreCase)
                        && i.Kind is MarkupList.Kind.Instruction or MarkupList.Kind.Note)
            .ToList();
        var replies = MarkupList.Build(backChecked)
            .Where(i => !i.Author.Equals(engineer, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // A TICK ANSWERS EVERYTHING WITHIN AN INCH OF IT. The engineer writes a column's mark, its
        // size and its reinforcing as three labels at the column; the drafter ticks the column once
        // (MB-4: 300 labels, 228 ticks). A reply is not used up by the first item it answers.
        var used = new HashSet<int>();
        var results = new List<Result>();
        foreach (var item in items)
        {
            int best = -1; double bestD = double.MaxValue;
            for (int j = 0; j < replies.Count; j++)
            {
                double d = Math.Sqrt(Math.Pow(replies[j].XMm - item.XMm, 2) + Math.Pow(replies[j].YMm - item.YMm, 2));
                if (d < bestD) { bestD = d; best = j; }
            }
            if (best < 0 || bestD > reachMm) { results.Add(new Result(item, Outcome.Open, null, null)); continue; }
            used.Add(best);
            var reply = replies[best];
            // THE DRAFTER'S INK BESIDE AN ITEM IS "DONE", WHATEVER ITS SIZE — 18 x 18 or 93 x 87 on
            // MB-6, a tick is drawn as large as the hand draws it. Words beside an item are a reply.
            bool wordless = reply.Kind is MarkupList.Kind.Approval or MarkupList.Kind.Shape
                            || MarkupList.Wordless(new MarkupNote("Ink", reply.Text, reply.Author, reply.Page));
            results.Add(wordless
                ? new Result(item, Outcome.Done, null, reply.Author)
                : new Result(item, Outcome.Replied, reply.Text, reply.Author));
        }
        var unprompted = replies.Where((r, j) => !used.Contains(j) && r.Kind is MarkupList.Kind.Instruction or MarkupList.Kind.Note).ToList();
        return new PageResult(round.PageNumber, round.SheetNumber, engineer, results, unprompted);
    }
}
