#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Kor.Opportunities.Data.Crm;

/// <summary>Where a session reports back: the run and its one-time token.</summary>
public sealed record BdPromptHandle(long RunId, string Token);

/// <summary>A question in Ian's own words, about today, one person or one pursuit.</summary>
/// <param name="Context">What the Director knows about the subject right now, one line each.</param>
/// <param name="Cards">Accepted cards that apply to this context, in full.</param>
/// <param name="Library">Every other accepted card, by title: the session pulls one in full with `doctrine show`.</param>
public sealed record BdAskInput(string Question, string AskedBy, string Subject, DoctrineContext Doctrine, IReadOnlyList<string> Context,
    IReadOnlyList<DoctrineCardRow> Cards, IReadOnlyList<DoctrineCardRow> Library, DateTimeOffset Now);

/// <summary>Turn the books into doctrine for a role, building on what Ian has already accepted.</summary>
public sealed record BdSynthesisInput(string Role, string AskedBy, IReadOnlyList<string> Books, IReadOnlyList<DoctrineCardRow> Accepted,
    IReadOnlyList<DoctrineCardRow> Proposed, string? Focus, DateTimeOffset Now,
    IReadOnlyList<DoctrineCardRecord> Records, IReadOnlyList<ClosedPursuit> Closed);

/// <summary>
/// The BD consigliere's prompts, composed from what the Director knows at the moment one is opened, so they cannot go stale —
/// the method of NetworkOps' PromptComposer. Pure: the app gathers the input, this only writes it, so a test can prove what a
/// prompt says. Every prompt carries the same rules and ends the same way: report back, so what the session learned comes into
/// the system as proposed doctrine instead of staying in a terminal. The session is Claude Code on Ian's PC (rule 14).
/// </summary>
public static class BdPromptComposer
{
    public const string Cli = "dotnet run --project tools/BdKnowledgeIngest --";

    public static string Ask(BdAskInput i, BdPromptHandle report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {i.Question.Trim()}");
        sb.AppendLine();
        sb.AppendLine($"Asked by {i.AskedBy} in BD Director at {i.Now.ToLocalTime():yyyy-MM-dd HH:mm}, about {i.Subject}. Written from the live database at that moment.");
        sb.AppendLine("Your job: answer the question as KOR's BD consigliere — lead with the answer, then why, then the play — grounded in the facts below and in the books, and report back.");
        sb.AppendLine();
        Rules(sb);
        sb.AppendLine($"## {i.Subject}: what the Director knows");
        foreach (var line in i.Context) sb.AppendLine("- " + line);
        if (i.Doctrine.Situations.Count > 0) sb.AppendLine("- Situations: " + string.Join(", ", i.Doctrine.Situations));
        sb.AppendLine();
        Doctrine(sb, i.Cards, i.Library);
        LookFurther(sb);
        Closing(sb, report, synthesis: false);
        return sb.ToString();
    }

    public static string Synthesis(BdSynthesisInput i, BdPromptHandle report)
    {
        var sb = new StringBuilder();
        var role = i.Role switch
        {
            "Operations" => "an operations director",
            "IT" => "a systems and IT director for a sixty-person engineering firm",
            _ => "a business development director",
        };
        sb.AppendLine($"# Synthesize the {i.Role} doctrine from the books");
        sb.AppendLine();
        sb.AppendLine($"Asked by {i.AskedBy} at {i.Now.ToLocalTime():yyyy-MM-dd HH:mm}.");
        sb.AppendLine($"Your job: read the books in KOR's knowledge base and turn them into the working doctrine of {role} at KOR Structural, as doctrine cards Ian accepts or rejects one by one.");
        if (!string.IsNullOrWhiteSpace(i.Focus)) sb.AppendLine($"This run's focus: {i.Focus!.Trim()}");
        sb.AppendLine();
        Rules(sb, experience: true);
        sb.AppendLine("## KOR, briefly");
        sb.AppendLine("- KOR Structural is a structural engineering consultancy. Business development runs in five regions, each with a lead: Vancouver and the Lower Mainland, Vancouver Island, the Okanagan and BC Interior, Alberta, and the United States.");
        sb.AppendLine("- Ian Lalonde was voted in to manage BD on 9 October 2026. He is not an engineer. He committed to a short monthly all-hands, a 20-minute one-to-one with each lead every two weeks, and a maintained action list.");
        sb.AppendLine("- Read the live picture yourself before writing cards that depend on it (see \"Looking further\").");
        sb.AppendLine();
        sb.AppendLine("## The books");
        foreach (var b in i.Books) sb.AppendLine("- " + b);
        sb.AppendLine();
        sb.AppendLine("## How to work");
        sb.AppendLine("1. Work theme by theme, not book by book: finding and qualifying work, the first meeting, account development, proposals and pricing, negotiation, referrals and relationships, running the team, strategy and choosing where to play, judgement and forecasting.");
        sb.AppendLine($"2. For each theme, pull the passages: `{Cli} claims search \"<theme>\" --take 30` and `{Cli} search \"<theme>\" --take 15 --per-source 3`. Read them; export a whole book with `{Cli} export <sourceId> --to <folder>` only when you need its argument end to end.");
        sb.AppendLine("3. Where the authors agree, write one card. Where they disagree, say so in the card and say which applies to a firm like KOR and why.");
        sb.AppendLine("4. Aim for 40 to 100 cards that together are the doctrine. A card earns its place by changing what Ian does on a real day; drop what is true but idle.");
        sb.AppendLine("5. Book-derived cards cite their passages: book title and passage reference or claim id. Experience-derived cards cite the pursuit ids under KOR's own record below. Two sources beat one.");
        sb.AppendLine("6. Do not repeat an accepted card. To correct or widen one, propose a card with `amends` set to its id.");
        sb.AppendLine();
        Doctrine(sb, i.Accepted, Array.Empty<DoctrineCardRow>());
        if (i.Proposed.Count > 0)
        {
            sb.AppendLine("## Already proposed, waiting for Ian");
            foreach (var c in i.Proposed.Take(200)) sb.AppendLine($"- #{c.Id} {c.Title}");
            sb.AppendLine();
        }

        OwnRecord(sb, i);
        LookFurther(sb);
        Closing(sb, report, synthesis: true);
        return sb.ToString();
    }

