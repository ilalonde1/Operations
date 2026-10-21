#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Portals;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Stores the documents of every pursuit on an Alberta Purchasing Connection posting in the pursuit itself
/// (opportunities.OpportunityFiles), with the session a person signed in to once from the app (opportunities.PortalSession).
///
/// Every run first RENEWS that session, whether or not anything is due, so it never goes idle; the renewed refresh token is
/// stored back. A refused renewal is recorded on the session row (the app shows "sign in to APC again") and nothing is fetched.
/// Downloading adds KOR to the posting's public interested list, so only PURSUED tenders are fetched: a CRM pursuit, live
/// (stage 1 or 3). A pursuit whose package already came from Bids&amp;Tenders (the same tender on both portals, e.g. Leduc) is
/// skipped. Fetched once, then at most daily until the deadline so addenda arrive; a file already on the pursuit with the same
/// name is not stored twice.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class ApcDocumentJob : IJob
{
    internal const string Portal = ApcPortal.PortalKey;
    private const string UploadedBy = "apc";

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<ApcDocumentJob> _logger;
    private readonly IHttpClientFactory _http;

    public ApcDocumentJob(IOptions<OpportunitiesWorkerOptions> options, ILogger<ApcDocumentJob> logger, IHttpClientFactory http)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.ApcDocumentsEnabled)
        {
            _logger.LogInformation("{Job}: disabled", nameof(ApcDocumentJob));
            return;
        }

        var ct = context.CancellationToken;
        var db = opt.OpportunitiesDb!;
        var sessions = new SqlPortalSessionStore(db);
        var session = await sessions.GetAsync(Portal, ct).ConfigureAwait(false);
        if (session is null)
        {
            context.Result = "Nobody has signed in to APC from the app yet";
            return;
        }

        if (session.LastError is not null)
        {
            context.Result = "APC sign-in needed: " + session.LastError;
            return;
        }

        var apc = new ApcPortal(_http.CreateClient(nameof(ApcDocumentJob)));
        ApcTokens tokens;
        try
        {
            tokens = await apc.RenewAsync(session.RefreshToken, ct).ConfigureAwait(false);
            await sessions.SaveRenewalAsync(Portal, tokens.RefreshToken, tokens.RefreshExpiresUtc, ct).ConfigureAwait(false);
        }
        catch (ApcSignInExpiredException ex)
        {
            await sessions.SaveErrorAsync(Portal, ex.Message, ct).ConfigureAwait(false);
            _logger.LogWarning("{Job}: {Message}", nameof(ApcDocumentJob), ex.Message);
            context.Result = "APC sign-in needed: renewal refused";
            return;
        }

        var targets = await LoadTargetsAsync(db, ct).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            context.Result = $"Session renewed (good until {tokens.RefreshExpiresUtc?.ToString("u") ?? "no idle limit"}); no pursued APC tender due a fetch";
            return;
        }

        var files = new SqlPursuitFileStore(db);
        int fetched = 0, stored = 0, failed = 0;
        foreach (var t in targets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (tokens.AccessExpiresUtc < DateTimeOffset.UtcNow.AddMinutes(1))
                {
                    tokens = await apc.RenewAsync(tokens.RefreshToken, ct).ConfigureAwait(false);
                    await sessions.SaveRenewalAsync(Portal, tokens.RefreshToken, tokens.RefreshExpiresUtc, ct).ConfigureAwait(false);
                }

                var posting = await apc.ReadPostingAsync(t.PostingUrl, ct).ConfigureAwait(false);
                if (posting is null)
                {
                    failed++;
                    await LogAsync(db, t.EngagementId, false, 0, 0, "The posting feed had no opportunity", ct).ConfigureAwait(false);
                    continue;
                }

                var existing = await files.ListByEngagementAsync(t.EngagementId, ct).ConfigureAwait(false);
                var added = 0;
                foreach (var d in posting.Documents)
                {
                    if (existing.Any(f => string.Equals(f.FileName, d.FileName, StringComparison.OrdinalIgnoreCase))) continue;
                    var bytes = await apc.DownloadAsync(posting, d, tokens.AccessToken, ct).ConfigureAwait(false);
                    SqlPursuitFileStore.ValidateLength(bytes.Length);
                    await using var content = new MemoryStream(bytes, writable: false);
                    await files.AddAsync(t.EngagementId, t.OpportunityId, d.FileName, content, bytes.Length,
                        BidsAndTendersDocumentJob.ContentType(d.FileName), UploadedBy, ct).ConfigureAwait(false);
                    added++;
                }

                fetched++;
                stored += added;
                await LogAsync(db, t.EngagementId, true, posting.Documents.Count, added, null, ct).ConfigureAwait(false);
                _logger.LogInformation("{Job}: pursuit {Id}: {Files} documents on the posting, {Added} stored", nameof(ApcDocumentJob), t.EngagementId, posting.Documents.Count, added);
            }
            catch (ApcSignInExpiredException ex)
            {
                await sessions.SaveErrorAsync(Portal, ex.Message, ct).ConfigureAwait(false);
                await LogAsync(db, t.EngagementId, false, 0, 0, ex.Message, ct).ConfigureAwait(false);
                failed++;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _logger.LogWarning(ex, "{Job}: pursuit {Id} fetch failed", nameof(ApcDocumentJob), t.EngagementId);
                await LogAsync(db, t.EngagementId, false, 0, 0, ex.GetType().Name + ": " + ex.Message, ct).ConfigureAwait(false);
            }
        }

        context.Result = $"pursuits={targets.Count}; fetched={fetched}; filesStored={stored}; failures={failed}";
        _logger.LogInformation("{Job}: {Result}", nameof(ApcDocumentJob), context.Result);
    }

    /// <summary>
    /// Live pursuits on an APC posting, not already supplied by Bids&amp;Tenders, that have never been fetched from APC or whose
    /// tender is still open and was last fetched over 20 hours ago.
    /// </summary>
    internal const string TargetsSql = @"
