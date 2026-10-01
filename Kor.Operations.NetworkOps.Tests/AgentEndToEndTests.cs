#nullable enable
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kor.Operations.NetworkOps.Service.Agents;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The REAL agent exe (.NET Framework 4.8, built by the service's build) against the REAL agent endpoints on a
// real HTTPS listener, with a throwaway certificate pinned exactly as APP01's is. The store is a fake (no SQL).
//
// WHAT IT COVERS: the wire both ways (the agent's DataContract JSON against the service's System.Text.Json); the
// certificate pin; the key check; a script run by Windows PowerShell 5.1 and its result back as an OnTargetRun;
// text outside ASCII surviving the trip; the idle time reaching the script; a wrong key refused; an agent
// pinned to some other certificate refusing to talk at all.
// WHAT IT DOES NOT: running as SYSTEM, the installer, the service host, or the idle read through
// CreateProcessAsUser (here the agent runs in this desktop session and reads its own) -- those are proven on a
// real PC (KOR-104N, 2026-09-30). A SAME-CLASS FAULT IT WOULD NOT CATCH: an agent that works on this machine and
// fails on one with a TLS policy that refuses this certificate's key type.
[Trait("Speed", "Slow")]
// ⚠ Cleanup is IAsyncLifetime, not IAsyncDisposable: xUnit 2 never calls the latter on a test class, and an agent
// left running holds the test host's output pipe, so `dotnet test` waits on it forever after every test passed.
public sealed class AgentEndToEndTests : IAsyncLifetime
{
    private const string Pc = "TESTPC";
    private readonly string _data = Path.Combine(Path.GetTempPath(), "kor-agent-e2e-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _key = AgentApi.NewKey();
    private readonly X509Certificate2 _cert = SelfSigned();
    private readonly AgentHub _hub = new(TimeProvider.System);
    private WebApplication? _app;
    private Process? _agent;

    [Fact]
    public async Task The_real_agent_runs_a_script_and_its_result_comes_back_intact()
    {
        var url = await StartServerAsync();
        _agent = StartAgent(url, Hex(_cert));
        await WaitConnectedAsync();

        var run = await _hub.RunAsync(Pc, "'kwurmlinger · active — ok'; \"$env:KOR_CONSOLE_IDLE_SECONDS\"", TimeSpan.FromSeconds(60), wantsIdle: true, default);

        Assert.NotNull(run);
        Assert.True(run!.Status == OnTargetStatus.Ok, $"{run.Status}: {run.Error}");
        var output = System.Text.Json.JsonSerializer.Deserialize<string[]>(run.OutputJson!)!;
        Assert.Equal("kwurmlinger · active — ok", output[0]);
        Assert.Matches("^[0-9]+$", output[1]);   // the agent read this session's idle time and handed it over
    }

    [Fact]
    public async Task A_failing_script_comes_back_as_a_script_error_with_its_line()
    {
        var url = await StartServerAsync();
        _agent = StartAgent(url, Hex(_cert));
        await WaitConnectedAsync();

        var run = await _hub.RunAsync(Pc, "$x = 1\nthrow 'the disk is gone'", TimeSpan.FromSeconds(60), false, default);
        Assert.Equal(OnTargetStatus.ScriptError, run!.Status);
        Assert.Contains("the disk is gone", run.Error);
    }

    [Fact]
    public async Task A_wrong_key_is_refused()
    {
        var url = await StartServerAsync();
        using var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });
        using var req = new HttpRequestMessage(HttpMethod.Post, url + "/agent/v1/poll") { Content = new StringContent("""{"version":"1","workDir":"C:\\w"}""", System.Text.Encoding.UTF8, "application/json") };
        req.Headers.Add(AgentApi.DeviceHeader, Pc);
        req.Headers.TryAddWithoutValidation("Authorization", "KorAgent " + AgentApi.NewKey());
        using var res = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.False(_hub.IsConnected(Pc));
    }

    [Fact]
    public async Task An_agent_pinned_to_another_certificate_never_connects()
    {
        var url = await StartServerAsync();
        using var other = SelfSigned();
        _agent = StartAgent(url, Hex(other));
        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.False(_hub.IsConnected(Pc));
        Assert.Contains("cannot reach the server", await File.ReadAllTextAsync(Path.Combine(_data, "agent.log")));
    }

    private async Task<string> StartServerAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_hub);
        builder.Services.AddSingleton<IAgentDirectory>(new FakeDirectory(AgentApi.Hash(_key)));
        builder.Services.ConfigureHttpJsonOptions(j => j.SerializerOptions.PropertyNameCaseInsensitive = true);
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(_cert)));
        _app = builder.Build();
        AgentApi.UseAgentGate(_app);   // as ApiHost does: the key is checked before any body is read
        AgentApi.Map(_app);
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return address.Replace("127.0.0.1", "localhost");
    }

    private Process StartAgent(string url, string pin)
    {
        Directory.CreateDirectory(_data);
        File.WriteAllText(Path.Combine(_data, "agent.key"), _key);
        var exe = AgentExe();
        return Process.Start(new ProcessStartInfo(exe, $"--console --server {url} --pin {pin} --device {Pc} --data \"{_data}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("the agent did not start");
    }

    private async Task WaitConnectedAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!_hub.IsConnected(Pc))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("the agent never called in. Its log: " + (File.Exists(Path.Combine(_data, "agent.log")) ? File.ReadAllText(Path.Combine(_data, "agent.log")) : "(none)"));
            await Task.Delay(200);
        }
    }

    /// <summary>The agent the service build bundles (Service.csproj, CopyAgentToOutput): the exe that ships.</summary>
    private static string AgentExe()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "agent", RemoteAgentInstall.ExeName);
        if (File.Exists(exe)) return exe;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Agent"))) dir = dir.Parent;
        var built = dir is null ? null : Directory.GetFiles(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Agent", "bin"), RemoteAgentInstall.ExeName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return built ?? throw new FileNotFoundException("the agent exe is not built: build Kor.Operations.NetworkOps.Service first");
    }

    private static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Schannel cannot use an ephemeral key: round-trip through PFX so the key is usable by the listener.
#pragma warning disable SYSLIB0057
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057
    }

    private static string Hex(X509Certificate2 c) => Convert.ToHexString(SHA256.HashData(c.RawData));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_agent is { HasExited: false }) { _agent.Kill(entireProcessTree: true); await _agent.WaitForExitAsync(); }
        _agent?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
        _cert.Dispose();
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class FakeDirectory(byte[] hash) : IAgentDirectory
    {
        public Task<NetworkOpsStore.AgentCredential?> AgentCredentialAsync(string deviceName, CancellationToken ct)
            => Task.FromResult(deviceName == Pc ? new NetworkOpsStore.AgentCredential(1, Pc, hash, false) : null);

        public Task TouchAgentAsync(int deviceId, string version, string? address, DateTime nowUtc, CancellationToken ct) => Task.CompletedTask;

        public Task<long?> QueueCheckIfStaleAsync(string deviceName, TimeSpan maxAge, string by, CancellationToken ct) => Task.FromResult<long?>(null);
    }
}
