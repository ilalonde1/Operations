#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Portals;

/// <summary>
/// Alberta Purchasing Connection, signed in: the documents of a posting, downloaded the way the site's own page downloads them.
///
/// Measured 2026-10-21 from the site's own scripts and config (purchasing.alberta.ca/config/config.json):
/// <list type="bullet">
/// <item>Sign-in is Keycloak at access.alberta.ca, realm 41898876-…, public client <c>apc-app</c> (oidc-client-ts in the page).
/// Its sign-in page carries Cloudflare Turnstile, which refuses an automated browser, so a PERSON signs in once
/// (<see cref="ReadBrowserSession"/> reads what the page stored) and this class only ever RENEWS that session.</item>
/// <item>Every API call carries <c>Authorization: Bearer {access token}</c>.</item>
/// <item>A document is <c>GET /api/opportunity/{opportunity id}/document/{document id}/az-sas-uri?action=download&amp;intent=view-posting</c>,
/// which returns a time-limited Azure blob link; the bytes come from that link with no further sign-in.</item>
/// <item>The public feed <c>/api/opportunity/public/{year}/{number}</c> gives the opportunity id and the document ids, with no sign-in.</item>
/// </list>
/// Downloading adds the signed-in supplier to the posting's PUBLIC interested list, so only pursued tenders are fetched.
/// </summary>
public sealed class ApcPortal
{
    public const string PortalKey = "APC";
    public const string SiteUrl = "https://purchasing.alberta.ca/";
    internal const string ApiBase = "https://purchasing.alberta.ca/api";
    internal const string TokenEndpoint = "https://access.alberta.ca/auth/realms/41898876-5358-402d-aae5-eee17a413de5/protocol/openid-connect/token";
    internal const string ClientId = "apc-app";

