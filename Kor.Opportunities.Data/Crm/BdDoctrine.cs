#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>One doctrine card (migration 362): a principle and its play, cited to the books.</summary>
public sealed record DoctrineCardRow(long Id, string Role, string Title, string Plain, string AppliesTo, string Principle, string Recognise,
    string Play, string? Warning, string Citations, string? Tags, long? SourceRunId, long? AmendsCardId, string Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? DecidedAtUtc, string? DecidedBy);

/// <summary>A card as a session proposes it in its report.</summary>
public sealed record DoctrineCardDraft(string Title, string Plain, string AppliesTo, string Principle, string Recognise, string Play,
    string? Warning, string Citations, string? Tags, long? Amends);

/// <summary>One Claude run started from the Director (migration 362).</summary>
public sealed record BdPromptRunRow(long Id, string Kind, string Role, string Subject, string? Question, string CreatedBy, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? OutcomeAtUtc, string? Outcome, string? Answer, int ProposedCards);

/// <summary>What a session reports back: its outcome, its answer in plain words, and any cards it proposes.</summary>
public sealed record BdPromptReport(string Outcome, string Answer, IReadOnlyList<DoctrineCardDraft> Cards);

/// <summary>The situation a prompt is about, in the terms a card's AppliesTo speaks.</summary>
public sealed record DoctrineContext(string Moment, IReadOnlyCollection<string> Situations, string? Stage = null, string? Region = null,
    string? Person = null, long? Org = null, string? Sector = null);

/// <summary>
/// Where a doctrine card applies, in a small closed vocabulary — the BD counterpart of NetworkOps' KnowledgeCards — so "how
/// to rescue a relationship that has gone quiet" reaches every later prompt about a pursuit going cold, and nothing else.
///
///   any                   every prompt
///   moment:&lt;m&gt;            hour | one-to-one | pursuit | person | ask | synthesis
///   situation:&lt;s&gt;         one of <see cref="Situations"/>
///   stage:&lt;s&gt;             Pending | Submitted
///   region:&lt;r&gt;            a CRM region (Vancouver/LowerMainland, VancouverIsland, Alberta, Okanagan-BcInterior, USA)
///   person:&lt;key&gt;          one person's mailbox key
///   org:&lt;id&gt;              one company (CanonicalOrg id)
///   sector:&lt;text&gt;         a sector word (health, residential, defence…)
///
/// Terms are comma-separated; a card applies when ANY term matches. Matching ignores case.
/// </summary>
public static class BdDoctrine
{
    public static readonly IReadOnlyList<string> Kinds = new[] { "any", "moment", "situation", "stage", "region", "person", "org", "sector" };
    public static readonly IReadOnlyList<string> Moments = new[] { "hour", "one-to-one", "pursuit", "person", "ask", "synthesis" };
    public static readonly IReadOnlyList<string> Situations = new[]
    {
        "going-cold", "never-touched", "late-action", "thin-references", "single-relationship", "fee-undercut", "rfp-response",
        "relationship-pursuit", "new-account", "first-meeting", "lost-pursuit", "won-pursuit", "quiet-market", "team-cadence",
    };
    public static readonly IReadOnlyList<string> Outcomes = new[] { "answered", "partly", "not-answered" };
    public const int MaxCardsPerRun = 150;

