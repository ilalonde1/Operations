#nullable enable
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Mesh;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Remote control (MeshCentral on KOR-MESH01) as NetworkOps sees it.
//
// WHAT IT COVERS: the client against a real websocket server on a pinned self-signed certificate -- the x-meshauth
// header, a device list arriving in several frames after other messages, a refused login, a wrong certificate; the
// "conn" FLAGS (5 = connected, the 09-30 misread); matching nodes to PCs by name and to rack servers by MeshName or
// Address; the findings; a stale read claiming nothing; the install script carrying this deployment's server, group
// and pin with nothing left unreplaced.
// WHAT IT DOES NOT: the real MeshCentral (its message shapes are taken from its meshctrl source, 1.2.5) and the real
// install on a PC -- both proven live. A SAME-CLASS FAULT IT WOULD NOT CATCH: MeshCentral renaming "conn" or nesting
// nodes differently in a later version; the live sweep's summary ("N linked") is what would show it.
public sealed class MeshTests
{
    // ---- the client

    [Fact]
    public async Task The_client_signs_in_reads_a_multi_frame_device_list_and_ignores_messages_before_it()
    {
        await using var server = await FakeMesh.StartAsync();
        var nodes = await new MeshCentralClient(server.Url, server.Pin, "networkops@korstructural.com", "secret").ListNodesAsync(default);

        Assert.Equal(server.ExpectedAuth, server.SeenAuth);
        Assert.Equal(3, nodes.Count);
        var n302 = nodes.Single(n => n.Name == "KOR-302N");
        Assert.Equal("node//bJ@yhUB", n302.Id);
        Assert.Equal("mesh//PCS", n302.MeshId);
        Assert.True(n302.AgentConnected);
        Assert.True(nodes.Single(n => n.Name == "KOR-216").AgentConnected);    // conn 5 = agent + AMT: connected
        Assert.False(nodes.Single(n => n.Name == "Kor-APP01").AgentConnected); // conn 0
    }

