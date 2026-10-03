#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kor.Operations.NetworkOps.Service.Rack;

/// <summary>
/// The UniFi controller's LIVE API (UniFi OS Server on KOR-UNIFI01, :11443): every device's ports as they are NOW (up,
/// speed, PoE, the SFP module) and every client connected now -- what the database read (kor-unifi-status) cannot say.
/// Ian, 2026-10-02: "you can use the api to more use than the UI ... Are we fully using it for the NetworkOps display?"
///
/// Read-only by what it calls (GET stat/device, GET stat/sta), signed in as a VIEW-ONLY local account
/// (KOR_NETWORKOPS_UNIFIAPIUSER / _UNIFIAPIPASSWORD, machine variables on APP01, never in a file). The controller's
/// certificate is self-signed (CN=unifi.local), so it is PINNED by SHA-256 (the rack device's CertSha256): the password is
/// only ever sent to that exact certificate. Signs out after each read.
/// Output: the fields NetworkOps uses, in the shape Core/Network/UniFiLive.Parse reads -- a device record also carries
/// SSH host keys and auth keys, and those are never copied.
/// </summary>
internal static class UniFiApi
{
    public static async Task<string> ReadLiveAsync(string address, int port, string site, string user, string password, string certSha256, CancellationToken ct)
    {
        var cookies = new CookieContainer();
        using var http = new HttpClient(new HttpClientHandler
        {
            CookieContainer = cookies,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert.RawData)).Equals(certSha256, StringComparison.OrdinalIgnoreCase),
        }) { BaseAddress = new Uri($"https://{address}:{port}/"), Timeout = TimeSpan.FromSeconds(30) };

        using var login = await http.PostAsJsonAsync("api/auth/login", new { username = user, password, rememberMe = false }, ct).ConfigureAwait(false);
        if (!login.IsSuccessStatusCode) throw new InvalidOperationException($"UniFi API sign-in refused ({(int)login.StatusCode})");
        var csrf = login.Headers.TryGetValues("X-CSRF-Token", out var v) ? v.FirstOrDefault() : null;
        try
        {
            var devices = await GetDataAsync(http, $"proxy/network/api/s/{site}/stat/device", ct).ConfigureAwait(false);
            var clients = await GetDataAsync(http, $"proxy/network/api/s/{site}/stat/sta", ct).ConfigureAwait(false);
            return Project(devices, clients, DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToJsonString();
        }
        finally
        {
            try
            {
                using var logout = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
                if (csrf is not null) logout.Headers.Add("X-CSRF-Token", csrf);
                using var _ = await http.SendAsync(logout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (HttpRequestException) { }   // the session expires on its own
        }
    }

    private static async Task<JsonArray> GetDataAsync(HttpClient http, string path, CancellationToken ct)
    {
        var body = await http.GetStringAsync(path, ct).ConfigureAwait(false);
        return JsonNode.Parse(body)?["data"] as JsonArray ?? throw new InvalidOperationException($"UniFi API {path}: no data");
    }

    /// <summary>Only the named fields, as Core/Network/UniFiLive.Parse reads them. Internal: a test holds it to that.</summary>
    internal static JsonObject Project(JsonArray devices, JsonArray clients, long now)
    {
        static JsonNode? F(JsonNode? n, string p) => n?[p]?.DeepClone();
        var o = new JsonObject
        {
            ["now"] = now,
            ["devices"] = new JsonArray(devices.Select(d => (JsonNode)new JsonObject
            {
                ["mac"] = F(d, "mac"), ["name"] = F(d, "name"), ["state"] = F(d, "state"), ["uptime"] = F(d, "uptime"),
                ["ports"] = new JsonArray((d?["port_table"] as JsonArray ?? []).Select(p => (JsonNode)new JsonObject
                {
                    ["port"] = F(p, "port_idx"), ["up"] = F(p, "up"), ["speed"] = F(p, "speed"), ["poe"] = F(p, "poe_enable"),
                    ["poeW"] = F(p, "poe_power"), ["media"] = F(p, "media"),
                    ["sfp"] = p?["sfp_found"]?.GetValue<bool>() == true ? F(p, "sfp_part") : null, ["name"] = F(p, "name"),
                }).ToArray()),
            }).ToArray()),
            ["clients"] = new JsonArray(clients.Select(c => (JsonNode)new JsonObject
            {
                ["mac"] = F(c, "mac"), ["ip"] = F(c, "ip"), ["hostname"] = F(c, "hostname"), ["name"] = F(c, "name"),
                ["wired"] = F(c, "is_wired"), ["swMac"] = F(c, "sw_mac"), ["swPort"] = F(c, "sw_port"), ["apMac"] = F(c, "ap_mac"),
                ["uptime"] = F(c, "uptime"),
            }).ToArray()),
        };
        return o;
    }
}