    /// <summary>Why an AppliesTo is refused, or null. Checked when a report arrives, so a typo cannot bank a card no prompt carries.</summary>
    public static string? Invalid(string? appliesTo)
    {
        var terms = Terms(appliesTo);
        if (terms.Count == 0) return "appliesTo is empty: say where it applies (any, moment:, situation:, stage:, region:, person:, org:, sector:)";
        foreach (var (kind, value) in terms)
        {
            if (!Kinds.Contains(kind)) return $"appliesTo term '{kind}' is not one of: {string.Join(", ", Kinds)}";
            if (kind != "any" && value.Length == 0) return $"appliesTo term '{kind}:' needs a value";
            if (kind == "moment" && !Moments.Contains(value, StringComparer.OrdinalIgnoreCase)) return $"moment '{value}' is not one of: {string.Join(", ", Moments)}";
            if (kind == "situation" && !Situations.Contains(value, StringComparer.OrdinalIgnoreCase)) return $"situation '{value}' is not one of: {string.Join(", ", Situations)}";
            if (kind == "org" && !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return $"org '{value}' must be a company id";
        }

        return null;
    }

    /// <summary>Whether a card concerns this context.</summary>
    public static bool AppliesTo(string appliesTo, DoctrineContext c) => Terms(appliesTo).Any(t => t.Kind switch
    {
        "any" => true,
        "moment" => string.Equals(t.Value, c.Moment, StringComparison.OrdinalIgnoreCase),
        "situation" => c.Situations.Contains(t.Value, StringComparer.OrdinalIgnoreCase),
        "stage" => string.Equals(t.Value, c.Stage, StringComparison.OrdinalIgnoreCase),
        "region" => string.Equals(t.Value, c.Region, StringComparison.OrdinalIgnoreCase),
        "person" => string.Equals(t.Value, c.Person, StringComparison.OrdinalIgnoreCase),
        "org" => c.Org is { } o && t.Value == o.ToString(CultureInfo.InvariantCulture),
        "sector" => c.Sector is { } s && s.Contains(t.Value, StringComparison.OrdinalIgnoreCase),
        _ => false,
    });

    private static List<(string Kind, string Value)> Terms(string? appliesTo) =>
        (appliesTo ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Split(':', 2))
            .Select(p => (p[0].Trim().ToLowerInvariant(), p.Length > 1 ? p[1].Trim() : ""))
            .ToList();

    /// <summary>A one-time report-back token and its SHA-256 (only the hash is stored).</summary>
    public static (string Token, byte[] Sha256) NewToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, Sha(token));
    }

    public static byte[] Sha(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text));

    private static readonly JsonSerializerOptions ReportJson = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private sealed record ReportFile(string? Outcome, string? Answer, List<CardFile>? Cards);
    private sealed record CardFile(string? Title, string? Plain, string? AppliesTo, string? Principle, string? Recognise, string? Play,
        string? Warning, string? Citations, string? Tags, long? Amends);

    /// <summary>Parses and validates a session's report. Every problem is reported; nothing is accepted with errors.</summary>
    public static (BdPromptReport? Report, IReadOnlyList<string> Errors) ParseReport(string json)
    {
        ReportFile? f;
        try { f = JsonSerializer.Deserialize<ReportFile>(json, ReportJson); }
        catch (JsonException ex) { return (null, new[] { "Not valid JSON: " + ex.Message }); }
        if (f is null) return (null, new[] { "The report is empty." });

        var errors = new List<string>();
        if (f.Outcome is null || !Outcomes.Contains(f.Outcome)) errors.Add($"outcome must be one of: {string.Join(", ", Outcomes)}.");
        if (string.IsNullOrWhiteSpace(f.Answer)) errors.Add("answer is required: what you found, in plain words.");
        var cards = f.Cards ?? new List<CardFile>();
        if (cards.Count > MaxCardsPerRun) errors.Add($"at most {MaxCardsPerRun} cards per run.");
        var drafts = new List<DoctrineCardDraft>();
        for (var i = 0; i < cards.Count; i++)
        {
            var c = cards[i];
            var at = $"card {i + 1}";
            static bool Has(string? s, int max) => !string.IsNullOrWhiteSpace(s) && s.Length <= max;
            if (!Has(c.Title, 200)) errors.Add($"{at}: title is required (200 characters at most).");
            if (!Has(c.Plain, 1000)) errors.Add($"{at}: plain is required: what this card is and what accepting it does (1000 at most).");
            if (Invalid(c.AppliesTo) is { } bad) errors.Add($"{at}: {bad}.");
            else if (c.AppliesTo!.Length > 400) errors.Add($"{at}: appliesTo is longer than 400 characters.");
            foreach (var (name, value) in new[] { ("principle", c.Principle), ("recognise", c.Recognise), ("play", c.Play), ("citations", c.Citations) })
                if (!Has(value, 2000)) errors.Add($"{at}: {name} is required (2000 characters at most).");
            if (c.Warning is { Length: > 2000 }) errors.Add($"{at}: warning is longer than 2000 characters.");
            if (c.Tags is { Length: > 400 }) errors.Add($"{at}: tags is longer than 400 characters.");
            if (errors.Count == 0)
                drafts.Add(new DoctrineCardDraft(c.Title!.Trim(), c.Plain!.Trim(), c.AppliesTo!.Trim(), c.Principle!.Trim(), c.Recognise!.Trim(),
                    c.Play!.Trim(), string.IsNullOrWhiteSpace(c.Warning) ? null : c.Warning.Trim(), c.Citations!.Trim(),
                    string.IsNullOrWhiteSpace(c.Tags) ? null : c.Tags.Trim(), c.Amends));
        }

        return errors.Count > 0 ? (null, errors) : (new BdPromptReport(f.Outcome!, f.Answer!.Trim(), drafts), Array.Empty<string>());
    }
}