    [Fact]
    public async Task A_refused_login_is_an_error_not_an_empty_fleet()
    {
        await using var server = await FakeMesh.StartAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new MeshCentralClient(server.Url, server.Pin, "networkops@korstructural.com", "WRONG").ListNodesAsync(default));
    }

    [Fact]
    public async Task A_server_with_another_certificate_is_never_spoken_to()
    {
        await using var server = await FakeMesh.StartAsync();
        await Assert.ThrowsAnyAsync<WebSocketException>(() =>
            new MeshCentralClient(server.Url, new string('A', 64), "networkops@korstructural.com", "secret").ListNodesAsync(default));
        Assert.Null(server.SeenAuth);   // the password never left
    }

    [Fact]
    public void A_connect_link_carries_the_id_without_its_prefix()
        => Assert.Equal("bJ@yhUB", MeshCentralClient.LinkId("node//bJ@yhUB"));

    // ---- matching

    [Fact]
    public void Nodes_match_pcs_by_name_and_servers_by_mesh_name_or_address()
    {
        MeshNode N(string name, string mesh = "mesh//PCS") => new("node//" + name, name, mesh, 1);
        var pcs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["KOR-302N"] = 1, ["KOR-104N"] = 7 };
        var rack = new List<RackIdentity>
        {
            new(20, "KOR-APP01 (apps, SQL, NetworkOps)", "KOR-APP01", ""),
            new(21, "Veeam backups (BK01)", "192.168.1.18", "KOR-BK01"),
        };
        var (linked, unmatched) = MeshMap.Map([N("kor-302n"), N("KOR-104N"), N("Kor-APP01", "mesh//SRV"), N("KOR-BK01", "mesh//SRV"), N("Kor-FS01", "mesh//SRV")],
            pcs, rack, "mesh//PCS", "mesh//SRV");

        Assert.Equal([1, 7, 20, 21], linked.Select(l => l.DeviceId).Order());
        Assert.Equal("KOR Servers", linked.Single(l => l.DeviceId == 21).Group);
        Assert.Equal("KOR PCs", linked.Single(l => l.DeviceId == 1).Group);
        Assert.Equal("Kor-FS01", Assert.Single(unmatched).Name);   // in Mesh, not (yet) a NetworkOps device
    }

    // ---- findings

    [Fact]
    public void Missing_and_silent_remote_control_are_findings_only_when_the_pc_answered()
    {
        Assert.Equal("mesh-missing", Assert.Single(MeshRules.Evaluate(new MeshPresence(false, false), answered: true)).RuleKey);
        Assert.Equal("mesh-silent", Assert.Single(MeshRules.Evaluate(new MeshPresence(true, false), answered: true)).RuleKey);
        Assert.Empty(MeshRules.Evaluate(new MeshPresence(true, true), answered: true));
        Assert.Empty(MeshRules.Evaluate(new MeshPresence(false, false), answered: false));
        Assert.Empty(MeshRules.Evaluate(null, answered: true));   // remote control off, or no trustworthy read
        Assert.NotNull(Knowledge.For("mesh-missing"));
        Assert.NotNull(Knowledge.For("mesh-silent"));
    }

    [Fact]
    public void A_stale_read_claims_nothing()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var state = new MeshState(clock);
        Assert.Null(state.PresenceOf(1));                                     // never read
        state.Update([new MeshLink(1, "KOR-302N", new MeshNode("node//x", "KOR-302N", "mesh//PCS", 1), "KOR PCs")]);
        Assert.Equal(new MeshPresence(true, true), state.PresenceOf(1));
        Assert.Equal(new MeshPresence(false, false), state.PresenceOf(2));    // fresh read, not listed = missing
        clock.Advance(MeshState.FreshFor + TimeSpan.FromMinutes(1));
        Assert.Null(state.PresenceOf(1));                                     // MeshCentral silent: no findings anywhere
    }

    // ---- servers seen only through remote control, and KOR-MESH01 itself (rack collectors Mesh / MeshServer)

    [Fact]
    public async Task A_remote_only_server_is_up_while_its_mesh_agent_is_connected_and_says_its_health_is_not_read()
    {
        var o = new NetworkOpsOptions { MeshUrl = "https://kor-mesh01", MeshCertSha256 = new string('A', 64), MeshUser = "u", MeshPassword = "p" };
        var state = new MeshState(TimeProvider.System);
        var collector = new Kor.Operations.NetworkOps.Service.Rack.RackCollector(Microsoft.Extensions.Options.Options.Create(o), new Kor.Operations.NetworkOps.Service.Power.PowerState(), state);
        var fs01 = new RackDevice { Name = "KOR-FS01 (file server)", Kind = "Server", Collector = "Mesh", Address = "KOR-FS01", MeshName = "Kor-FS01" };
        var mesh01 = new RackDevice { Name = "KOR-MESH01", Kind = "Server", Collector = "MeshServer", Address = "192.168.1.27" };
        var none = new Dictionary<string, string>();

        Assert.False((await collector.CollectAsync(fs01, none, default)).Reachable);           // no MeshCentral read yet: nothing claimed
        Assert.False((await collector.CollectAsync(mesh01, none, default)).Reachable);

        state.Update([], [new MeshNode("node//fs", "Kor-FS01", "mesh//SRV", 1), new MeshNode("node//pc", "KOR-216", "mesh//PCS", 5), new MeshNode("node//rds", "Kor-RDS01", "mesh//SRV", 0)]);
        var up = await collector.CollectAsync(fs01, none, default);
        Assert.True(up.Reachable);
        Assert.Contains("health not read", up.Summary);
        Assert.Equal("node//fs", up.Facts["mesh.node"]);

        var rds = new RackDevice { Name = "KOR-RDS01", Kind = "Server", Collector = "Mesh", Address = "KOR-RDS01", MeshName = "Kor-RDS01" };
        Assert.Contains("not connected", (await collector.CollectAsync(rds, none, default)).Error);

        var server = await collector.CollectAsync(mesh01, none, default);
        Assert.True(server.Reachable);
        Assert.Equal("MeshCentral answering · 2 of 3 agents connected", server.Summary);   // conn 5 counts as connected
    }

    // ---- the install script

    [Theory]
    [InlineData(false, "5Jy%40ap0%24vhvy")]
    [InlineData(true, "1oFligrm")]
    public void The_install_script_carries_this_deployments_server_group_and_pin(bool server, string meshIdStart)
    {
        var o = new NetworkOpsOptions
        {
            MeshUrl = "https://kor-mesh01.int.korstructural.com/", MeshCertSha256 = "f9374a04fdbb5f61200dd37cb9a52b933c13d831d0a6c662f6f1417c1f0bd774",
            MeshPcGroup = "mesh//5Jy@ap0$vhvyyxWDAdLIVTrxIhCFsl", MeshServerGroup = "mesh//1oFligrmT8ErcYovFnEzlm$lVcch",
        };
        var s = MeshInstaller.Script(o, server);
        Assert.DoesNotContain("{{", s);
        Assert.Contains("https://kor-mesh01.int.korstructural.com/meshagents?id=4&meshid=" + meshIdStart, s);
        Assert.Contains("'F9374A04FDBB5F61200DD37CB9A52B933C13D831D0A6C662F6F1417C1F0BD774'", s);
        Assert.Contains(@"--installPath=""C:\Program Files\KorOperations\MeshAgent""", s);
    }

    /// <summary>A websocket "MeshCentral": checks x-meshauth, sends serverinfo then the device list in several frames.</summary>
    private sealed class FakeMesh : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly X509Certificate2 _cert;
        public Uri Url { get; private init; } = null!;
        public string Pin => Convert.ToHexString(SHA256.HashData(_cert.RawData));
        public string ExpectedAuth => Convert.ToBase64String(Encoding.UTF8.GetBytes("networkops@korstructural.com")) + "," + Convert.ToBase64String(Encoding.UTF8.GetBytes("secret"));
        public string? SeenAuth { get; private set; }

        private FakeMesh(WebApplication app, X509Certificate2 cert) { _app = app; _cert = cert; }

        public static async Task<FakeMesh> StartAsync()
        {
            var cert = SelfSigned();
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(cert)));
            var app = builder.Build();
            FakeMesh? self = null;
            app.UseWebSockets();
            app.Map("/control.ashx", async (HttpContext h) =>
            {
                if (!h.WebSockets.IsWebSocketRequest) { h.Response.StatusCode = 400; return; }
                var auth = h.Request.Headers["x-meshauth"].ToString();
                self!.SeenAuth = auth;
                using var ws = await h.WebSockets.AcceptWebSocketAsync();
                if (auth != self.ExpectedAuth)
                {
                    await Send(ws, """{"action":"close","cause":"noauth"}""");
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", default);
                    return;
                }
                await Send(ws, """{"action":"serverinfo","serverinfo":{"name":"kor-mesh01"}}""");
                var buf = new byte[4096];
                var req = await ws.ReceiveAsync(buf, default);
                if (!Encoding.UTF8.GetString(buf, 0, req.Count).Contains("\"nodes\"")) return;
                var list = """{"action":"nodes","nodes":{"mesh//PCS":[{"_id":"node//bJ@yhUB","name":"KOR-302N","conn":1},{"_id":"node//sZB","name":"KOR-216","conn":5}],"mesh//SRV":[{"_id":"node//app","name":"Kor-APP01","conn":0}]}}""";
                var bytes = Encoding.UTF8.GetBytes(list);
                // in three frames: the client must assemble one message from them
                var third = bytes.Length / 3;
                await ws.SendAsync(bytes.AsMemory(0, third), WebSocketMessageType.Text, false, default);
                await ws.SendAsync(bytes.AsMemory(third, third), WebSocketMessageType.Text, false, default);
                await ws.SendAsync(bytes.AsMemory(2 * third), WebSocketMessageType.Text, true, default);
                try { await ws.ReceiveAsync(buf, default); } catch (WebSocketException) { }
            });
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().Replace("127.0.0.1", "localhost");
            self = new FakeMesh(app, cert) { Url = new Uri(address) };
            return self;
        }

        private static Task Send(WebSocket ws, string text) => ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, default);

        private static X509Certificate2 SelfSigned()
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            req.CertificateExtensions.Add(san.Build());
            using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
#pragma warning disable SYSLIB0057
            return new X509Certificate2(c.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
            _cert.Dispose();
        }
    }
}
