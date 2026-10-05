#nullable enable
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Kor.Operations.NetworkOps.Cli;

// `netops mcp`: a stdio MCP server that Claude Code launches, which PROXIES to the real MCP endpoint on APP01 (/mcp,
// behind the same Entra auth as the rest of the API). It signs in AS THE PERSON -- AppServer's silent, auto-refreshing
// token -- and PINS the service certificate, so there is nothing for anyone to set up: no token, no cert, no env, no
// script. One tool set (the service's); this forwards tools/list and tools/call. The DB credential never leaves APP01.
//
// Claude Code config (repo .mcp.json): { "networkops": { "command": "netops", "args": ["mcp"] } }.
internal static class McpBridge
{
    public static async Task<int> RunAsync()
    {
        var baseUrl = (Environment.GetEnvironmentVariable("KOR_NETWORKOPS_API") ?? AppServer.BaseUrl).TrimEnd('/');
        var tokens = new AppServer(TimeSpan.FromSeconds(120));   // reuse its MSAL (silent, file-cached, auto-refreshing)
        var http = new HttpClient(new BearerHandler(tokens)
        {
            InnerHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                    cert is not null && Convert.ToHexString(SHA256.HashData(cert.RawData)).Equals(AppServer.CertSha256, StringComparison.OrdinalIgnoreCase),
            },
        });

        await using var upstream = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri($"{baseUrl}/mcp") }, http, NullLoggerFactory.Instance),
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);   // stdout is the MCP channel -- logs to stderr
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithListToolsHandler(async (_, ct) =>
                new ListToolsResult { Tools = [.. (await upstream.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false)).Select(t => t.ProtocolTool)] })
            .WithCallToolHandler(async (ctx, ct) =>
                await upstream.CallToolAsync(ctx.Params!, ct).ConfigureAwait(false));
        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }
}

// A fresh NetworkOps bearer on every upstream request: AppServer's token is MSAL-silent (the app's shared cache) and
// auto-refreshes, so a long-lived session never needs a new one by hand.
internal sealed class BearerHandler(AppServer tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.AccessTokenAsync(ct).ConfigureAwait(false));
        return await base.SendAsync(request, ct).ConfigureAwait(false);
    }
}
