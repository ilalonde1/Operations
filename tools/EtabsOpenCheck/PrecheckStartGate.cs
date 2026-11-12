// PRE-CHECK START GATE (board row 2103 step B, Ian 12 Nov 14:4x #133436 measure M1, relayed by framework-f8). A CSI start
// tool (SafeOpenCheck, EtabsOpenCheck) refuses to start SAFE or ETABS unless `takeoff precheck model` PASSES on the e2k
// it is given - the gate lives in the tool, so it holds whatever script calls the tool:
//
//     --precheck-takeoff <takeoff.exe of a pin>  --precheck-e2k <model.e2k>  [--pin-storey <storey>]
//     --no-precheck "<why>"     (instead: the start goes ahead, the why is printed; never silent)
//
// A value may not start with "--" (it is read as a missing value: a malformed pair, refused).
//
// Neither given, both given, a file missing, a malformed pair, or the precheck not exiting 0 (REFUSED 1, Usage 2, a crash,
// no exit within the timeout) = REFUSED: the caller exits 6 and starts nothing. The precheck's own lines are passed
// through, with the e2k's full sha256 and the takeoff path, so the log says which bytes passed through which pin.
// WHAT IT PROVES FOR SAFE: an .f2k set is written from the composed model, not from the e2k (01819 run 17: composed-safe2x
// carries SAFE-only edits), so for SAFE the gate proves the ETABS model the reactions came from, NOT the .f2k bytes. No
// precheck of an .f2k exists (stated gap, a board row).
// COVERS (PrecheckStartGateTests): every refusal above, a PASS, the arguments handed to takeoff, the output passed through.
// DOES NOT: run takeoff on KOR-210 (the first staged start proves it there); check the e2k is the one the CSI program
// opens - that is the caller's (EtabsOpenCheck gates the very copy it opens; SafeOpenCheck gates the e2k it is told).
// SAME-CLASS FAULT IT WOULD MISS: a caller that starts SAFE/ETABS without going through a tool that calls this.
// THIS IS A COPY, for EtabsOpenCheck: the TESTED original is KOR.Drafter Kor.Operations.EngineeringTools.Core/Csi/PrecheckStartGate.cs
// (PrecheckStartGateTests; TheEtabsOpenCheckCopyIsThisGate holds this copy equal). Change it there first and copy it here.
using System.Diagnostics;
using System.Security.Cryptography;

namespace Kor.EtabsOpenCheck;

public static class PrecheckStartGate
{
    public const int RefusedExit = 6;

    public sealed record Verdict(bool Start, string Line, IReadOnlyList<string> Output);

    /// <summary>Takes --precheck-takeoff, --precheck-e2k, --pin-storey and --no-precheck out of <paramref name="args"/>
    /// (the rest is returned for the tool's own parsing). A flag given without its value is a malformed pair.</summary>
    public static (string? Takeoff, string? E2k, string? Pin, string? NoPrecheck, string? Bad, List<string> Others) Parse(IReadOnlyList<string> args)
    {
        string? takeoff = null, e2k = null, pin = null, why = null, bad = null;
        var others = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            bool ours = a.Equals("--precheck-takeoff", StringComparison.OrdinalIgnoreCase) || a.Equals("--precheck-e2k", StringComparison.OrdinalIgnoreCase)
                     || a.Equals("--pin-storey", StringComparison.OrdinalIgnoreCase) || a.Equals("--no-precheck", StringComparison.OrdinalIgnoreCase);
            if (!ours) { others.Add(a); continue; }
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal)) { bad ??= a + " needs a value"; continue; }
            string v = args[++i];
            switch (a.ToLowerInvariant())
            {
                case "--precheck-takeoff": takeoff = v; break;
                case "--precheck-e2k": e2k = v; break;
                case "--pin-storey": pin = v; break;
                default: why = v; break;
            }
        }
        return (takeoff, e2k, pin, why, bad, others);
    }

    /// <summary>The gate. <paramref name="runProcess"/> runs an exe with arguments and returns its exit code (null = it did
    /// not exit within the timeout, or could not start) and its output lines; tests plant one.</summary>
    public static Verdict Check(string? takeoff, string? e2k, string? pin, string? noPrecheck, string? bad,
        Func<string, IReadOnlyList<string>, (int? Exit, List<string> Lines)>? runProcess = null)
    {
        Verdict No(string why, IReadOnlyList<string>? output = null) =>
            new(false, $"PRECHECK REFUSED - {why}: this tool starts nothing (exit {RefusedExit})", output ?? []);
        if (bad != null) return No(bad);
        bool gate = takeoff != null || e2k != null || pin != null;
        if (noPrecheck != null && gate) return No("--no-precheck and --precheck-* given together: one or the other");
        if (noPrecheck != null)
            return noPrecheck.Trim().Length == 0 ? No("--no-precheck needs a reason")
                 : new(true, $"PRECHECK NOT RUN - stated: {noPrecheck}", []);
        if (takeoff == null || e2k == null)
            return No("no pre-check given: --precheck-takeoff <takeoff.exe> --precheck-e2k <model.e2k> [--pin-storey P], or --no-precheck \"<why>\"");
        if (!File.Exists(takeoff)) return No("--precheck-takeoff: no such file " + takeoff);
        if (!File.Exists(e2k)) return No("--precheck-e2k: no such file " + e2k);

        string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(e2k))).ToLowerInvariant();
        var a = new List<string> { "precheck", "model", "--model", Path.GetFullPath(e2k) };
        if (pin != null) { a.Add("--pin-storey"); a.Add(pin); }
        var (exit, lines) = (runProcess ?? RunProcess)(Path.GetFullPath(takeoff), a);
        string what = $"`takeoff precheck model` on {Path.GetFileName(e2k)} sha256 {sha}{(pin != null ? " --pin-storey " + pin : " (supports: the file's RESTRAINTs)")} by {Path.GetFullPath(takeoff)}";
        if (exit == 0) return new(true, "PRECHECK PASSED - " + what, lines);
        return No(exit == null ? what + " did not exit (timeout or could not start)" : $"{what} exited {exit}", lines);
    }

    /// <summary>Runs the precheck, output captured, at most 10 minutes (01819's 2.4 MB e2k takes seconds on the dev PC).</summary>
    public static (int? Exit, List<string> Lines) RunProcess(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var x in args) psi.ArgumentList.Add(x);
        var lines = new List<string>();
        try
        {
            using var p = Process.Start(psi);
            if (p == null) { lines.Add("could not start: Process.Start gave no process for " + exe); return (null, lines); }
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (lines) lines.Add(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (lines) lines.Add(e.Data); };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            if (!p.WaitForExit(TimeSpan.FromMinutes(10)))
            {
                // killed: wait for it and its redirected output to end, then hand back a COPY taken under the lock, so a
                // late output line can never change the list the caller is walking (review 1927 (a))
                try { p.Kill(true); p.WaitForExit(TimeSpan.FromSeconds(30)); } catch (InvalidOperationException) { }
                lock (lines) { lines.Add("killed: no exit within 10 minutes"); return (null, [.. lines]); }
            }
            p.WaitForExit();
            lock (lines) return (p.ExitCode, [.. lines]);
        }
        catch (System.ComponentModel.Win32Exception e) { lock (lines) { lines.Add("could not start: " + e.Message); return (null, [.. lines]); } }
    }
}
