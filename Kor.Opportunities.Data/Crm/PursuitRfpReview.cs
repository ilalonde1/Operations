#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>A question for the buyer: what to ask, why it matters to KOR, and where in the RFP it comes from.</summary>
public sealed record RfpQuestion(string Question, string Why, string? Ref);

/// <summary>
/// Something to do because of what the RFP says: what, who ("owner" = the pursuit's owner, "Ian", or an email address), by
/// when, and why. Each becomes an ask on its owner's My day (migration 378).
/// </summary>
public sealed record RfpAction(string Action, string Owner, DateOnly Due, string Why);

/// <summary>What a review session reports: migration 377's PursuitReview row, the checklist for PursuitRequirement, and the actions.</summary>
public sealed record RfpReviewReport(string Outcome, string Recommendation, string Summary, string Reasons,
    IReadOnlyList<RfpQuestion> Questions, string? QuestionsDeadline, string? QuestionsTo, string? FilesRead,
    IReadOnlyList<PursuitRequirementInput> Checklist, IReadOnlyList<RfpAction> Actions);

/// <summary>A stored review, as the pursuit shows it.</summary>
public sealed record PursuitReviewRow(long Id, long EngagementId, long? PromptRunId, string Recommendation, string Summary, string Reasons,
    IReadOnlyList<RfpQuestion> Questions, string? QuestionsDeadline, string? QuestionsTo, string? FilesRead, string? ChecklistNote,
    string ReportedBy, DateTimeOffset ReportedAtUtc, IReadOnlyList<RfpAction> Actions);

/// <summary>One live tender pursuit as the BD Director's Today and Pursuits screens show it, with its latest review if any.</summary>
public sealed record TenderBoardRow(long EngagementId, string Project, string? Buyer, int Stage, string? OwnerMailbox, string? OwnerName,
    DateTimeOffset? DeadlineUtc, string? NextStep, DateTimeOffset? NextStepDueUtc, string? Recommendation, string? Reasons,
    IReadOnlyList<RfpQuestion> Questions, string? QuestionsDeadline, IReadOnlyList<RfpAction> Actions, string? QuestionsTo,
    bool ReviewRunning, long? OpportunityId, DateTimeOffset? UpdatedAtUtc)
{
    /// <summary>A grabbed forward project with no tender yet: no RFP to read, no deadline.</summary>
    public bool IsForward => OpportunityId is null;
}

/// <summary>A pursuit's file, as the review prompt lists it.</summary>
public sealed record RfpReviewFile(long Id, string FileName, long SizeBytes, string UploadedBy, DateTimeOffset UploadedAtUtc);

/// <summary>Everything the review prompt says about the pursuit, gathered by the app at the moment the review opens.</summary>
public sealed record RfpReviewInput(long EngagementId, string Project, string? Buyer, string? BuyerContact, string? BuyerEmail,
    DateTimeOffset? SubmissionDeadlineUtc, string? Owner, string Stage, string? PostingUrl, IReadOnlyList<RfpReviewFile> Files,
    int ChecklistItems, string RequestedBy, DateTimeOffset Now);

/// <summary>Where a pursuit's documents and review stand, read in one query for the pursuit's status line.</summary>
public sealed record PursuitReviewStatus(long EngagementId, string? Portal, int PortalFiles, DateTimeOffset? LastFileAtUtc,
    DateTimeOffset? LastFetchAtUtc, bool? LastFetchSucceeded, string? LastFetchNote, string? PortalSessionError,
    long? RunId, DateTimeOffset? RunCreatedAtUtc, string? RunCreatedBy, DateTimeOffset? RunReportedAtUtc, int Reviews);

/// <summary>
/// Claude's reading of a grabbed tender's RFP. When the documents arrive, the app on a PC with Claude Code opens a session on
/// them (the BD Director's method: <see cref="BdPromptComposer"/>, a one-time token, a report back through the BD CLI); the
/// session reads the files from the database with <c>pursuit files text</c> and reports a summary, a recommendation, the
/// questions for the buyer and the checklist. Pure parts here, so a test can prove what the prompt says and what a report
/// must hold; <see cref="SqlPursuitReviewStore"/> keeps the result. No model is called from the app (rule 14).
/// </summary>
public static class PursuitRfpReview
{
    public const string Kind = "rfp";
    public static IReadOnlyList<string> Recommendations { get; } = Array.AsReadOnly(new[] { "go", "no-go", "decide" });

    /// <summary>The uploaders that mean "fetched from the portal by the Worker": a review starts when one of these lands.</summary>
    public static IReadOnlyList<string> PortalUploaders { get; } = Array.AsReadOnly(new[] { "apc", "bids-and-tenders" });

    public static string Subject(long engagementId, string project) => Clip($"RFP review: pursuit #{engagementId} {project}", 200);

