#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.OnTarget;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The run-on-target contract: what the payload publishes and how it is read back. A payload
// that fails on the target produces no result file at all -- the only symptom is a timeout --
// so the contract is proven here, including by running the payload in real PowerShell.
//
// WHAT IT COVERS: embedding, atomic publish (.tmp then rename), Ok/error reporting, the
// one-element-array normalisation, the BOM, and (Slow) an end-to-end local run under Windows
// PowerShell 5.1 -- the engine the targets run -- with a non-ASCII body. WHAT IT DOES NOT: the
// SCM launch and c$ staging (live-only; see OnTargetChannel). A SAME-CLASS FAULT IT WOULD NOT
// CATCH: a probe body that is valid PowerShell 7 but not 5.1 syntax fails only on the target;
// the Slow test runs 5.1 so it catches that for the bodies it runs, not for every probe.
public sealed class OnTargetPayloadTests
{
    [Fact]
    public void The_body_is_embedded_verbatim_and_published_atomically()
    {
        const string body = "Get-Item C:\\Windows | Select-Object Name";
        var p = OnTargetPayload.Build(body, @"C:\Windows\Temp\korrun-x.json");
        Assert.Contains(body, p);
        Assert.Contains(@"WriteAllText('C:\Windows\Temp\korrun-x.json.tmp'", p);
        Assert.Contains(@"Move-Item -LiteralPath 'C:\Windows\Temp\korrun-x.json.tmp' -Destination 'C:\Windows\Temp\korrun-x.json'", p);
        Assert.Contains("Ok = $false; Error =", p);
    }

    [Fact]
    public void A_result_path_with_a_quote_is_refused()
        => Assert.Throws<ArgumentException>(() => OnTargetPayload.Build("1", @"C:\it's.json"));

    [Fact]
    public void Scripts_are_written_with_a_bom_because_targets_run_powershell_5_1()
        => Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, OnTargetPayload.ScriptEncoding.GetPreamble());

    [Fact]
    public void A_single_object_output_is_normalised_to_an_array()
    {
        var r = OnTargetPayload.ParseResult("""{ "Ok": true, "Output": { "Answer": 42 } }""");
        Assert.True(r.Ok);
        Assert.Equal(42, JsonDocument.Parse(r.OutputJson!).RootElement[0].GetProperty("Answer").GetInt32());
    }

    [Fact]
    public void An_error_result_carries_its_message_and_line()
    {
        var r = OnTargetPayload.ParseResult("""{ "Ok": false, "Error": "deliberate", "Line": 3 }""");
        Assert.False(r.Ok);
        Assert.Equal("deliberate", r.Error);
        Assert.Equal(3, r.Line);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("[pscustomobject]@{ Answer = 42; Label = 'probe ' + [char]0x2014 + ' ok' }", true)]
    [InlineData("throw 'deliberate'", false)]
    public void The_payload_runs_under_windows_powershell_5_1(string body, bool expectOk)
    {
        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(ps)) return;   // not a Windows box with 5.1; the contract tests above still ran

        var dir = Path.Combine(Path.GetTempPath(), "kor-ontarget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var result = Path.Combine(dir, "result.json");
            var script = Path.Combine(dir, "payload.ps1");
            File.WriteAllText(script, OnTargetPayload.Build(body, result), OnTargetPayload.ScriptEncoding);
            using var proc = Process.Start(new ProcessStartInfo(ps, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true })!;
            Assert.True(proc.WaitForExit(60_000), "payload did not finish in 60s");

            Assert.True(File.Exists(result), "the payload published no result file");
            Assert.False(File.Exists(result + ".tmp"), "a .tmp file was left behind");
            var r = OnTargetPayload.ParseResult(File.ReadAllText(result));
            Assert.Equal(expectOk, r.Ok);
            if (expectOk)
            {
                var o = JsonDocument.Parse(r.OutputJson!).RootElement[0];
                Assert.Equal(42, o.GetProperty("Answer").GetInt32());
                Assert.Equal("probe \u2014 ok", o.GetProperty("Label").GetString());
            }
            else Assert.Equal("deliberate", r.Error);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
