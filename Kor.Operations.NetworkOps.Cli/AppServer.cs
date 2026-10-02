#nullable enable
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Kor.Operations.NetworkOps.Cli;

// NetworkOps on APP01, as the CLI talks to it: the route every Claude session uses to work a machine. The session runs
// on the person's PC, usually over the VPN; it never reaches a PC or server from there. It asks APP01 -- on the same
// network as everything -- and APP01 runs the work through the machine's agent or its own network route.
//
// Signed in as the person, exactly like the app: the app's own Entra registration, the same token cache file (so once
// the app has signed in, this is silent), and Conditional Access's MFA on top. The API's certificate is pinned by its
// SHA-256, as the app pins it. These values are the app's (Kor.Operations.App/App.config); a test keeps them equal.
internal sealed class AppServer : IDisposable
{
    public const string BaseUrl = "https://KOR-APP01.int.korstructural.com:8445";
    public const string Scope = "api://1ba6790b-5f6b-4538-aad5-5d6949720385/NetworkOps.Access";
    public const string CertSha256 = "EBAE9FE81D92623E1EED3C1F9C8F4ED7D5750A8BAE27522CE9F2CE1A3738EA6F";
    public const string TenantId = "d9be1f7f-aacf-461a-8d1b-5528b86d540f";
    public const string ClientId = "69b68cd2-a051-4782-a45e-4f1276942c06";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private IPublicClientApplication? _pca;

    public AppServer(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null && Convert.ToHexString(SHA256.HashData(cert.RawData)).Equals(CertSha256, StringComparison.OrdinalIgnoreCase),
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(Environment.GetEnvironmentVariable("KOR_NETWORKOPS_API") ?? BaseUrl), Timeout = timeout };
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<T>(req, ct).ConfigureAwait(false);
    }

    public async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Json) };
        return await SendAsync<T>(req, ct).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct).ConfigureAwait(false));
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            string? error = null;
            try { using var d = JsonDocument.Parse(text); error = d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null; } catch (JsonException) { }
            throw new AppServerException((int)res.StatusCode, res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? "NetworkOps refused the sign-in: it needs an account in the Entra group \"NetworkOps Admins\", signed in with MFA."
                : error ?? $"NetworkOps answered {(int)res.StatusCode} {res.ReasonPhrase}");
        }
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        _pca ??= await BuildPcaAsync().ConfigureAwait(false);
        var scopes = new[] { Scope };
        var account = (await _pca.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
        try
        {
            if (account is not null) return (await _pca.AcquireTokenSilent(scopes, account).ExecuteAsync(ct).ConfigureAwait(false)).AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            Console.Error.WriteLine("netops: signing in to NetworkOps (a browser window opens for MFA)...");
            var again = _pca.AcquireTokenInteractive(scopes).WithAccount(account);
            if (!string.IsNullOrEmpty(ex.Claims)) again = again.WithClaims(ex.Claims);
            return (await again.ExecuteAsync(ct).ConfigureAwait(false)).AccessToken;
        }
        Console.Error.WriteLine("netops: signing in to NetworkOps (a browser window opens)...");
        return (await _pca.AcquireTokenInteractive(scopes).ExecuteAsync(ct).ConfigureAwait(false)).AccessToken;
    }

    // The app's registration and token cache file (Kor.Operations.App/NetworkOps/NetworkOpsClient.BuildPcaAsync).
    private static async Task<IPublicClientApplication> BuildPcaAsync()
    {
        var pca = PublicClientApplicationBuilder.Create(ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{TenantId}")
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

    public void Dispose() => _http.Dispose();
}

internal sealed class AppServerException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
