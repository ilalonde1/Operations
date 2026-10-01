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

    /// <summary>
    /// The largest result, in JSON characters, that is ever sent back. The health probe's is ~100 KB.
    /// A probe that emits PowerShell's rich objects instead of plain values -- a string from
    /// Get-Content, a FileInfo -- serialises their hidden PSDrive/PSProvider graph too: on 2026-09-29
    /// a probe meant to return 1 KB published 105 MB, and the client sat reading it over the VPN.
    /// Over the limit, the target replaces the result with an error saying so; nothing large crosses.
    /// </summary>
    public const int MaxResultChars = 8 * 1024 * 1024;

    public static string Build(string body, string resultPath, int maxResultChars = MaxResultChars)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPath);
        // PowerShell closes a single-quoted string on the typographic quotes too (U+2018-U+201B), not only on '.
        if (resultPath.IndexOfAny(['\'', '‘', '’', '‚', '‛']) >= 0)
            throw new ArgumentException("Result path may not contain a quote of any kind.", nameof(resultPath));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResultChars, 1024);

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
            $json = $r | ConvertTo-Json -Depth 8
            if ($json.Length -gt {{maxResultChars}}) {
                $json = [pscustomobject]@{ Ok = $false; Line = 0; Error = ('result too large: {0:N1} MB of JSON, the limit is {1:N1} MB. The probe returned rich PowerShell objects; return plain values instead ([string] casts, or Select-Object with only the properties needed).' -f ($json.Length / 1MB), ({{maxResultChars}} / 1MB)) } | ConvertTo-Json
            }
            [IO.File]::WriteAllText('{{tmp}}', $json, [Text.UTF8Encoding]::new($false))
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
