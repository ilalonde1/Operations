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
                new ListToolsResult { Tools = [.. (await SignIn.OrSayItLapsed(() => upstream.ListToolsAsync(cancellationToken: ct).AsTask()).ConfigureAwait(false)).Select(t => t.ProtocolTool)] })
            .WithCallToolHandler(async (ctx, ct) =>
                await SignIn.OrLapsedResult(() => upstream.CallToolAsync(ctx.Params!, ct).AsTask()).ConfigureAwait(false));
        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }

    // A lapsed sign-in must say so. Until 28 Oct the SDK turned the token source's SignInNeededException into its stock
    // "An error occurred invoking '<tool>'", and Revit Fleet spent several attempts on 27 Oct ~23:50 debugging its own
    // script (board row 72) while the CLI, on the same lapse, exited 4 "SIGN-IN NEEDED".
    internal static class SignIn
    {
        public const string Lapsed = "NetworkOps SIGN-IN EXPIRED (the same fault the CLI reports as exit 4): ";

        /// <summary>The sign-in fault anywhere in the chain, or null: the token is asked for inside the HTTP pipeline, so
        /// the SDK's transport may wrap it.</summary>
        public static SignInNeededException? Find(Exception? e)
        {
            for (; e is not null; e = e.InnerException)
                if (e is SignInNeededException s) return s;
                else if (e is AggregateException a && a.InnerExceptions.Select(Find).FirstOrDefault(x => x is not null) is { } inner) return inner;
            return null;
        }

        /// <summary>tools/call: a lapsed sign-in comes back as a tool error whose text says what to do.</summary>
        public static async Task<CallToolResult> OrLapsedResult(Func<Task<CallToolResult>> call)
        {
            try { return await call().ConfigureAwait(false); }
            catch (Exception e) when (Find(e) is { } s)
            {
                return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = Lapsed + s.Message }] };
            }
        }

        /// <summary>tools/list has no error result: an McpException is the one the SDK passes through with its own text.</summary>
        public static async Task<T> OrSayItLapsed<T>(Func<Task<T>> call)
        {
            try { return await call().ConfigureAwait(false); }
            catch (Exception e) when (Find(e) is { } s) { throw new ModelContextProtocol.McpException(Lapsed + s.Message, e); }
        }
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