    private static readonly Regex PostingRef = new(@"/posting/AB-(\d{4})-(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;

    public ApcPortal(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <summary>Renews a session: a fresh access token, and the refresh token to keep for the next renewal.</summary>
    public async Task<ApcTokens> RenewAsync(string refreshToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken,
            }),
        };
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            throw new ApcSignInExpiredException($"APC sign-in renewal refused (HTTP {(int)res.StatusCode}): {Trim(body, 200)}");
        }

        return ParseTokens(body, DateTimeOffset.UtcNow);
    }

    /// <summary>The posting's opportunity id and its live documents, from the public feed.</summary>
    public async Task<ApcPosting?> ReadPostingAsync(string postingUrl, CancellationToken ct)
    {
        if (PostingPath(postingUrl) is not { } path) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return ParsePosting(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    /// <summary>One document's bytes: the signed link first (needs the session), then the file from Azure.</summary>
    public async Task<byte[]> DownloadAsync(ApcPosting posting, ApcDocument document, string accessToken, CancellationToken ct)
    {
        var url = $"{ApiBase}/opportunity/{posting.OpportunityId}/document/{document.Id}/az-sas-uri?action=download&intent=view-posting";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"APC refused the download link for {document.FileName} (HTTP {(int)res.StatusCode}): {Trim(body, 200)}");
        }

        var blobUrl = ParseSasUri(body) ?? throw new HttpRequestException($"APC returned no download link for {document.FileName}: {Trim(body, 200)}");
        return await _http.GetByteArrayAsync(blobUrl, ct).ConfigureAwait(false);
    }

    /// <summary>Pure: a posting URL to its public-feed path, <c>/opportunity/public/{year}/{number}</c>; null for anything else.</summary>
    public static string? PostingPath(string postingUrl)
    {
        var m = PostingRef.Match(postingUrl ?? "");
        return m.Success ? $"/opportunity/public/{m.Groups[1].Value}/{int.Parse(m.Groups[2].Value)}" : null;
    }

    /// <summary>Pure: the token endpoint's answer. Lifetimes are turned into instants against <paramref name="now"/>.</summary>
    public static ApcTokens ParseTokens(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString() ?? throw new FormatException("No access_token.");
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(refresh)) throw new FormatException("The sign-in server returned no refresh_token.");
        DateTimeOffset? refreshExpires = root.TryGetProperty("refresh_expires_in", out var re) && re.ValueKind == JsonValueKind.Number && re.GetInt64() > 0
            ? now.AddSeconds(re.GetInt64())
            : null; // 0 = an offline token with no idle limit
        var accessExpires = root.TryGetProperty("expires_in", out var ae) && ae.ValueKind == JsonValueKind.Number ? now.AddSeconds(ae.GetInt64()) : now.AddMinutes(5);
        return new ApcTokens(access, refresh, accessExpires, refreshExpires);
    }

    /// <summary>Pure: the opportunity id and the live documents and addenda, in the feed's order.</summary>
    public static ApcPosting? ParsePosting(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("opportunity", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        if (!o.TryGetProperty("id", out var idEl) || !Guid.TryParse(idEl.GetString(), out var id)) return null;
        var docs = new List<ApcDocument>();
        var seen = new HashSet<Guid>();
        foreach (var list in new[] { "documents", "addendaDocuments" })
        {
            if (!o.TryGetProperty(list, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var d in arr.EnumerateArray())
            {
                if (d.TryGetProperty("deletedOnUtc", out var del) && del.ValueKind == JsonValueKind.String) continue;
                if (!d.TryGetProperty("id", out var di) || !Guid.TryParse(di.GetString(), out var docId) || !seen.Add(docId)) continue;
                docs.Add(new ApcDocument(docId, FileNameOf(d), d.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : null));
            }
        }

        return new ApcPosting(id, docs);
    }

    /// <summary>
    /// Pure: the file name the site itself saves a document as (its buildDocumentFilename): the title, with the stored file's
    /// extension added when the title lacks it.
    /// </summary>
    internal static string FileNameOf(JsonElement d)
    {
        var title = d.TryGetProperty("title", out var t) ? t.GetString()?.Trim() : null;
        var file = d.TryGetProperty("filename", out var f) ? f.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(title)) return string.IsNullOrEmpty(file) ? "document" : file;
        var dot = file?.LastIndexOf('.') ?? -1;
        var ext = dot >= 0 ? file![(dot + 1)..] : "";
        return ext.Length == 0 || title.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase) ? title : $"{title}.{ext}";
    }

    /// <summary>Pure: the az-sas-uri answer is a bare link, a JSON string, or an object carrying it.</summary>
    public static string? ParseSasUri(string body)
    {
        var text = (body ?? "").Trim();
        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return text;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String) return root.GetString();
            foreach (var name in new[] { "sasUri", "uri", "url" })
            {
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// Pure: what the APC page stored after a person signed in (oidc-client-ts keeps the user under a key
    /// <c>oidc.user:{authority}:apc-app</c>): the refresh token and who signed in. Null when nobody is signed in yet.
    /// </summary>
    public static ApcBrowserSession? ReadBrowserSession(string storedUserJson)
    {
        if (string.IsNullOrWhiteSpace(storedUserJson)) return null;
        using var doc = JsonDocument.Parse(storedUserJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("refresh_token", out var r) || string.IsNullOrEmpty(r.GetString())) return null;
        string? who = null;
        if (root.TryGetProperty("profile", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "email", "preferred_username", "name" })
            {
                if (p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                {
                    who = v.GetString();
                    break;
                }
            }
        }

        return new ApcBrowserSession(r.GetString()!, who);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}

public sealed record ApcTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresUtc, DateTimeOffset? RefreshExpiresUtc);

public sealed record ApcPosting(Guid OpportunityId, IReadOnlyList<ApcDocument> Documents);

public sealed record ApcDocument(Guid Id, string FileName, long? SizeBytes);

public sealed record ApcBrowserSession(string RefreshToken, string? Account);