    public static string Prompt(RfpReviewInput i, BdPromptHandle report)
    {
        var cli = BdPromptComposer.Cli;
        var sb = new StringBuilder();
        sb.AppendLine($"# Read the RFP for pursuit #{i.EngagementId}: {i.Project}");
        sb.AppendLine();
        sb.AppendLine($"Opened for {i.RequestedBy} at {i.Now.ToLocalTime():yyyy-MM-dd HH:mm}, from the live database, because this tender's documents have arrived.");
        sb.AppendLine("Your job: read every document on this pursuit and tell KOR, in plain words, what this is, whether to bid and why, what to ask the buyer before the questions deadline, and what the submission must contain. Then report back; the app shows your report on the pursuit.");
        sb.AppendLine();
        sb.AppendLine("## Rules");
        sb.AppendLine("- Never call the Anthropic API or any outside service, and never send anything to anyone. Questions are drafted for Ian; he decides what goes out.");
        sb.AppendLine("- KOR Structural is a structural engineering consultancy. Say plainly which parts of the scope are not structural (civil utilities, roads, mechanical, testing) and what that means for a bid: a sub-consultant, a question to the buyer, or a reason not to bid.");
        sb.AppendLine("- Ground every statement in the documents: name the section or page. Never say the RFP lacks something without the search that shows it.");
        sb.AppendLine("- Never cite tracker win rates; KOR's real win rate is not yet known.");
        sb.AppendLine("- Read only. The one write you make is the report at the end.");
        sb.AppendLine("- Plain words, short sentences. Ian manages BD; he is not an engineer.");
        sb.AppendLine();
        sb.AppendLine("## The pursuit");
        sb.AppendLine($"- Pursuit #{i.EngagementId}, stage {i.Stage}{(i.Owner is { } o ? ", owner " + o : "")}.");
        sb.AppendLine($"- Project: {i.Project}");
        if (i.Buyer is { } b) sb.AppendLine($"- Buyer: {b}");
        if (i.BuyerContact is not null || i.BuyerEmail is not null) sb.AppendLine($"- Buyer contact on file: {i.BuyerContact ?? "(no name)"}{(i.BuyerEmail is { } e ? " <" + e + ">" : "")}");
        sb.AppendLine($"- Submission deadline on file: {(i.SubmissionDeadlineUtc is { } d ? d.ToLocalTime().ToString("ddd d MMM yyyy h:mm tt", CultureInfo.InvariantCulture) : "not on file")}. Trust the RFP's own timetable over this.");
        if (i.PostingUrl is { } u) sb.AppendLine($"- Posting: {u}");
        sb.AppendLine($"- Checklist items already on the pursuit: {i.ChecklistItems}.");
        sb.AppendLine();
        sb.AppendLine("## The documents");
        foreach (var f in i.Files)
            sb.AppendLine($"- file {f.Id}: {f.FileName} ({f.SizeBytes:N0} bytes, from {f.UploadedBy}, {f.UploadedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm})");
        sb.AppendLine();
        sb.AppendLine($"Read them all as text, straight from the database (nothing is copied to disk): `{cli} pursuit files text {i.EngagementId}`.");
        sb.AppendLine($"One file: `{cli} pursuit files text {i.EngagementId} --file <file id>`. A scanned page has no text; say which.");
        sb.AppendLine();
        sb.AppendLine("## What to work out");
        sb.AppendLine("1. What it is: the buyer, the work, the contract form and term, the first work package if any, the timetable (questions deadline, addenda, submission, interviews), how it is scored, page limits and mandatory forms.");
        sb.AppendLine("2. Whether KOR should bid: `go`, `no-go`, or `decide` when it turns on something only Ian or the buyer can settle. Reasons first: fit with structural work, scope KOR does not do, pricing weight, references needed, insurance, travel, anything unusual.");
        sb.AppendLine("3. The questions for the buyer: only ones whose answer changes whether or how KOR bids. Each with why it matters and the section it comes from. Note the questions deadline and the address questions go to.");
        sb.AppendLine("4. The checklist: every requirement a compliant submission must meet, quoted, with its section.");
        sb.AppendLine("5. The actions: what has to happen next, in order, each with who does it and by when, so the review is something to act on. Use the RFP's own dates (send questions before the questions deadline, start reference requests now, decide go/no-go a working day before the questions deadline or, if that has passed, within two working days). A no-go still has an action: \"Archive the pursuit\" for the owner. Owner is \"owner\" (whoever owns the pursuit), \"Ian\" (go/no-go and anything needing the BD manager), or a person's email address. Today is " + i.Now.ToLocalTime().ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture) + "; no due date before it.");
        if (i.ChecklistItems > 0) sb.AppendLine("   The pursuit already has a checklist; yours is kept with the review but does not replace it.");
        sb.AppendLine();
        sb.AppendLine("## When you are done: report back (required)");
        sb.AppendLine("Write the report to a JSON file, then run the command below once.");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"outcome\": \"answered\",               // answered | partly (some documents unreadable) | not-answered");
        sb.AppendLine("  \"recommendation\": \"decide\",          // go | no-go | decide");
        sb.AppendLine("  \"summary\": \"What this is, in a short paragraph: buyer, work, contract, timetable, scoring.\",");
        sb.AppendLine("  \"reasons\": \"Why go, no-go or decide: one point per line, the deciding one first.\",");
        sb.AppendLine("  \"questions\": [ { \"question\": \"Can a structural firm propose with a civil sub-consultant?\", \"why\": \"Civil utilities are a minimum qualification; KOR does not do them.\", \"ref\": \"Appendix E A.3\" } ],");
        sb.AppendLine("  \"questionsDeadline\": \"Fri 23 Oct 2026, 2:00 pm MST\",");
        sb.AppendLine("  \"questionsTo\": \"name@buyer.ca\",          // the email address only; how questions must be sent goes in summary");
        sb.AppendLine("  \"filesRead\": \"Which files you read, and any you could not.\",");
        sb.AppendLine("  \"actions\": [ { \"action\": \"Decide go or no-go\", \"owner\": \"Ian\", \"due\": \"2026-10-22\", \"why\": \"The questions close 23 Oct; a no-go saves the proposal effort.\" } ],   // 1 to 12, in the order to do them");
        sb.AppendLine("  \"checklist\": [ { \"requirement\": \"Quoted requirement\", \"sourceRef\": \"s. 3.1.2\", \"checkKind\": \"Manual\", \"checkParameter\": null } ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("checkKind is Manual, PhrasePresent (checkParameter = the phrase the proposal must contain), PageLimit (checkParameter = the page count) or FileName (checkParameter = a file the submission must include).");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine($"{cli} pursuit review report {report.RunId.ToString(CultureInfo.InvariantCulture)} <report.json> --token {report.Token}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("If the command refuses the report it says why; fix the file and run it again. Sending again replaces this run's review.");
        sb.AppendLine($"The token is for run {report.RunId.ToString(CultureInfo.InvariantCulture)} only.");
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private sealed record ReportFile(string? Outcome, string? Recommendation, string? Summary, string? Reasons, List<QuestionFile>? Questions,
        string? QuestionsDeadline, string? QuestionsTo, string? FilesRead, List<PursuitRequirementInput>? Checklist, List<ActionFile>? Actions);
    private sealed record QuestionFile(string? Question, string? Why, string? Ref);
    private sealed record ActionFile(string? Action, string? Owner, string? Due, string? Why);

    /// <summary>"owner", "Ian", or an email address; anything else is refused so an action never lands on nobody.</summary>
    public static bool IsActionOwner(string? owner) =>
        owner is not null && (owner.Equals("owner", StringComparison.OrdinalIgnoreCase) || owner.Equals("Ian", StringComparison.OrdinalIgnoreCase)
            || (!owner.Contains(' ') && owner.IndexOf('@') > 0 && owner.IndexOf('@') == owner.LastIndexOf('@')));

    /// <summary>Parses and validates a session's report. Every problem is reported; nothing is accepted with errors.</summary>
    public static (RfpReviewReport? Report, IReadOnlyList<string> Errors) ParseReport(string json)
    {
        ReportFile? f;
        try { f = JsonSerializer.Deserialize<ReportFile>(json, Json); }
        catch (JsonException ex) { return (null, new[] { "Not valid JSON: " + ex.Message }); }
        if (f is null) return (null, new[] { "The report is empty." });

        var errors = new List<string>();
        if (f.Outcome is null || !BdDoctrine.Outcomes.Contains(f.Outcome)) errors.Add($"outcome must be one of: {string.Join(", ", BdDoctrine.Outcomes)}.");
        if (f.Recommendation is null || !Recommendations.Contains(f.Recommendation)) errors.Add("recommendation must be go, no-go or decide.");
        if (string.IsNullOrWhiteSpace(f.Summary)) errors.Add("summary is required: what this tender is, in plain words.");
        if (string.IsNullOrWhiteSpace(f.Reasons)) errors.Add("reasons is required: why go, no-go or decide.");
        var questions = new List<RfpQuestion>();
        var qs = f.Questions ?? new List<QuestionFile>();
        if (qs.Count > 40) errors.Add("at most 40 questions.");
        for (var n = 0; n < qs.Count; n++)
        {
            var q = qs[n];
            if (string.IsNullOrWhiteSpace(q?.Question) || q.Question.Length > 1000) errors.Add($"question {n + 1}: question is required (1000 characters at most).");
            else if (string.IsNullOrWhiteSpace(q.Why) || q.Why.Length > 1000) errors.Add($"question {n + 1}: why is required (1000 characters at most).");
            else questions.Add(new RfpQuestion(q.Question.Trim(), q.Why.Trim(), string.IsNullOrWhiteSpace(q.Ref) ? null : Clip(q.Ref.Trim(), 200)));
        }
        if (f.QuestionsDeadline is { Length: > 100 }) errors.Add("questionsDeadline is longer than 100 characters.");
        if (f.QuestionsTo is { Length: > 320 }) errors.Add("questionsTo is longer than 320 characters.");
        if (f.FilesRead is { Length: > 2000 }) errors.Add("filesRead is longer than 2000 characters.");
        var checklist = f.Checklist ?? new List<PursuitRequirementInput>();
        try { PursuitRequirementValidation.Validate(checklist); }
        catch (ArgumentException ex) { errors.Add("checklist " + ex.Message); }

        // A review is for acting on: at least one action, each with an owner and a date.
        var actions = new List<RfpAction>();
        var acts = f.Actions ?? new List<ActionFile>();
        if (acts.Count is 0 or > 12) errors.Add("actions: give 1 to 12, in the order to do them (a no-go still has one: archive the pursuit).");
        for (var n = 0; n < acts.Count; n++)
        {
            var a = acts[n];
            var at = $"action {n + 1}";
            if (string.IsNullOrWhiteSpace(a?.Action) || a.Action.Length > 300) errors.Add($"{at}: action is required (300 characters at most).");
            else if (!IsActionOwner(a.Owner?.Trim())) errors.Add($"{at}: owner must be \"owner\", \"Ian\" or an email address.");
            else if (!DateOnly.TryParseExact(a.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due)) errors.Add($"{at}: due must be yyyy-MM-dd.");
            else if (string.IsNullOrWhiteSpace(a.Why) || a.Why.Length > 500) errors.Add($"{at}: why is required (500 characters at most).");
            else actions.Add(new RfpAction(a.Action.Trim(), a.Owner!.Trim(), due, a.Why.Trim()));
        }

        return errors.Count > 0
            ? (null, errors)
            : (new RfpReviewReport(f.Outcome!, f.Recommendation!, f.Summary!.Trim(), f.Reasons!.Trim(), questions,
                Blank(f.QuestionsDeadline), Blank(f.QuestionsTo), Blank(f.FilesRead), checklist, actions), Array.Empty<string>());
    }

    public static string ActionsJson(IReadOnlyList<RfpAction> actions) => JsonSerializer.Serialize(actions, Json);

    public static IReadOnlyList<RfpAction> ReadActions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<RfpAction>();
        try { return JsonSerializer.Deserialize<List<RfpAction>>(json, Json) ?? new List<RfpAction>(); }
        catch (JsonException) { return Array.Empty<RfpAction>(); }
    }

    /// <summary>Who an action lands on: the pursuit's owner, Ian's mailbox, or the address given.</summary>
    public static string ResolveOwner(string owner, string? pursuitOwner) =>
        owner.Equals("owner", StringComparison.OrdinalIgnoreCase) ? (string.IsNullOrWhiteSpace(pursuitOwner) ? IanMailbox : pursuitOwner!)
        : owner.Equals("Ian", StringComparison.OrdinalIgnoreCase) ? IanMailbox
        : owner.ToLowerInvariant();

    public const string IanMailbox = "ilalonde@korstructural.com";

    public static string QuestionsJson(IReadOnlyList<RfpQuestion> questions) => JsonSerializer.Serialize(questions, Json);

    public static IReadOnlyList<RfpQuestion> ReadQuestions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<RfpQuestion>();
        try { return JsonSerializer.Deserialize<List<RfpQuestion>>(json, Json) ?? new List<RfpQuestion>(); }
        catch (JsonException) { return Array.Empty<RfpQuestion>(); }
    }

    /// <summary>
    /// The pursuit's status line, in plain words, so whoever is looking knows what is happening without asking: waiting for the
    /// documents, a sign-in needed, the documents in, Claude reading them, or the review ready. Null for a pursuit that is not
    /// a portal tender and has no review.
    /// </summary>
    public static string? Describe(PursuitReviewStatus s, DateTimeOffset now)
    {
        // One short line each; the files themselves are listed under Files, so the line never repeats them.
        if (s.RunId is not null && s.RunCreatedAtUtc is { } opened && s.RunReportedAtUtc is null)
            return now - opened > TimeSpan.FromHours(6)
                ? $"Claude started reading the RFP {opened.ToLocalTime():ddd h:mm tt} but has not reported. Run it again from More."
                : $"Claude is reading the RFP, started {opened.ToLocalTime():h:mm tt}. The review appears here when it's done.";
        if (s.Reviews > 0) return "Claude's RFP review is ready.";
        if (s.PortalFiles > 0)
            return $"{s.PortalFiles} document{(s.PortalFiles == 1 ? "" : "s")} in from {s.Portal ?? "the portal"}. Claude's review starts by itself on Ian's PC.";
        if (s.Portal is null) return null;
        if (s.Portal == "APC" && s.PortalSessionError is not null)
            return "APC sign-in needed before the documents can download: More ▸ Sign in to APC.";
        var every = s.Portal == "APC" ? "every 10 minutes" : "every hour";
        return s.LastFetchAtUtc is { } at && s.LastFetchSucceeded == false
            ? $"The last {s.Portal} download failed at {at.ToLocalTime():h:mm tt} ({s.LastFetchNote}). Trying again {every}."
            : $"Waiting for the documents from {s.Portal}, checked {every}.";
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Review runs and their reports (migration 377). Nothing here sends anything to anyone.</summary>
public sealed class SqlPursuitReviewStore
{
    private readonly string _db;

    public SqlPursuitReviewStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _db = connectionString;
    }

    /// <summary>
    /// Pursuits whose portal documents are in and that have never had a review run: what the app opens by itself. Drafting
    /// only (a submitted bid needs no reading), one run per pursuit ever; "Run the review again" is a person's choice.
    /// </summary>
    internal const string DueSql = @"
SELECT e.Id
FROM opportunities.CrmEngagements e
WHERE e.Stage = 1
  AND EXISTS (SELECT 1 FROM opportunities.OpportunityFiles f WHERE f.EngagementId = e.Id AND f.UploadedBy IN (N'apc', N'bids-and-tenders'))
  AND NOT EXISTS (SELECT 1 FROM opportunities.BdPromptRun r WHERE r.EngagementId = e.Id AND r.Kind = N'rfp')
  AND NOT EXISTS (SELECT 1 FROM opportunities.PursuitReview v WHERE v.EngagementId = e.Id)
ORDER BY e.Id;";

    public async Task<IReadOnlyList<long>> ListDueAsync(CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(DueSql, con) { CommandTimeout = 30 };
        var ids = new List<long>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) ids.Add(r.GetInt64(0));
        return ids;
    }

    /// <summary>
    /// Records a review run as it opens. With <paramref name="onlyIfNone"/> it is also the claim: null when the pursuit already
    /// has a run, so two PCs with the app open never both start one.
    /// </summary>
    public async Task<long?> StartRunAsync(long engagementId, string subject, string createdBy, byte[] tokenSha256, bool onlyIfNone, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
INSERT INTO opportunities.BdPromptRun (Kind, Role, Subject, Question, CreatedBy, PromptSha256, TokenSha256, EngagementId)
OUTPUT INSERTED.Id
SELECT N'rfp', N'BD', @subject, NULL, @by, @prompt, @token, @e
WHERE @force = 1 OR NOT EXISTS (SELECT 1 FROM opportunities.BdPromptRun WITH (UPDLOCK, HOLDLOCK) WHERE EngagementId = @e AND Kind = N'rfp');", con);
        cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@subject", SqlDbType.NVarChar, 200).Value = subject;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = createdBy;
        cmd.Parameters.Add("@prompt", SqlDbType.Binary, 32).Value = new byte[32];
        cmd.Parameters.Add("@token", SqlDbType.Binary, 32).Value = tokenSha256;
        cmd.Parameters.Add("@force", SqlDbType.Bit).Value = !onlyIfNone;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is { } id and not DBNull ? Convert.ToInt64(id, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// A session's report. Refused (null) when the token does not match a review run. Stores the review (a resend of the same
    /// run replaces it) and returns the run's pursuit, so the caller can file the checklist.
    /// </summary>
    public async Task<long?> ReportAsync(long runId, string token, RfpReviewReport report, string? checklistNote, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        long engagementId;
        string by;
        await using (var check = new SqlCommand(@"SELECT EngagementId, CreatedBy FROM opportunities.BdPromptRun WITH (UPDLOCK)
WHERE Id = @id AND TokenSha256 = @token AND Kind = N'rfp' AND EngagementId IS NOT NULL;", con, tx))
        {
            check.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
            check.Parameters.Add("@token", SqlDbType.Binary, 32).Value = BdDoctrine.Sha(token);
            await using var r = await check.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
            engagementId = r.GetInt64(0);
            by = r.GetString(1);
        }

        await using (var update = new SqlCommand(@"
UPDATE opportunities.BdPromptRun SET Outcome = @outcome, Answer = @summary, OutcomeAtUtc = sysdatetimeoffset() WHERE Id = @id;
DELETE FROM opportunities.PursuitReview WHERE PromptRunId = @id;", con, tx))
        {
            update.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
            update.Parameters.Add("@outcome", SqlDbType.NVarChar, 16).Value = report.Outcome;
            update.Parameters.Add("@summary", SqlDbType.NVarChar, -1).Value = report.Summary;
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await InsertAsync(con, tx, engagementId, runId, report, checklistNote, $"Claude (opened by {by})", ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return engagementId;
    }

    /// <summary>A review written outside a run (one done in a session before the pursuit had a run), kept the same way.</summary>
    public async Task AddAsync(long engagementId, RfpReviewReport report, string? checklistNote, string reportedBy, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        await InsertAsync(con, tx, engagementId, null, report, checklistNote, reportedBy, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertAsync(SqlConnection con, SqlTransaction tx, long engagementId, long? runId, RfpReviewReport report,
        string? checklistNote, string reportedBy, CancellationToken ct)
    {
        await using var insert = new SqlCommand(@"
INSERT INTO opportunities.PursuitReview
    (EngagementId, PromptRunId, Recommendation, Summary, Reasons, QuestionsJson, QuestionsDeadline, QuestionsTo, FilesRead, ChecklistNote, ReportedBy, ActionsJson)
VALUES (@e, @run, @rec, @summary, @reasons, @questions, @deadline, @to, @files, @note, @by, @actions);", con, tx);
        insert.Parameters.Add("@actions", SqlDbType.NVarChar, -1).Value = PursuitRfpReview.ActionsJson(report.Actions);
        insert.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
        insert.Parameters.Add("@run", SqlDbType.BigInt).Value = (object?)runId ?? DBNull.Value;
        insert.Parameters.Add("@rec", SqlDbType.NVarChar, 16).Value = report.Recommendation;
        insert.Parameters.Add("@summary", SqlDbType.NVarChar, -1).Value = report.Summary;
        insert.Parameters.Add("@reasons", SqlDbType.NVarChar, -1).Value = report.Reasons;
        insert.Parameters.Add("@questions", SqlDbType.NVarChar, -1).Value = PursuitRfpReview.QuestionsJson(report.Questions);
        insert.Parameters.Add("@deadline", SqlDbType.NVarChar, 100).Value = (object?)report.QuestionsDeadline ?? DBNull.Value;
        insert.Parameters.Add("@to", SqlDbType.NVarChar, 320).Value = (object?)report.QuestionsTo ?? DBNull.Value;
        insert.Parameters.Add("@files", SqlDbType.NVarChar, 2000).Value = (object?)report.FilesRead ?? DBNull.Value;
        insert.Parameters.Add("@note", SqlDbType.NVarChar, 400).Value = (object?)checklistNote ?? DBNull.Value;
        insert.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = reportedBy.Length <= 150 ? reportedBy : reportedBy[..150];
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The newest review of a pursuit, or null.</summary>
    public async Task<PursuitReviewRow?> LatestAsync(long engagementId, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
SELECT TOP (1) Id, EngagementId, PromptRunId, Recommendation, Summary, Reasons, QuestionsJson, QuestionsDeadline, QuestionsTo, FilesRead,
       ChecklistNote, ReportedBy, ReportedAtUtc, ActionsJson
FROM opportunities.PursuitReview WHERE EngagementId = @e ORDER BY ReportedAtUtc DESC, Id DESC;", con);
        cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new PursuitReviewRow(r.GetInt64(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5),
            PursuitRfpReview.ReadQuestions(S(6)), S(7), S(8), S(9), S(10), r.GetString(11), r.GetDateTimeOffset(12), PursuitRfpReview.ReadActions(S(13)));
    }

    /// <summary>
    /// Puts a review's actions where people work: each becomes an Open ask on its owner's My day (a resend of the same review
    /// withdraws the asks it made before), and the first becomes the pursuit's next step when the pursuit has none or its next
    /// step is overdue. Returns what was filed, for the CLI to print.
    /// </summary>
    public async Task<(int Asks, bool NextStepSet, IReadOnlyList<string> Owners)> FileActionsAsync(long engagementId, string askedBy,
        IReadOnlyList<RfpAction> actions, DateTimeOffset now, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        string? owner;
        long? org;
        string? nextNote;
        DateTimeOffset? nextDue;
        await using (var read = new SqlCommand("SELECT OwnerStaffId, BuyerCanonicalOrgId, NextActionNote, NextActionDueUtc FROM opportunities.CrmEngagements WITH (UPDLOCK) WHERE Id = @e;", con, tx))
        {
            read.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
            await using var r = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false)) throw new InvalidOperationException($"Pursuit {engagementId} does not exist.");
            owner = r.IsDBNull(0) ? null : r.GetString(0);
            org = r.IsDBNull(1) ? null : r.GetInt64(1);
            nextNote = r.IsDBNull(2) ? null : r.GetString(2);
            nextDue = r.IsDBNull(3) ? null : r.GetDateTimeOffset(3);
        }

        await using (var withdraw = new SqlCommand(@"UPDATE opportunities.BdAsk SET Status = N'Withdrawn', ClosedAtUtc = sysdatetimeoffset(),
    ClosedBy = @by, ClosedNote = N'Replaced by a newer report of the same review.'
WHERE EngagementId = @e AND AskedBy = @by AND Status = N'Open';", con, tx))
        {
            withdraw.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
            withdraw.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = askedBy;
            await withdraw.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var owners = new List<string>();
        foreach (var a in actions)
        {
            var to = PursuitRfpReview.ResolveOwner(a.Owner, owner);
            owners.Add(to);
            await using var insert = new SqlCommand(@"INSERT INTO opportunities.BdAsk (ToStaffKey, EngagementId, CanonicalOrgId, Ask, WhyNow, Minutes, DueUtc, AskedBy)
VALUES (@to, @e, @org, @ask, @why, 30, @due, @by);", con, tx);
            insert.Parameters.Add("@to", SqlDbType.NVarChar, 150).Value = to;
            insert.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
            insert.Parameters.Add("@org", SqlDbType.BigInt).Value = (object?)org ?? DBNull.Value;
            insert.Parameters.Add("@ask", SqlDbType.NVarChar, 500).Value = a.Action.Length <= 500 ? a.Action : a.Action[..500];
            insert.Parameters.Add("@why", SqlDbType.NVarChar, 1000).Value = a.Why;
            insert.Parameters.Add("@due", SqlDbType.DateTimeOffset).Value = DueAtEndOfDay(a.Due);
            insert.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = askedBy;
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // The pursuit's own next step: only filled when empty or overdue, so a person's own next step is never overwritten.
        var nextStepSet = false;
        if (actions.Count > 0 && (string.IsNullOrWhiteSpace(nextNote) || nextDue is null || nextDue < now))
        {
            var first = actions[0];
            await using var next = new SqlCommand(@"UPDATE opportunities.CrmEngagements SET NextActionNote = @note, NextActionDueUtc = @due,
    UpdatedAtUtc = sysdatetimeoffset(), UpdatedBy = @by WHERE Id = @e;", con, tx);
            next.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
            next.Parameters.Add("@note", SqlDbType.NVarChar, 500).Value = first.Action.Length <= 500 ? first.Action : first.Action[..500];
            next.Parameters.Add("@due", SqlDbType.DateTimeOffset).Value = DueAtEndOfDay(first.Due);
            next.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = askedBy;
            await next.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            nextStepSet = true;
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return (actions.Count, nextStepSet, owners);
    }

    /// <summary>
    /// When a pursuit changes hands, the open review asks that were the OWNER's (action owner "owner") follow it to the new
    /// owner; Ian's own actions ("Ian") and anyone named stay where they are. Matched by the review's own action text, so a
    /// person's hand-made asks are never moved. Returns how many moved.
    /// </summary>
    public async Task<int> MoveOwnerActionsAsync(long engagementId, string fromOwner, string toOwner, IReadOnlyList<RfpAction> actions, CancellationToken ct)
    {
        var mine = actions.Where(a => a.Owner.Equals("owner", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Action.Length <= 500 ? a.Action : a.Action[..500]).Distinct().ToList();
        if (mine.Count == 0 || string.Equals(fromOwner, toOwner, StringComparison.OrdinalIgnoreCase)) return 0;
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        var moved = 0;
        foreach (var text in mine)
        {
            await using var cmd = new SqlCommand(@"UPDATE opportunities.BdAsk SET ToStaffKey = @to
WHERE EngagementId = @e AND Status = N'Open' AND ToStaffKey = @from AND Ask = @ask AND AskedBy LIKE N'Claude (RFP review%';", con);
            cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
            cmd.Parameters.Add("@from", SqlDbType.NVarChar, 150).Value = fromOwner;
            cmd.Parameters.Add("@to", SqlDbType.NVarChar, 150).Value = toOwner;
            cmd.Parameters.Add("@ask", SqlDbType.NVarChar, 500).Value = text;
            moved += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return moved;
    }

    /// <summary>A due day is the end of that day in Alberta/BC working hours: 5 pm Mountain, so an action is not "overdue" at breakfast.</summary>
    private static DateTimeOffset DueAtEndOfDay(DateOnly due) => new(due.ToDateTime(new TimeOnly(17, 0)), TimeSpan.FromHours(-6));

    /// <summary>What the review prompt says about a pursuit, read live as the review opens. Null when the pursuit does not exist.</summary>
    public async Task<RfpReviewInput?> LoadInputAsync(long engagementId, string requestedBy, DateTimeOffset now, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
SELECT o.Name, o.BuyerName, o.BuyerContactName, o.BuyerContactEmail, o.SubmissionDeadlineUtc, e.Stage,
       (SELECT TOP (1) x.Url FROM opportunities.OpportunityObservations x WHERE x.OpportunityId = o.Id AND x.Url LIKE N'http%'
        ORDER BY x.IngestedAtUtc DESC, x.Id DESC),
       (SELECT COUNT(*) FROM opportunities.PursuitRequirement q WHERE q.EngagementId = e.Id)
FROM opportunities.CrmEngagements e JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
WHERE e.Id = @e;
SELECT Id, FileName, SizeBytes, UploadedBy, UploadedAtUtc FROM opportunities.OpportunityFiles WHERE EngagementId = @e ORDER BY Id;", con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        var stage = r.GetInt32(5) switch { 1 => "Drafting", 3 => "Submitted", 6 => "Won", 7 => "Lost", 8 => "Archived", var n => n.ToString(CultureInfo.InvariantCulture) };
        var head = (Name: r.GetString(0), Buyer: S(1), Contact: S(2), Email: S(3), Deadline: r.IsDBNull(4) ? (DateTimeOffset?)null : r.GetDateTimeOffset(4),
            Stage: stage, Url: S(6), Checklist: r.GetInt32(7));
        await r.NextResultAsync(ct).ConfigureAwait(false);
        var files = new List<RfpReviewFile>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            files.Add(new RfpReviewFile(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? 0 : r.GetInt64(2), r.GetString(3), r.GetDateTimeOffset(4)));
        return new RfpReviewInput(engagementId, head.Name, head.Buyer, head.Contact, head.Email, head.Deadline, null, head.Stage, head.Url,
            files, head.Checklist, requestedBy, now);
    }

    /// <summary>
    /// The live tender pursuits the BD Director's Today and Pursuits screens show: drafting or submitted, with a deadline from
    /// a week ago on, or a review; each with its owner's name and its latest review. One read, on window load and Refresh.
    /// </summary>
    internal const string BoardSql = @"
SELECT e.Id, COALESCE(o.Name, fwd.ProjectName), COALESCE(o.BuyerName, fwd.ProponentName, fwd.MunicipalityName), e.Stage, e.OwnerStaffId, who.DisplayName,
       o.SubmissionDeadlineUtc, e.NextActionNote, e.NextActionDueUtc,
       r.Recommendation, r.Reasons, r.QuestionsJson, r.QuestionsDeadline, r.ActionsJson, r.QuestionsTo,
       CAST(CASE WHEN run.Id IS NOT NULL AND run.OutcomeAtUtc IS NULL THEN 1 ELSE 0 END AS bit), e.OpportunityId, e.UpdatedAtUtc
FROM opportunities.CrmEngagements e
LEFT JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
-- A forward pursuit (a grabbed project, no tender yet) shows under its project's name.
OUTER APPLY (SELECT TOP (1) m.ProjectName, m.ProponentName, m.MunicipalityName
             FROM opportunities.CrmEngagementProjectLink l JOIN opportunities.MajorProjectsInventory m ON m.Id = l.MajorProjectsInventoryId
             WHERE l.EngagementId = e.Id AND l.MatchedBy = N'Grab') fwd
OUTER APPLY (SELECT TOP (1) s.DisplayName FROM opportunities.BdStaff s WHERE s.Mailbox = e.OwnerStaffId COLLATE DATABASE_DEFAULT) who
OUTER APPLY (SELECT TOP (1) v.Recommendation, v.Reasons, v.QuestionsJson, v.QuestionsDeadline, v.ActionsJson, v.QuestionsTo
             FROM opportunities.PursuitReview v WHERE v.EngagementId = e.Id ORDER BY v.ReportedAtUtc DESC, v.Id DESC) r
OUTER APPLY (SELECT TOP (1) x.Id, x.OutcomeAtUtc FROM opportunities.BdPromptRun x WHERE x.EngagementId = e.Id AND x.Kind = N'rfp' ORDER BY x.Id DESC) run
WHERE e.Stage IN (1, 3)
  -- A submitted bid stays until it is won or lost, however long ago it closed (audit C10, 2026-10-21).
  AND (o.Id IS NOT NULL AND (e.Stage = 3 OR r.Recommendation IS NOT NULL OR o.SubmissionDeadlineUtc >= DATEADD(day, -7, SYSDATETIMEOFFSET()))
       OR o.Id IS NULL AND e.Stage = 1 AND fwd.ProjectName IS NOT NULL)
ORDER BY COALESCE(o.SubmissionDeadlineUtc, '9999-12-31'), e.Id;";

    public async Task<IReadOnlyList<TenderBoardRow>> ListBoardAsync(CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(BoardSql, con) { CommandTimeout = 30 };
        var rows = new List<TenderBoardRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            DateTimeOffset? D(int i) => r.IsDBNull(i) ? null : r.GetDateTimeOffset(i);
            rows.Add(new TenderBoardRow(r.GetInt64(0), r.GetString(1), S(2), r.GetInt32(3), S(4), S(5), D(6), S(7), D(8), S(9), S(10),
                PursuitRfpReview.ReadQuestions(S(11)), S(12), PursuitRfpReview.ReadActions(S(13)), S(14), r.GetBoolean(15),
                r.IsDBNull(16) ? null : r.GetInt64(16), D(17)));
        }
        return rows;
    }

    internal const string StatusSql = @"
SELECT
    portal.Portal,
    (SELECT COUNT(*) FROM opportunities.OpportunityFiles f WHERE f.EngagementId = e.Id AND f.UploadedBy IN (N'apc', N'bids-and-tenders')),
    (SELECT MAX(f.UploadedAtUtc) FROM opportunities.OpportunityFiles f WHERE f.EngagementId = e.Id AND f.UploadedBy IN (N'apc', N'bids-and-tenders')),
    lastFetch.FetchedAtUtc, lastFetch.Succeeded, lastFetch.Note,
    CASE WHEN portal.Portal = N'APC' THEN (SELECT s.LastError FROM opportunities.PortalSession s WHERE s.Portal = N'APC') END,
    run.Id, run.CreatedAtUtc, run.CreatedBy, run.OutcomeAtUtc,
    (SELECT COUNT(*) FROM opportunities.PursuitReview v WHERE v.EngagementId = e.Id)
FROM opportunities.CrmEngagements e
OUTER APPLY (SELECT TOP (1) CASE WHEN x.Url LIKE N'https://purchasing.alberta.ca/posting/AB-%' THEN N'APC' ELSE N'Bids&Tenders' END AS Portal
             FROM opportunities.OpportunityObservations x
             WHERE x.OpportunityId = e.OpportunityId
               AND (x.Url LIKE N'https://purchasing.alberta.ca/posting/AB-%' OR x.Url LIKE N'%.bidsandtenders.ca/Module/Tenders/%/Tender/Detail/%')
             ORDER BY CASE WHEN x.Url LIKE N'%.bidsandtenders.ca/%' THEN 0 ELSE 1 END, x.IngestedAtUtc DESC) portal
OUTER APPLY (SELECT TOP (1) d.FetchedAtUtc, d.Succeeded, d.Note FROM opportunities.PursuitDocumentFetch d
             WHERE d.EngagementId = e.Id ORDER BY d.FetchedAtUtc DESC, d.Id DESC) lastFetch
OUTER APPLY (SELECT TOP (1) r.Id, r.CreatedAtUtc, r.CreatedBy, r.OutcomeAtUtc FROM opportunities.BdPromptRun r
             WHERE r.EngagementId = e.Id AND r.Kind = N'rfp' ORDER BY r.CreatedAtUtc DESC, r.Id DESC) run
WHERE e.Id = @e;";

    public async Task<PursuitReviewStatus?> StatusAsync(long engagementId, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(StatusSql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        DateTimeOffset? D(int i) => r.IsDBNull(i) ? null : r.GetDateTimeOffset(i);
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new PursuitReviewStatus(engagementId, S(0), r.GetInt32(1), D(2), D(3), r.IsDBNull(4) ? null : r.GetBoolean(4), S(5), S(6),
            r.IsDBNull(7) ? null : r.GetInt64(7), D(8), S(9), D(10), r.GetInt32(11));
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var con = new SqlConnection(_db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        return con;
    }
}