/// <summary>Runs and doctrine cards. Nothing here sends anything to anyone.</summary>
public interface IBdDoctrineStore
{
    /// <summary>Records a run as it is opened; returns its id. Only hashes of the prompt and the token are stored.</summary>
    Task<long> StartRunAsync(string kind, string role, string subject, string? question, string createdBy, byte[] promptSha256, byte[] tokenSha256, CancellationToken ct);
    /// <summary>The hash of the prompt as composed (it carries the run id, so it is written after the run exists). Only before a report.</summary>
    Task SetPromptShaAsync(long runId, byte[] promptSha256, CancellationToken ct);
    /// <summary>
    /// A session's report. Refused (false) when the token does not match the run. A second report replaces the first: the
    /// run's outcome and answer, and its still-proposed cards. Cards Ian already decided are never touched.
    /// </summary>
    Task<bool> ReportAsync(long runId, string token, BdPromptReport report, CancellationToken ct);
    Task<IReadOnlyList<BdPromptRunRow>> ListRunsAsync(int take, CancellationToken ct);
    Task<IReadOnlyList<DoctrineCardRow>> ListCardsAsync(string role, CancellationToken ct);
    /// <summary>Accepts or rejects a PROPOSED card. Accepting an amendment retires the card it amends. False when it was not proposed.</summary>
    Task<bool> DecideCardAsync(long cardId, bool accept, string by, CancellationToken ct);
}

public sealed class SqlBdDoctrineStore : IBdDoctrineStore
{
    private readonly string _connectionString;

