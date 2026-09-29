#nullable enable
using System.Text;
using System.Text.Json;

namespace Kor.Operations.NetworkOps.Core.OnTarget;

// The script that actually runs, as SYSTEM, on a workstation: the caller's body, its output
// captured (or its error), serialised to JSON, published atomically. Pure -- no I/O.
//
// Why the work runs on the target at all: anything that walks a registry hive, a file tree or
// an event log is one network round trip per key/file/record, and Ian is on the VPN almost
// all the time. reg.exe /s against one uninstall hive took 5+ minutes over the VPN; run on
// the machine it takes under a second. So only two things cross the wire: one service call
// and one small JSON file back (2026-09-28, feedback_run_on_the_target_never_chatty_over_vpn).
//
// The same contract as New-KorOnTargetPayload in tools/WorkstationOps, so a probe written for
// one runs under the other.
public static class OnTargetPayload
{
    /// <summary>
    /// Windows PowerShell 5.1 -- what every workstation here runs -- reads a BOM-less script as
    /// ANSI, so one em-dash in a probe turns the whole file into a parse error and nothing runs
    /// (the timeout is the only symptom). Scripts are always written with this encoding.
    /// </summary>
    public static readonly Encoding ScriptEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string Build(string body, string resultPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPath);
        if (resultPath.Contains('\'')) throw new ArgumentException("Result path may not contain a single quote.", nameof(resultPath));

        var tmp = resultPath + ".tmp";
        return $$"""
            $ErrorActionPreference = 'Stop'
            $r = try {
                $o = & {
            {{body}}
                }
                [pscustomobject]@{ Ok = $true; Output = @($o) }
            } catch {
                [pscustomobject]@{ Ok = $false; Error = $_.Exception.Message; Line = $_.InvocationInfo.ScriptLineNumber }
            }
            [IO.File]::WriteAllText('{{tmp}}', ($r | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath '{{tmp}}' -Destination '{{resultPath}}' -Force
            """;
    }

    /// <summary>Reads the JSON the payload publishes. Output stays raw JSON: probes own their shapes.</summary>
    public static OnTargetResult ParseResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("Ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        if (!ok)
        {
            var err = root.TryGetProperty("Error", out var e) ? e.GetString() : null;
            var line = root.TryGetProperty("Line", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : (int?)null;
            return new OnTargetResult(false, null, err ?? "unknown error", line);
        }
        // PowerShell serialises a one-element array as the element itself; normalise to an array.
        var output = root.TryGetProperty("Output", out var o) ? o : default;
        var raw = output.ValueKind switch
        {
            JsonValueKind.Array => output.GetRawText(),
            JsonValueKind.Undefined or JsonValueKind.Null => "[]",
            _ => $"[{output.GetRawText()}]",
        };
        return new OnTargetResult(true, raw, null, null);
    }
}

/// <param name="OutputJson">The body's output as a JSON array, when <paramref name="Ok"/>.</param>
public sealed record OnTargetResult(bool Ok, string? OutputJson, string? Error, int? Line);