/// <summary>The saved APC session can no longer be renewed: a person has to sign in again.</summary>
public sealed class ApcSignInExpiredException : Exception
{
    public ApcSignInExpiredException(string message) : base(message) { }
}

/// <summary>opportunities.PortalSession (migration 376): one saved sign-in per portal.</summary>
public sealed class SqlPortalSessionStore
{
    private readonly string _connectionString;

    public SqlPortalSessionStore(string connectionString)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString) ? throw new ArgumentException("Connection string required.", nameof(connectionString)) : connectionString;
    }

    public async Task<PortalSessionRow?> GetAsync(string portal, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
SELECT Account, RefreshToken, RefreshExpiresUtc, SignedInBy, SignedInAtUtc, LastRenewedAtUtc, LastError
FROM opportunities.PortalSession WHERE Portal = @p;", con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 40).Value = portal;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new PortalSessionRow(
            r.IsDBNull(0) ? null : r.GetString(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4),
            r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5), r.IsDBNull(6) ? null : r.GetString(6));
    }

    /// <summary>A person signed in: replaces whatever was saved.</summary>
    public async Task SaveSignInAsync(string portal, string? account, string refreshToken, DateTimeOffset? refreshExpiresUtc, string signedInBy, CancellationToken ct)
    {
        await ExecAsync(@"
MERGE opportunities.PortalSession AS t
USING (SELECT @p AS Portal) AS s ON t.Portal = s.Portal
WHEN MATCHED THEN UPDATE SET Account = @a, RefreshToken = @rt, RefreshExpiresUtc = @rx, SignedInBy = @by,
    SignedInAtUtc = SYSDATETIMEOFFSET(), LastRenewedAtUtc = SYSDATETIMEOFFSET(), LastError = NULL
WHEN NOT MATCHED THEN INSERT (Portal, Account, RefreshToken, RefreshExpiresUtc, SignedInBy, SignedInAtUtc, LastRenewedAtUtc)
    VALUES (@p, @a, @rt, @rx, @by, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());", portal, account, refreshToken, refreshExpiresUtc, signedInBy, null, ct).ConfigureAwait(false);
    }

    /// <summary>The Worker renewed it: keep the new refresh token, clear any error.</summary>
    public Task SaveRenewalAsync(string portal, string refreshToken, DateTimeOffset? refreshExpiresUtc, CancellationToken ct) =>
        ExecAsync(@"
UPDATE opportunities.PortalSession SET RefreshToken = @rt, RefreshExpiresUtc = @rx, LastRenewedAtUtc = SYSDATETIMEOFFSET(), LastError = NULL
WHERE Portal = @p;", portal, null, refreshToken, refreshExpiresUtc, null, null, ct);

    /// <summary>A renewal was refused: record why, and leave the row for the next sign-in to replace.</summary>
    public Task SaveErrorAsync(string portal, string error, CancellationToken ct) =>
        ExecAsync("UPDATE opportunities.PortalSession SET LastError = @err WHERE Portal = @p;", portal, null, null, null, null, error, ct);

    private async Task ExecAsync(string sql, string portal, string? account, string? refreshToken, DateTimeOffset? refreshExpires, string? by, string? error, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 40).Value = portal;
        cmd.Parameters.Add("@a", SqlDbType.NVarChar, 200).Value = (object?)account ?? DBNull.Value;
        cmd.Parameters.Add("@rt", SqlDbType.NVarChar, -1).Value = (object?)refreshToken ?? DBNull.Value;
        cmd.Parameters.Add("@rx", SqlDbType.DateTimeOffset).Value = (object?)refreshExpires ?? DBNull.Value;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 200).Value = (object?)by ?? DBNull.Value;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 400).Value = (object?)(error is { Length: > 400 } ? error[..400] : error) ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public sealed record PortalSessionRow(string? Account, string RefreshToken, DateTimeOffset? RefreshExpiresUtc, string SignedInBy,
    DateTimeOffset SignedInAtUtc, DateTimeOffset? LastRenewedAtUtc, string? LastError);