    public SqlBdDoctrineStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<long> StartRunAsync(string kind, string role, string subject, string? question, string createdBy, byte[] promptSha256, byte[] tokenSha256, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"INSERT INTO opportunities.BdPromptRun (Kind, Role, Subject, Question, CreatedBy, PromptSha256, TokenSha256)
OUTPUT INSERTED.Id VALUES (@kind, @role, @subject, @question, @by, @prompt, @token);", con);
        Text(cmd, "@kind", 16, kind); Text(cmd, "@role", 20, role); Text(cmd, "@subject", 200, Clip(subject, 200));
        Text(cmd, "@question", 2000, question is null ? null : Clip(question, 2000)); Text(cmd, "@by", 150, createdBy);
        cmd.Parameters.Add("@prompt", SqlDbType.Binary, 32).Value = promptSha256;
        cmd.Parameters.Add("@token", SqlDbType.Binary, 32).Value = tokenSha256;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task SetPromptShaAsync(long runId, byte[] promptSha256, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("UPDATE opportunities.BdPromptRun SET PromptSha256 = @sha WHERE Id = @id AND OutcomeAtUtc IS NULL;", con);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@sha", SqlDbType.Binary, 32).Value = promptSha256;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ReportAsync(long runId, string token, BdPromptReport report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        string role;
        await using (var check = new SqlCommand("SELECT Role FROM opportunities.BdPromptRun WITH (UPDLOCK) WHERE Id = @id AND TokenSha256 = @token;", con, tx))
        {
            check.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
            check.Parameters.Add("@token", SqlDbType.Binary, 32).Value = BdDoctrine.Sha(token);
            if (await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string r) return false;
            role = r;
        }

        await using (var update = new SqlCommand(@"
UPDATE opportunities.BdPromptRun SET Outcome = @outcome, Answer = @answer, OutcomeAtUtc = sysdatetimeoffset() WHERE Id = @id;
DELETE FROM opportunities.DoctrineCard WHERE SourceRunId = @id AND Status = N'proposed';", con, tx))
        {
            update.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
            Text(update, "@outcome", 16, report.Outcome);
            update.Parameters.Add("@answer", SqlDbType.NVarChar, -1).Value = report.Answer;
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var c in report.Cards)
        {
            await using var insert = new SqlCommand(@"INSERT INTO opportunities.DoctrineCard
    (Role, Title, Plain, AppliesTo, Principle, Recognise, Play, Warning, Citations, Tags, SourceRunId, AmendsCardId)
SELECT @role, @title, @plain, @applies, @principle, @recognise, @play, @warning, @citations, @tags, @run,
       (SELECT Id FROM opportunities.DoctrineCard WHERE Id = @amends AND Role = @role);", con, tx);
            Text(insert, "@role", 20, role); Text(insert, "@title", 200, c.Title); Text(insert, "@plain", 1000, c.Plain);
            Text(insert, "@applies", 400, c.AppliesTo); Text(insert, "@principle", 2000, c.Principle); Text(insert, "@recognise", 2000, c.Recognise);
            Text(insert, "@play", 2000, c.Play); Text(insert, "@warning", 2000, c.Warning); Text(insert, "@citations", 2000, c.Citations);
            Text(insert, "@tags", 400, c.Tags);
            insert.Parameters.Add("@run", SqlDbType.BigInt).Value = runId;
            insert.Parameters.Add("@amends", SqlDbType.BigInt).Value = (object?)c.Amends ?? DBNull.Value;
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<BdPromptRunRow>> ListRunsAsync(int take, CancellationToken ct)
    {
        var rows = new List<BdPromptRunRow>();
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
SELECT TOP (@take) r.Id, r.Kind, r.Role, r.Subject, r.Question, r.CreatedBy, r.CreatedAtUtc, r.OutcomeAtUtc, r.Outcome, r.Answer,
       (SELECT COUNT(*) FROM opportunities.DoctrineCard c WHERE c.SourceRunId = r.Id AND c.Status = N'proposed')
FROM opportunities.BdPromptRun r ORDER BY r.CreatedAtUtc DESC, r.Id DESC;", con);
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = Math.Clamp(take, 1, 500);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new BdPromptRunRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Opt(r, 4), r.GetString(5), r.GetDateTimeOffset(6),
                r.IsDBNull(7) ? null : r.GetDateTimeOffset(7), Opt(r, 8), Opt(r, 9), r.GetInt32(10)));
        return rows;
    }

    public async Task<IReadOnlyList<DoctrineCardRow>> ListCardsAsync(string role, CancellationToken ct)
    {
        var rows = new List<DoctrineCardRow>();
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
SELECT Id, Role, Title, Plain, AppliesTo, Principle, Recognise, Play, Warning, Citations, Tags, SourceRunId, AmendsCardId, Status,
       CreatedAtUtc, DecidedAtUtc, DecidedBy
FROM opportunities.DoctrineCard WHERE Role = @role
ORDER BY CASE Status WHEN N'proposed' THEN 0 WHEN N'accepted' THEN 1 ELSE 2 END, CreatedAtUtc, Id;", con);
        Text(cmd, "@role", 20, role);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new DoctrineCardRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                r.GetString(7), Opt(r, 8), r.GetString(9), Opt(r, 10), r.IsDBNull(11) ? null : r.GetInt64(11), r.IsDBNull(12) ? null : r.GetInt64(12),
                r.GetString(13), r.GetDateTimeOffset(14), r.IsDBNull(15) ? null : r.GetDateTimeOffset(15), Opt(r, 16)));
        return rows;
    }

    public async Task<bool> DecideCardAsync(long cardId, bool accept, string by, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(by)) throw new ArgumentException("Who decided is required.", nameof(by));
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
DECLARE @amends BIGINT;
UPDATE opportunities.DoctrineCard SET Status = @status, DecidedAtUtc = sysdatetimeoffset(), DecidedBy = @by, @amends = AmendsCardId
WHERE Id = @id AND Status = N'proposed';
IF @@ROWCOUNT = 0 BEGIN SELECT 0; RETURN; END;
IF @status = N'accepted' AND @amends IS NOT NULL
    UPDATE opportunities.DoctrineCard SET Status = N'retired', DecidedAtUtc = sysdatetimeoffset(), DecidedBy = @by
    WHERE Id = @amends AND Status = N'accepted';
SELECT 1;", con, tx);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = cardId;
        Text(cmd, "@status", 16, accept ? "accepted" : "rejected");
        Text(cmd, "@by", 150, by);
        var ok = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) == 1;
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return ok;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    private static void Text(SqlCommand cmd, string name, int size, string? value) => cmd.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;
    private static string? Opt(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
