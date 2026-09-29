#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.NetworkOps.Core.Learning;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Kor.Operations.App.NetworkOps;

// The Command Center's only way in: the NetworkOps API on KOR-APP01 (Api/ApiHost.cs in the service).
// The PC holds no database credential. Every call carries an Entra token for "KOR NetworkOps API",
// which Entra only issues to members of "NetworkOps Admins", and only after MFA (Conditional Access
// "NetworkOps API - require MFA"). The API writes the signed-in UPN into every audit column.
//
// The server certificate is self-signed (there is no internal CA) and pinned here by its SHA-256:
// this client trusts that one certificate and nothing else, so a man in the middle gets nothing.
// Settings are in App.config (NetworkOps.Api*); none of them is a secret.
public sealed class NetworkOpsClient
{
    public const string BaseUrlKey = "NetworkOps.ApiBaseUrl";
    public const string ScopeKey = "NetworkOps.ApiScope";
    public const string PinKey = "NetworkOps.ApiCertSha256";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient? _http;
    private readonly string[] _scopes = [];
    private readonly Lazy<Task<IPublicClientApplication>>? _pca;
    private readonly string? _notConfigured;

    private NetworkOpsClient(string notConfigured) => _notConfigured = notConfigured;

    public NetworkOpsClient(string baseUrl, string scope, string certSha256, string tenantId, string clientId)
    {
        var pin = certSha256.Replace(":", "").Replace(" ", "").Trim();
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                // Trust exactly the pinned certificate: a self-signed one, with no CA to vouch for it.
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                    cert is not null && string.Equals(Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())), pin, StringComparison.OrdinalIgnoreCase),
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        _scopes = [scope];
        _pca = new Lazy<Task<IPublicClientApplication>>(() => BuildPcaAsync(tenantId, clientId));
    }

    /// <summary>Reads NetworkOps.Api* from App.config and the app's own Entra registration (Graph.TenantId/ClientId).</summary>
    public static NetworkOpsClient FromAppConfig()
    {
        string? S(string k) => ConfigurationManager.AppSettings[k] is { Length: > 0 } v ? v.Trim() : null;
        var (url, scope, pin, tenant, client) = (S(BaseUrlKey), S(ScopeKey), S(PinKey), S("Graph.TenantId"), S("Graph.ClientId"));
        return url is null || scope is null || pin is null || tenant is null || client is null
            ? Unconfigured($"App.config is missing {BaseUrlKey}, {ScopeKey} or {PinKey}, so the page has no NetworkOps service to talk to.")
            : new NetworkOpsClient(url, scope, pin, tenant, client);
    }

    public static NetworkOpsClient Unconfigured(string why) => new(why);

    public bool IsConfigured => _notConfigured is null;

    // ------------------------------------------------------------------ reads

    public Task<FleetSnapshot> GetFleetAsync(CancellationToken ct) => GetAsync<FleetSnapshot>("api/fleet", ct)!;

    public Task<DeviceHistory> GetDeviceHistoryAsync(int deviceId, CancellationToken ct) => GetAsync<DeviceHistory>($"api/devices/{deviceId}/history", ct)!;

    public async Task<IReadOnlyList<Resolution>> GetResolutionsAsync(CancellationToken ct)
        => (await GetAsync<List<ResolutionRow>>("api/resolutions", ct).ConfigureAwait(false)).Select(r => r.ToResolution()).ToList();

    public async Task<TriggerState?> GetTriggerAsync(long triggerId, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Get, $"api/triggers/{triggerId}", null, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureOkAsync(res).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<TriggerState>(Json, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ writes (the API records who, from the token)

    /// <summary>Queues "check this PC now"; the service claims it within ~5 s.</summary>
    public async Task<long> QueueCheckAsync(string deviceName, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Post, $"api/devices/{Uri.EscapeDataString(deviceName)}/check", null, ct).ConfigureAwait(false);
        await EnsureOkAsync(res).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("triggerId").GetInt64();
    }

    /// <summary>Cancels a check that has not been claimed yet. False when the service already took it.</summary>
    public async Task<bool> CancelCheckAsync(long triggerId, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Post, $"api/triggers/{triggerId}/cancel", null, ct).ConfigureAwait(false);
        await EnsureOkAsync(res).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("cancelled").GetBoolean();
    }

    public Task AcknowledgeAsync(long findingId, string? note, CancellationToken ct)
        => PostAsync($"api/findings/{findingId}/acknowledge", new AnnotateRequest(note, null), ct);

    public Task SnoozeAsync(long findingId, DateTime untilUtc, string? note, CancellationToken ct)
        => PostAsync($"api/findings/{findingId}/snooze", new AnnotateRequest(note, untilUtc), ct);

    public Task ReopenAsync(long findingId, CancellationToken ct)
        => PostAsync($"api/findings/{findingId}/reopen", null, ct);

    public Task AddNoteAsync(int deviceId, string body, CancellationToken ct)
        => PostAsync($"api/devices/{deviceId}/notes", new NoteRequest(body), ct);

    // ------------------------------------------------------------------ plumbing

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        await EnsureOkAsync(res).ConfigureAwait(false);
        return (await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false))!;
    }

    private async Task PostAsync(string path, object? body, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Post, path, body, ct).ConfigureAwait(false);
        await EnsureOkAsync(res).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (_notConfigured is not null) throw new InvalidOperationException(_notConfigured);
        var token = await TokenAsync(ct).ConfigureAwait(false);
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        try
        {
            return await _http!.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            throw new InvalidOperationException("The NetworkOps service presented a certificate that is not the pinned one. Nothing was sent. " +
                $"If APP01's certificate was renewed, update {PinKey} in App.config.", ex);
        }
    }

    private static async Task EnsureOkAsync(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        var detail = await ErrorOfAsync(res).ConfigureAwait(false);
        throw new InvalidOperationException(res.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "Access denied by the NetworkOps service. It needs an account in the Entra group \"NetworkOps Admins\", signed in with MFA.",
            HttpStatusCode.Conflict or HttpStatusCode.BadRequest or HttpStatusCode.NotFound => detail ?? $"The service said {(int)res.StatusCode}.",
            _ => $"The NetworkOps service answered {(int)res.StatusCode} {res.ReasonPhrase}.{(detail is null ? "" : " " + detail)}",
        });
    }

    private static async Task<string?> ErrorOfAsync(HttpResponseMessage res)
    {
        try
        {
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        var pca = await _pca!.Value.ConfigureAwait(false);
        var account = (await pca.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
        try
        {
            if (account is not null)
                return (await pca.AcquireTokenSilent(_scopes, account).ExecuteAsync(ct).ConfigureAwait(false)).AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            // Conditional Access asking for MFA arrives here as a claims challenge: pass it on.
            var interactive = pca.AcquireTokenInteractive(_scopes).WithAccount(account);
            if (!string.IsNullOrEmpty(ex.Claims)) interactive = interactive.WithClaims(ex.Claims);
            return (await interactive.ExecuteAsync(ct).ConfigureAwait(false)).AccessToken;
        }
        return (await pca.AcquireTokenInteractive(_scopes).ExecuteAsync(ct).ConfigureAwait(false)).AccessToken;
    }

    // The same registration and the same token cache as the app's Graph sign-in
    // (Services/MsalGraphAuthenticationProvider), so the account is already known and only the MFA
    // step Conditional Access requires for this API ever prompts.
    private static async Task<IPublicClientApplication> BuildPcaAsync(string tenantId, string clientId)
    {
        var pca = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .WithDefaultRedirectUri()
            .Build();
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR", "KorTransmittals");
        Directory.CreateDirectory(dir);
        var props = new StorageCreationPropertiesBuilder(Path.Combine(dir, "msal_token_cache.dat"), dir)
            .WithCacheChangedEvent("Kor.Transmittals.MsalTokenCache")
            .Build();
        (await MsalCacheHelper.CreateAsync(props).ConfigureAwait(false)).RegisterCache(pca.UserTokenCache);
        return pca;
    }
}
