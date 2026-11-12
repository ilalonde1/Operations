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
            else
            {
                Assert.Equal("deliberate", r.Error);
                Assert.Equal(1, r.Line);   // the line in the body as its caller wrote it, not in the wrapper
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---- NET-GUARD (12 Nov 2026, brain #133338, board row 2100) ------------------------------------------------
    //
    // THE MUST-FAIL TEST. From 01:57 to 11:05 on 12 Nov, 64 netops calls to KOR-210 sent a check script whose output
    // was Get-Content lines; each line carries PSPath/PSDrive/PSProvider, the old payload's ConvertTo-Json -Depth 8
    // walked that graph without end, no result was ever published, and each call left a powershell.exe alive --
    // ~120 GB of commit between them, ETABS starved. This runs that same shape of probe through the payload under
    // Windows PowerShell 5.1 (what the targets run): the OLD payload publishes nothing within the bound (proven
    // against d14ef5d4's OnTargetPayload.cs), the new one publishes the three plain lines in seconds.
    //
    // WHAT IT COVERS: the flattening of the payload's output (strings off Get-Content, a FileInfo off Get-Item) under
    // 5.1, end to end to the published file. WHAT IT DOES NOT: the SCM launch, c$ staging, or the 64-call stacking
    // (see the alive-marker tests below and OnTargetChannel); PowerShell 7 (targets run 5.1); a rich object whose
    // property GETTER itself hangs (the watchdog test covers that by killing, not by flattening). A SAME-CLASS FAULT
    // IT WOULD NOT CATCH: a probe body that hangs on its own before producing output (e.g. a WMI query against a
    // starved service) -- flattening cannot help; only the watchdog bounds it.
    [Theory]
    [InlineData("Get-Content -LiteralPath '{0}'", "lines")]
    [InlineData("Get-Item -LiteralPath $env:windir", "fileinfo")]
    public void Rich_powershell_output_is_published_as_plain_values_not_walked_forever(string bodyTemplate, string expect)
    {
        var ps = WindowsPowerShell();
        if (ps is null) return;
        var dir = NewDir();
        try
        {
            var input = Path.Combine(dir, "input.txt");
            File.WriteAllText(input, "line one\n\nline three\n");
            var result = Path.Combine(dir, "result.json");
            // The kill is far beyond the bound: this test measures the flattening, not the watchdog.
            var payload = OnTargetPayload.Build(string.Format(bodyTemplate, input), result, killAfter: TimeSpan.FromMinutes(10));
            Assert.True(RunBounded(ps, dir, payload, TimeSpan.FromSeconds(60)),
                "the payload did not finish in 60 s: its output was walked by the serialiser (the KOR-210 hang of 12 Nov)");

            var r = OnTargetPayload.ParseResult(File.ReadAllText(result));
            Assert.True(r.Ok, r.Error);
            var output = JsonDocument.Parse(r.OutputJson!).RootElement;
            if (expect == "lines")
            {
                Assert.Equal(new[] { "line one", "", "line three" }, output.EnumerateArray().Select(e => e.GetString() ?? "<null>").ToArray());
            }
            else
            {
                var item = output[0];
                Assert.Equal("Windows", item.GetProperty("Name").GetString(), ignoreCase: true);
                // One level of a rich object: the parent directory is its path, not another graph.
                Assert.Equal(JsonValueKind.String, item.GetProperty("Parent").ValueKind);
            }
            Assert.True(new FileInfo(result).Length < 64 * 1024, $"published {new FileInfo(result).Length} bytes for a tiny output");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // The watchdog: the body (a sleep) and the serialisation (a property getter that sleeps) are each killed ON THE
    // TARGET at the limit, with an error result saying so. Covers: the kill, the published reason, that the payload
    // process is gone. Does not cover: the child-process sweep (taskkill of what the body started) beyond running it.
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("Start-Sleep -Seconds 300; 'never'")]
    [InlineData("[pscustomobject]@{ A = 1 } | Add-Member -MemberType ScriptProperty -Name Slow -Value { Start-Sleep -Seconds 300; 2 } -PassThru")]
    public void A_payload_past_its_limit_is_killed_on_the_target_and_says_so(string body)
    {
        var ps = WindowsPowerShell();
        if (ps is null) return;
        var dir = NewDir();
        try
        {
            var result = Path.Combine(dir, "result.json");
            var payload = OnTargetPayload.Build(body, result, killAfter: TimeSpan.FromSeconds(5));
            var clock = Stopwatch.StartNew();
            Assert.True(RunBounded(ps, dir, payload, TimeSpan.FromSeconds(60)), "the watchdog did not kill the payload within 60 s");
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(45), $"took {clock.Elapsed.TotalSeconds:0} s for a 5 s limit");

            Assert.True(File.Exists(result), "the watchdog published no result");
            var r = OnTargetPayload.ParseResult(File.ReadAllText(result));
            Assert.False(r.Ok);
            Assert.StartsWith("killed on the target after 5 s", r.Error);
        }
        finally { TryDeleteDir(dir); }
    }

    // The alive marker: held for the payload's life, gone when it ends, so OnTargetChannel can refuse to stack.
    [Fact]
    [Trait("Speed", "Slow")]
    public void A_running_payload_is_seen_alive_and_a_second_of_its_kind_is_refused_until_it_ends()
    {
        var ps = WindowsPowerShell();
        if (ps is null) return;
        var dir = NewDir();
        try
        {
            const string body = "Start-Sleep -Seconds 6; 'done'";
            var kind = OnTargetAlive.KindOf(body);
            var result = Path.Combine(dir, "result.json");
            var payload = OnTargetPayload.Build(body, result, killAfter: TimeSpan.FromMinutes(1), alivePath: Path.Combine(dir, OnTargetAlive.FileName("abc12345", kind)));
            var script = Path.Combine(dir, "payload.ps1");
            File.WriteAllText(script, payload, OnTargetPayload.ScriptEncoding);
            using var proc = StartPs(ps, script);
            try
            {
                var seen = SpinUntil(() => OnTargetAlive.Find(dir).Count == 1, TimeSpan.FromSeconds(20));
                Assert.True(seen, "the running payload's alive marker was not seen");
                var alive = OnTargetAlive.Find(dir);
                Assert.Equal(kind, alive[0].Kind);
                Assert.NotNull(OnTargetAlive.Refusal(alive, kind, "TEST-PC", DateTime.UtcNow));
                Assert.Null(OnTargetAlive.Refusal(alive, OnTargetAlive.KindOf("something else"), "TEST-PC", DateTime.UtcNow));

                Assert.True(proc.WaitForExit(60_000), "payload did not finish");
                Assert.True(File.Exists(result));
                Assert.Empty(OnTargetAlive.Find(dir));
                Assert.Empty(Directory.GetFiles(dir, "*.alive"));
            }
            finally { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        }
        finally { TryDeleteDir(dir); }
    }

    [Fact]
    public void A_marker_nobody_holds_is_stale_and_removed_so_it_never_blocks_a_host()
    {
        var dir = NewDir();
        try
        {
            var stale = Path.Combine(dir, OnTargetAlive.FileName("dead0001", "aaaaaaaaaaaa"));
            File.WriteAllText(stale, "");
            Assert.Empty(OnTargetAlive.Find(dir));
            Assert.False(File.Exists(stale));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_held_marker_is_alive_and_the_any_kind_cap_refuses_past_the_limit()
    {
        var dir = NewDir();
        var held = new List<FileStream>();
        try
        {
            for (var i = 0; i < OnTargetAlive.MaxAlivePerHost; i++)
                held.Add(new FileStream(Path.Combine(dir, OnTargetAlive.FileName($"live000{i}", $"kind0000000{i}")),
                    FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete, 4096, FileOptions.DeleteOnClose));
            var alive = OnTargetAlive.Find(dir);
            Assert.Equal(OnTargetAlive.MaxAlivePerHost, alive.Count);
            var refusal = OnTargetAlive.Refusal(alive, "anotherkind0", "TEST-PC", DateTime.UtcNow);
            Assert.NotNull(refusal);
            Assert.Contains("TEST-PC", refusal);
            Assert.Null(OnTargetAlive.Refusal(alive.Take(OnTargetAlive.MaxAlivePerHost - 1).ToList(), "anotherkind0", "TEST-PC", DateTime.UtcNow));
        }
        finally
        {
            foreach (var f in held) f.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string? WindowsPowerShell()
    {
        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        return File.Exists(ps) ? ps : null;
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kor-ontarget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        // The watchdog may still be letting go of the directory for a moment after the payload ends.
        for (var i = 0; i < 10; i++)
        {
            try { Directory.Delete(dir, recursive: true); return; }
            catch (IOException) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) { Thread.Sleep(500); }
        }
    }

    private static Process StartPs(string ps, string script)
        => Process.Start(new ProcessStartInfo(ps, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"")
        { UseShellExecute = false, CreateNoWindow = true })!;

    /// <summary>Runs the payload; true when it ended within <paramref name="bound"/>. A payload still running at the
    /// bound is killed with its whole tree here, so a failing test never leaves a spinner behind on this PC.</summary>
    private static bool RunBounded(string ps, string dir, string payload, TimeSpan bound)
    {
        var script = Path.Combine(dir, "payload.ps1");
        File.WriteAllText(script, payload, OnTargetPayload.ScriptEncoding);
        using var proc = StartPs(ps, script);
        if (proc.WaitForExit((int)bound.TotalMilliseconds)) return true;
        proc.Kill(entireProcessTree: true);
        proc.WaitForExit(10_000);
        return false;
    }

    private static bool SpinUntil(Func<bool> condition, TimeSpan within)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < within)
        {
            if (condition()) return true;
            Thread.Sleep(200);
        }
        return condition();
    }

    // The 2026-09-29 fault: a probe published 105 MB. Here a plain 200 KB value stands in for the
    // blob -- a real rich object (Get-Item) takes minutes of CPU just to serialise at depth 8, which
    // is its own reason probes must return plain values. Under a 64 KB limit the target must publish
    // a short error, not the blob.
    [Fact]
    [Trait("Speed", "Slow")]
    public void An_oversized_result_is_replaced_by_an_error_on_the_target()
    {
        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(ps)) return;

        var dir = Path.Combine(Path.GetTempPath(), "kor-ontarget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var result = Path.Combine(dir, "result.json");
            var script = Path.Combine(dir, "payload.ps1");
            File.WriteAllText(script, OnTargetPayload.Build("[pscustomobject]@{ Big = 'x' * 200000 }", result, maxResultChars: 64 * 1024), OnTargetPayload.ScriptEncoding);
            using var proc = Process.Start(new ProcessStartInfo(ps, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true })!;
            Assert.True(proc.WaitForExit(60_000), "payload did not finish in 60s");

            Assert.True(new FileInfo(result).Length < 4096, $"published {new FileInfo(result).Length} bytes: the limit did not hold");
            var r = OnTargetPayload.ParseResult(File.ReadAllText(result));
            Assert.False(r.Ok);
            Assert.StartsWith("result too large:", r.Error);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