    private static void OwnRecord(StringBuilder sb, BdSynthesisInput i)
    {
        sb.AppendLine("## What KOR's own record shows");
        sb.AppendLine(DoctrineTrackRecord.Rule);
        sb.AppendLine();
        sb.AppendLine("### Cards needing a look");
        var flagged = i.Records.Where(r => r.Flags.Count > 0).ToArray();
        if (flagged.Length == 0) sb.AppendLine("No flagged cards in this record.");
        foreach (var r in flagged) sb.AppendLine($"- Card #{r.CardId}: {r.FlagText}. {r.Summary}.");
        sb.AppendLine();
        sb.AppendLine("| Card | Suggested | Done | Not today | Not doing | Undecided | Advanced | Won | Lost | Archived | Last suggested |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var r in i.Records.OrderByDescending(r => r.Flags.Count > 0).ThenBy(r => r.CardId))
            sb.AppendLine($"| #{r.CardId} | {r.Suggested} | {r.Done} | {r.NotToday} | {r.NotDoing} | {r.Undecided} | {r.PursuitsAdvanced} | {r.PursuitsWon} | {r.PursuitsLost} | {r.PursuitsArchived} | {r.LastSuggested?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "never"} |");
        sb.AppendLine();
        sb.AppendLine("### Pursuits closed in the last 90 days");
        foreach (var p in i.Closed.Where(p => p.ClosedAtUtc >= i.Now.AddDays(-90) && p.ClosedAtUtc <= i.Now && p.Stage is 6 or 7 or 8))
        {
            var stage = p.Stage switch { 6 => "Won", 7 => "Lost", _ => "Archived" };
            sb.AppendLine($"- Pursuit #{p.EngagementId}: {p.Company} · {stage} · {p.ClosedAtUtc:yyyy-MM-dd}. Reason: {(string.IsNullOrWhiteSpace(p.Reason) ? "not recorded" : p.Reason)}");
        }
        sb.AppendLine();
        sb.AppendLine("Propose amendments or retirements where the record argues for them. Use `amends` for an amendment; recommend retirements by card id in your answer for Ian to decide with Retire. Nothing retires automatically.");
        sb.AppendLine("Propose a card from KOR's experience only when two or more outcomes share a reason, citing those pursuits by id in the card's citations. A missing reason is not shared evidence. Distinguish observation from causation; a flag alone is not a verdict.");
        sb.AppendLine();
    }

    private static void Rules(StringBuilder sb, bool experience = false)
    {
        sb.AppendLine("## Rules");
        sb.AppendLine("- Never call the Anthropic API or any outside service, and never send anything to anyone. Every message you draft is addressed to Ian; he decides what goes out.");
        sb.AppendLine(experience
            ? "- Ground recommendations in cited book passages or in KOR's recorded outcomes under the experience rule below. Say when the evidence is missing."
            : "- Ground every recommendation in the books: name the book and the passage. If the books do not speak to it, say so instead of inventing a principle.");
        sb.AppendLine("- Ground every fact in what is below or in a query you ran and show. Never say something is absent without the query that proves it.");
        sb.AppendLine("- Read only. The one write you make is the report at the end; changes to pursuits, actions and people are Ian's, in the app.");
        sb.AppendLine("- Plain words, short sentences. Ian manages BD; he is not an engineer and has no time for jargon.");
        sb.AppendLine();
    }

    private static void Doctrine(StringBuilder sb, IReadOnlyList<DoctrineCardRow> cards, IReadOnlyList<DoctrineCardRow> library)
    {
        sb.AppendLine("## Doctrine Ian has accepted");
        if (cards.Count == 0 && library.Count == 0)
        {
            sb.AppendLine("None yet. Work from the books directly.");
            sb.AppendLine();
            return;
        }

        foreach (var c in cards)
        {
            sb.AppendLine($"### #{c.Id} {c.Title}");
            sb.AppendLine($"- Principle: {c.Principle}");
            sb.AppendLine($"- You are in it when: {c.Recognise}");
            sb.AppendLine($"- The play: {c.Play}");
            if (c.Warning is { } w) sb.AppendLine($"- Warning sign: {w}");
            sb.AppendLine($"- From: {c.Citations}");
        }

        if (library.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Other accepted cards, by title (pull one in full with `{Cli} doctrine show <id>`):");
            foreach (var c in library.Take(300)) sb.AppendLine($"- #{c.Id} {c.Title}");
        }

        sb.AppendLine();
    }

    private static void LookFurther(StringBuilder sb)
    {
        sb.AppendLine("## Looking further");
        sb.AppendLine($"- The books: `{Cli} list`, `{Cli} claims search \"<question>\" --take 12`, `{Cli} search \"<question>\" --take 10`.");
        sb.AppendLine($"- The doctrine: `{Cli} doctrine list` and `{Cli} doctrine show <id>`.");
        sb.AppendLine("- The live BD data is in KorOpportunitiesDb, schema `opportunities`: CrmEngagements (pursuits; Stage 1 Pending, 3 Submitted, 6 Won, 7 Lost, 8 Archived), CrmActivities (touches), BdAsk (the action register), BdMeeting, BdReference, CanonicalOrg (companies), IntelPerson. Read with `python -I tools/SqlQuery/oq.py -q \"SELECT …\"` (SELECT only).");
        sb.AppendLine();
    }

    private static void Closing(StringBuilder sb, BdPromptHandle report, bool synthesis)
    {
        sb.AppendLine("## When you are done: report back (required)");
        sb.AppendLine("Write the report to a JSON file, then run the command below once. It lands in BD Director: your answer under the question, and every card as a proposal Ian accepts or rejects.");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"outcome\": \"answered\",            // answered | partly | not-answered");
        sb.AppendLine(synthesis
            ? "  \"answer\": \"What you covered, which themes are thin in the books, and where the authors disagree.\","
            : "  \"answer\": \"Your answer, in plain words: the answer first, then why, then the play. Cite the books.\",");
        sb.AppendLine("  \"cards\": [                          // optional on an ask; the whole point of a synthesis");
        sb.AppendLine("    {");
        sb.AppendLine("      \"plain\": \"In plain English: what this card is and what accepting it will change. Ian reads this first and decides from it.\",");
        sb.AppendLine("      \"title\": \"Rescue a relationship that has gone quiet\",");
        sb.AppendLine("      \"appliesTo\": \"situation:going-cold, moment:pursuit\",");
        sb.AppendLine("      \"principle\": \"The principle, in one or two sentences.\",");
        sb.AppendLine("      \"recognise\": \"How you know you are in this situation, from what the Director shows.\",");
        sb.AppendLine("      \"play\": \"What to do, concretely, this week.\",");
        sb.AppendLine("      \"warning\": \"What it looks like when this is going wrong (optional).\",");
        sb.AppendLine(synthesis
            ? "      \"citations\": \"Book title and passage reference or claim id; or KOR experience: pursuit #101 and pursuit #102 with their shared outcome reason.\","
            : "      \"citations\": \"Book title, passage reference or claim id; a second source.\",");
        sb.AppendLine("      \"tags\": \"relationships, follow-up\"");
        sb.AppendLine("      // \"amends\": 12   to correct or widen accepted card #12: accepted, it retires #12");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("appliesTo uses a closed vocabulary, comma = or: any | moment:" + string.Join("|", BdDoctrine.Moments)
            + " | situation:" + string.Join("|", BdDoctrine.Situations) + " | stage:Pending|Submitted | region:<CRM region> | person:<mailbox> | org:<company id> | sector:<word>.");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine($"{Cli} prompt report {report.RunId.ToString(CultureInfo.InvariantCulture)} <report.json> --token {report.Token}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("If the command refuses the report it says why; fix the file and run it again. Sending again REPLACES your earlier answer and your still-proposed cards; cards Ian has already decided stay as they are.");
        sb.AppendLine("Nothing goes into the doctrine twice. The command compares every card by meaning with every card already there, accepted, waiting or rejected: a FAIL repeats one (drop it, or set `amends` to improve an accepted card); a CHECK is close to one (read both, then drop, amend or keep it, and send again with `--checked`). To check before sending: `dotnet run --project tools/BdKnowledgeIngest -- doctrine similar <report.json>`.");
        sb.AppendLine($"The token is for run {report.RunId.ToString(CultureInfo.InvariantCulture)} only.");
    }
}
