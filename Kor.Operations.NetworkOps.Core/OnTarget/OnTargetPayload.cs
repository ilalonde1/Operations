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

    /// <summary>The target-side kill when the caller names none: the network route's own default wait.</summary>
    public static readonly TimeSpan DefaultKillAfter = TimeSpan.FromMinutes(10);

    /// <summary>How deep the output is walked before a value is turned into its string. Probes own their shapes
    /// down to here; the serialiser is then given depth to spare, so it never truncates or walks on its own.</summary>
    public const int PlainDepth = 8;

    /// <summary>How many values the output may hold before the rest are replaced by a truncation marker.</summary>
    public const int MaxPlainValues = 200_000;

    /// <summary>
    /// The payload. Three bounds, all ON the target, because the caller giving up does not stop anything there
    /// (12 Nov 2026, brain #133338: 64 netops calls to KOR-210 each left a powershell.exe spinning in
    /// ConvertTo-Json on Get-Content output -- ~120 GB of commit, ETABS starved for nine hours):
    /// <list type="number">
    /// <item>a watchdog process kills the payload after <paramref name="killAfter"/> -- the body AND the
    /// serialisation -- and publishes an error result saying so;</item>
    /// <item>the output is flattened to plain values (strings, numbers, bools, arrays, ordered maps) before
    /// ConvertTo-Json ever sees it, so no PSDrive/PSProvider graph reaches the serialiser;</item>
    /// <item>when <paramref name="alivePath"/> is given, the payload holds that file open (delete-on-close) for
    /// its whole life, so the caller can see a payload is still alive there and not stack another
    /// (<see cref="OnTargetAlive"/>).</item>
    /// </list>
    /// </summary>
    public static string Build(string body, string resultPath, int maxResultChars = MaxResultChars, TimeSpan? killAfter = null, string? alivePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPath);
        RefuseQuotes(resultPath, nameof(resultPath));
        if (alivePath is not null) RefuseQuotes(alivePath, nameof(alivePath));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResultChars, 1024);
        var kill = killAfter ?? DefaultKillAfter;
        ArgumentOutOfRangeException.ThrowIfLessThan(kill, TimeSpan.FromSeconds(1), nameof(killAfter));
        var killMs = (int)Math.Min(kill.TotalMilliseconds, int.MaxValue);
        var killSeconds = Math.Round(kill.TotalSeconds, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var tmp = resultPath + ".tmp";
        var aliveOpen = alivePath is null ? "$korAlive = $null" :
            $"$korAlive = [IO.FileStream]::new('{alivePath}', [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]'Read, Delete', 4096, [IO.FileOptions]::DeleteOnClose)";
        var head = $$"""
            $ErrorActionPreference = 'Stop'
            {{aliveOpen}}
            # The watchdog: a separate process, because nothing inside this one runs while a cmdlet spins.
            # It waits on THIS process (pid and start time, so a reused pid is never killed), and past the limit
            # publishes an error result, then kills this process and anything it started.
            $korWatchdog = @'
            $ErrorActionPreference = 'SilentlyContinue'
            $p = Get-Process -Id __KORPID__
            if ($p -and $p.StartTime.Ticks -eq __KORTICKS__ -and -not $p.WaitForExit({{killMs}})) {
                if (-not (Test-Path -LiteralPath '{{resultPath}}')) {
                    $j = '{"Ok":false,"Line":0,"Error":"killed on the target after {{killSeconds}} s: the script and its serialisation did not finish in time (pid __KORPID__)"}'
                    [IO.File]::WriteAllText('{{tmp}}.kill', $j, [Text.UTF8Encoding]::new($false))
                    Move-Item -LiteralPath '{{tmp}}.kill' -Destination '{{resultPath}}'
                }
                try { $p.Kill() } catch { }
                Get-CimInstance Win32_Process -Filter "ParentProcessId = __KORPID__" | Where-Object { $_.ProcessId -ne $PID } |
                    ForEach-Object { & taskkill.exe /F /T /PID $_.ProcessId | Out-Null }
            }
            '@
            # Plain values only: a string from Get-Content or a FileInfo carries a PSDrive/PSProvider graph that
            # ConvertTo-Json walks for hours. Strings, numbers, bools and dates pass as their base values; maps and
            # PSCustomObjects keep their shape to depth {{PlainDepth}}; any other .NET object gives one level of its properties.
            $script:korValues = 0
            function ConvertTo-KorPlain($v, [int]$d) {
                if ($null -eq $v) { return $null }
                $script:korValues++
                if ($script:korValues -gt {{MaxPlainValues}}) { return '[truncated: the output holds more than {{MaxPlainValues}} values]' }
                $b = $v.PSObject.BaseObject
                if ($b -is [string] -or $b -is [char]) { return [string]::new(([string]$b).ToCharArray()) }
                if ($b -is [enum]) { return $b.value__ }
                if ($b -is [bool] -or $b -is [datetime] -or $b -is [int] -or $b -is [long] -or $b -is [double] -or $b -is [decimal] -or $b -is [single] -or $b -is [byte] -or $b -is [sbyte] -or $b -is [int16] -or $b -is [uint16] -or $b -is [uint32] -or $b -is [uint64]) { return $b }
                if ($b -is [ValueType] -or $d -le 0) { return [string]::new(([string]$b).ToCharArray()) }
                if ($b -is [Collections.IDictionary]) {
                    $h = [ordered]@{}
                    foreach ($k in @($b.Keys)) { $h[[string]$k] = ConvertTo-KorPlain $b[$k] ($d - 1) }
                    return $h
                }
                if ($b -is [Collections.IEnumerable]) {
                    $list = [Collections.Generic.List[object]]::new()
                    foreach ($i in $b) { $list.Add((ConvertTo-KorPlain $i ($d - 1))) }
                    return ,$list.ToArray()
                }
                $custom = $b -is [Management.Automation.PSCustomObject]
                # A rich object inside a rich object is its string: one level, never a graph.
                if (-not $custom -and $d -le 1) { return [string]::new(([string]$b).ToCharArray()) }
                $inner = if ($custom) { $d - 1 } else { 1 }
                $h = [ordered]@{}
                foreach ($p in $v.PSObject.Properties) {
                    try { $h[$p.Name] = ConvertTo-KorPlain $p.Value $inner } catch { $h[$p.Name] = '[error: ' + $_.Exception.Message + ']' }
                }
                return $h
            }

            $r = try {
                $korSelf = Get-Process -Id $PID
                $korWatchdog = $korWatchdog.Replace('__KORPID__', [string]$PID).Replace('__KORTICKS__', [string]$korSelf.StartTime.Ticks)
                $korEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($korWatchdog))
                Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -WindowStyle Hidden -ArgumentList '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $korEncoded
                $o = & {
            """;
        // Line numbers are reported relative to the body, as the caller wrote it, not to this wrapper.
        var bodyFirstLine = head.Count(c => c == '\n') + 2;
        var tail = $$"""
                }
                [ordered]@{ Ok = $true; Output = (ConvertTo-KorPlain @($o) {{PlainDepth + 1}}) }
            } catch {
                $korLine = [int]$_.InvocationInfo.ScriptLineNumber
                if ($korLine -ge {{bodyFirstLine}}) { $korLine -= {{bodyFirstLine - 1}} }
                [ordered]@{ Ok = $false; Error = [string]$_.Exception.Message; Line = $korLine }
            }
            $json = $r | ConvertTo-Json -Depth {{PlainDepth + 4}}
            if ($json.Length -gt {{maxResultChars}}) {
                $json = [ordered]@{ Ok = $false; Line = 0; Error = ('result too large: {0:N1} MB of JSON, the limit is {1:N1} MB. The probe returned rich PowerShell objects; return plain values instead ([string] casts, or Select-Object with only the properties needed).' -f ($json.Length / 1MB), ({{maxResultChars}} / 1MB)) } | ConvertTo-Json
            }
            [IO.File]::WriteAllText('{{tmp}}', $json, [Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath '{{tmp}}' -Destination '{{resultPath}}' -Force
            if ($korAlive) { $korAlive.Dispose() }
            """;
        return head + "\n" + body + "\n" + tail;
    }

    // PowerShell closes a single-quoted string on the typographic quotes too (U+2018-U+201B), not only on '.
    private static void RefuseQuotes(string path, string name)
    {
        if (path.IndexOfAny(['\'', '‘', '’', '‚', '‛']) >= 0)
            throw new ArgumentException("A path may not contain a quote of any kind.", name);
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
