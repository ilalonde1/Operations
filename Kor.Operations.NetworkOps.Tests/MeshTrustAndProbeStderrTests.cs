#nullable enable
using System.Net.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Service.Mesh;
using Kor.Operations.NetworkOps.Transport;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Two faults the overnight fleet check of 2026-10-02 found, each as a check on its class.
//
// 1. KOR-MESH01 moved to a Let's Encrypt certificate (renewed every ~60 days) and NetworkOps, which trusted MeshCentral by
//    a SHA-256 pin on the old self-signed one, could no longer read it ("Unable to connect": Connect links and remote-control
//    installs dead). The class: A PIN ON A CERTIFICATE THAT ROTATES BREAKS AT THE ROTATION. MeshTrust is the one rule, and
//    the PC-side install script must say the same thing.
// 2. "probe-incomplete: session: No User exists for *" on 6 PCs nobody was signed in to: `quser 2>$null` under
//    ErrorActionPreference Stop turns a native command's stderr into a terminating error. The class: A NATIVE COMMAND'S
//    STDERR REDIRECTED BY POWERSHELL IN A STOP SCRIPT. Reproduced on KOR-207 and fixed in the same run (cmd swallows it).
//
// WHAT IT COVERS: MeshTrust's answers (validated -> yes; pinned self-signed -> yes; anything else, or no certificate, -> no);
// that the service's MeshCentral client and the PC install script both use that rule; that no script NetworkOps runs on a
// machine redirects a native command's stderr with 2>$null.
// WHAT IT DOES NOT: a live TLS handshake (proven live on 2026-10-02 after the deploy: the Mesh sweep read MESH01 again), or
// a native command's stderr redirected another way (2>&1 into a variable would not throw, so it is not the class).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: another rotating certificate pinned elsewhere (Veeam's is pinned on purpose: it is
// self-signed and does not rotate) -- only MESH01's is checked here.
public sealed class MeshTrustAndProbeStderrTests
{
    private static readonly byte[] SelfSigned = [1, 2, 3, 4];
    private static string PinOf(byte[] raw) => Convert.ToHexString(SHA256.HashData(raw));

    [Fact]
    public void Mesh01_is_trusted_by_a_validated_certificate_or_by_the_pin()
    {
        var pin = PinOf(SelfSigned);
        Assert.True(MeshTrust.Accepts([9, 9, 9], SslPolicyErrors.None, pin));                               // Let's Encrypt, any renewal
        Assert.True(MeshTrust.Accepts(SelfSigned, SslPolicyErrors.RemoteCertificateChainErrors, pin));       // the rollback certificate
        Assert.True(MeshTrust.Accepts(SelfSigned, SslPolicyErrors.RemoteCertificateChainErrors, pin.ToLowerInvariant()));
        Assert.False(MeshTrust.Accepts([9, 9, 9], SslPolicyErrors.RemoteCertificateChainErrors, pin));       // untrusted and not pinned
        Assert.False(MeshTrust.Accepts([9, 9, 9], SslPolicyErrors.RemoteCertificateNameMismatch, pin));      // trusted CA, wrong name
        Assert.False(MeshTrust.Accepts(null, SslPolicyErrors.None, pin));
        Assert.False(MeshTrust.Accepts(SelfSigned, SslPolicyErrors.RemoteCertificateChainErrors, ""));        // no pin configured
    }

    [Fact]
    public void The_service_and_the_PC_install_script_use_the_same_rule()
    {
        var client = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.NetworkOps.Transport", "MeshCentralClient.cs"));
        Assert.Contains("MeshTrust.Accepts(", client);
        Assert.DoesNotContain("SHA256.HashData", client);   // no second, pin-only rule beside it

        var script = MeshInstaller.Script(new Kor.Operations.NetworkOps.Service.NetworkOpsOptions
            { MeshUrl = "https://kor-mesh01.int.korstructural.com", MeshCertSha256 = new string('A', 64), MeshPcGroup = "mesh//x" }, server: false);
        var callback = Regex.Match(script, @"ServerCertificateValidationCallback = \{(.*?)\n\}", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("if ([int]$errors -eq 0) { return $true }", callback);   // validated = trusted, as MeshTrust
        Assert.Contains(new string('A', 64), callback);                            // and the pin, as MeshTrust
    }

    [Fact]
    public void No_script_run_on_a_machine_redirects_a_native_commands_stderr_with_2_dollar_null()
    {
        var root = RepoRoot();
        var scripts = new[] { "Kor.Operations.NetworkOps.Core", "Kor.Operations.NetworkOps.Service", "Kor.Operations.NetworkOps.Agent" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(root, p), "*.ps1", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();
        Assert.True(scripts.Count >= 15, $"found only {scripts.Count} scripts: the scan is broken");
        var offenders = scripts.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)))
            .Where(x => !x.l.TrimStart().StartsWith('#') && Regex.IsMatch(x.l, @"2>\s*\$null"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.l.Trim()}").ToList();
        Assert.True(offenders.Count == 0, "a native command's stderr redirected with 2>$null throws under ErrorActionPreference Stop -- use cmd.exe /c '... 2>nul':\n" + string.Join("\n", offenders));
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
