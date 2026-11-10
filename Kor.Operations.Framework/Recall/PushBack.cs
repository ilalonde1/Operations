using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Recall;

/// <summary>
/// Board row 1893 part 3, the push-back (BRAIN lane, 10 Nov): a Stop check on the reply about to go to Ian. Narrow on purpose; two rules.
///
/// RULE 1, DECIDED. The reply proposes or asks (<see cref="IsProposal"/>), and one of its <see cref="Nearest"/> nearest records
/// (<see cref="BrainRecall.Rank"/>) is a DECISION or DEAD END that carries Ian's own quoted words, scored at least
/// <see cref="MinScore"/> on at least <see cref="MinTermsHit"/> shared words, and the reply does not cite it by id. Then the turn is
/// refused once, quoting his words and the record: re-read it, cite it, or say plainly to Ian that the proposal goes against it.
///
/// RULE 2, SEEN. The reply tells Ian he has seen, looked at or approved something (<see cref="SeenClaim"/>). Nothing in the brain
/// records what Ian has seen, so no claim of it can be checked: the turn is refused once, asking for the evidence (his message, a file
/// opened on his screen) or the claim out. From 10 Nov: "the sheets you've seen" - Ian: "I HAVEN"T SEEN ANY THING YET?!?!?!".
///
/// Each refusal fires once per session per key (a record id, or rule 2's sentence), and never when Claude Code says a Stop hook is
/// already active, so it cannot loop. Does NOT: judge whether the proposal really contradicts the decision (a shared-word match is
/// not a contradiction, so rule 1 asks, it does not decide); see a decision recorded under other words; check a finding (reviews,
/// measurements: only Ian's decisions and dead ends); look at tool calls (Stop only). False alarms are stated in BrainRecallTests.
/// </summary>
public static class PushBack
{
    public const int Nearest = 5;
    public const double MinScore = 12.0;
    public const int MinTermsHit = 4;
    public const int MinReplyChars = 60;

    private static readonly Regex Proposal = new(
        @"(?i)\b(should I|shall I|do you want( me)?|want me to|would you like|I propose|I'd propose|I recommend|my recommendation|the fix is|the plan is|my plan|your call|options? (are|is)\b|option [a-c1-3]\b)",
        RegexOptions.CultureInvariant);

    // Seen, saw, looked at only: "approved" claims were 3 of the 5 false alarms in the 10 Nov replay (cde1e168, 44 replies), each already
    // naming its evidence. A claim inside quotation marks is a quote, not a claim.
    private static readonly Regex Seen = new(
        @"(?i)\b(you(?:'ve|’ve| have)? (?:already )?(?:seen|saw|looked at)|(?:as|that|which) you (?:saw|looked at)|you(?:'ve|’ve| have) (?:already )?had (?:a )?look)\b",
        RegexOptions.CultureInvariant);

    /// <summary>Evidence named in the claim's own sentence: a record or row id, or quoted words.</summary>
    private static readonly Regex Evidence = new(@"(#\d{3,}|\brows? \d{3,}|[""“][^""”]{3,}[""”])", RegexOptions.CultureInvariant);

    private static readonly Regex IanQuote = new(
        @"\bIan\b[^""“\n]{0,120}?[:\s,]\s*(?:[""“]([^""”\n]{6,300})[""”]|'([^'\n]{6,300})'(?=[\s.,;:)]|$))",
        RegexOptions.CultureInvariant);

    public static bool IsProposal(string reply) => Proposal.IsMatch(reply);

    /// <summary>The sentences of a reply that propose or ask (<see cref="IsProposal"/>), joined; empty when none. The push-back ranks
    /// these, not the whole reply: a reply's report of what was done outvotes the one sentence that proposes.</summary>
    public static string ProposalText(string reply) =>
        string.Join(" ", Regex.Split(reply ?? "", @"(?<=[.!?])\s+|\n+").Where(s => Proposal.IsMatch(s)).Select(s => s.Trim()));

    /// <summary>The sentence holding a claim that Ian saw something, or null.</summary>
    public static string? SeenClaim(string reply)
    {
        foreach (Match m in Seen.Matches(reply))
        {
            var start = reply.LastIndexOfAny(new[] { '.', '!', '?', '\n' }, Math.Max(0, m.Index - 1)) + 1;
            var end = reply.IndexOfAny(new[] { '.', '!', '?', '\n' }, m.Index + m.Length);
            var sentence = reply[start..(end < 0 ? reply.Length : end + 1)].Trim();
            var before = reply[start..m.Index];
            if (before.Count(c => c is '"' or '“' or '”') % 2 == 1) continue;   // inside a quotation
            if (Evidence.IsMatch(sentence)) continue;
            return sentence;
        }
        return null;
    }

    /// <summary>Ian's first quoted words in a record, or null when the record quotes none.</summary>
    public static string? IanWords(string text)
    {
        var m = IanQuote.Match(text ?? "");
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim() : null;
    }

    public sealed record Verdict(bool Refuse, string? Key, string Reason);

    public static Verdict Decide(string reply, IReadOnlyList<BrainRecall.Ranked> ranked, ISet<string> alreadyRefused)
    {
        if (string.IsNullOrWhiteSpace(reply) || reply.Length < MinReplyChars) return new Verdict(false, null, "reply too short to judge");
        var seen = SeenClaim(reply);
        if (seen is not null)
        {
            // stable across processes (each hook call is one): string.GetHashCode is randomised per process
            var key = "seen:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seen)))[..16];
            if (!alreadyRefused.Contains(key))
                return new Verdict(true, key,
                    $"BRAIN PUSH-BACK (seen): this reply tells Ian he has seen or approved something - \"{Cut(seen, 200)}\". Nothing in the record shows " +
                    "what Ian has seen. Name the evidence (his own message, or the file opened on his screen and when) or take the claim out. " +
                    "10 Nov, after \"the sheets you've seen\", Ian: \"I HAVEN'T SEEN ANY THING YET?!?!?!\"");
        }
        if (!IsProposal(reply)) return new Verdict(false, null, "not a proposal or question to Ian");
        foreach (var r in ranked.Take(Nearest))
        {
            var d = r.Doc;
            if (d.Board || (d.Kind != "decision" && d.Kind != "dead-end")) continue;
            if (r.Score < MinScore || r.Hit.Count < MinTermsHit) continue;
            if (Regex.IsMatch(reply, @"#\s?" + d.Id + @"\b")) continue;
            var key = "record:" + d.Id;
            if (alreadyRefused.Contains(key)) continue;
            var words = IanWords(d.Text);
            if (words is null) continue;
            return new Verdict(true, key,
                $"BRAIN PUSH-BACK (decided): this proposal shares {r.Hit.Count} words ({string.Join(", ", r.Hit.Take(8))}) with Ian's recorded " +
                $"{d.Kind} {d.Ref} [{d.Where}] {d.Name}: Ian: \"{Cut(words, 300)}\". Read it (brain.Memory Id {d.Id}). If the proposal agrees with it, " +
                $"cite {d.Ref}; if it goes against it, tell Ian so in plain words with his own words above. A word match is not proof it applies.");
        }
        return new Verdict(false, null, "no decision of Ian's near enough");
    }

    private static string Cut(string s, int most) => s.Length <= most ? s : s[..(most - 3)] + "...";
}