SELECT e.Id, e.OpportunityId, obs.Url
FROM opportunities.CrmEngagements e
JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
CROSS APPLY (SELECT TOP (1) x.Url FROM opportunities.OpportunityObservations x
             WHERE x.OpportunityId = o.Id AND x.Url LIKE N'https://purchasing.alberta.ca/posting/AB-%'
             ORDER BY x.IngestedAtUtc DESC, x.Id DESC) obs
OUTER APPLY (SELECT MAX(f.FetchedAtUtc) AS LastAt FROM opportunities.PursuitDocumentFetch f
             WHERE f.EngagementId = e.Id AND f.Portal = N'APC' AND f.Succeeded = 1) last
WHERE e.Stage IN (1, 3)
  AND NOT EXISTS (SELECT 1 FROM opportunities.PursuitDocumentFetch b
                  WHERE b.EngagementId = e.Id AND b.Portal = N'BIDSTENDERS' AND b.Succeeded = 1 AND b.FilesInPackage > 0)
  AND (last.LastAt IS NULL
       OR (o.SubmissionDeadlineUtc > SYSDATETIMEOFFSET() AND last.LastAt < DATEADD(hour, -20, SYSDATETIMEOFFSET())))
ORDER BY e.Id;";

    private static async Task<IReadOnlyList<Target>> LoadTargetsAsync(string db, CancellationToken ct)
    {
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(TargetsSql, con) { CommandTimeout = 30 };
        var list = new List<Target>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(new Target(r.GetInt64(0), r.GetInt64(1), r.GetString(2)));
        return list;
    }

    private static async Task LogAsync(string db, long engagementId, bool ok, int inPackage, int stored, string? note, CancellationToken ct)
    {
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
INSERT INTO opportunities.PursuitDocumentFetch (EngagementId, Portal, Succeeded, FilesInPackage, FilesStored, Note)
VALUES (@e, @p, @ok, @n, @s, @note);", con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@e", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 40).Value = Portal;
        cmd.Parameters.Add("@ok", SqlDbType.Bit).Value = ok;
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = inPackage;
        cmd.Parameters.Add("@s", SqlDbType.Int).Value = stored;
        cmd.Parameters.Add("@note", SqlDbType.NVarChar, 400).Value = (object?)(note is { Length: > 400 } ? note[..400] : note) ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private sealed record Target(long EngagementId, long OpportunityId, string PostingUrl);
}
