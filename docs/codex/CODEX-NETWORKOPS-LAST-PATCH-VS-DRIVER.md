# NetworkOps — "last patched" must mean a real OS patch, not a driver

Bounded brief for Codex. **Source edits only** — you do not build, run, test, apply, deploy or commit.
Response goes beside this file as `CODEX-NETWORKOPS-LAST-PATCH-VS-DRIVER-RESPONSE.md`, in the same shape as
the other CODEX responses (what changed per piece, what was deliberately not done, `git diff --check`
clean, do not commit). Claude builds, tests, verifies the binary, installs and deploys afterward.

## Why this exists (grounded, not hypothetical)

The health probe records the single most-recent **successful** Windows-Update history entry as the device
page's "Last update installed" and as the date the `not-patched` rule counts from. It already drops Defender
signatures and Store churn — but **not drivers**. So a third-party driver push becomes the machine's
"last patched", and worse, it **resets the `not-patched` clock**, hiding a genuine patch gap. This is live
in committed fixtures:

- `Fixtures/health-v2/KOR-1001.json`: `LastInstall 2026-09-29 · "Silicon Laboratories Inc. - Ports - 11.2.0.167"`
  by `pwsh.exe` — a **serial-port driver**, shown as the last update installed.
- `KOR-104N` / `KOR-202`: `"Update for Windows (KB5124010)"` by `wusa` — genuine patches.

The discriminator, confirmed against that real data: **a genuine Windows/Office security or quality update
carries a `KB#####` in its title; a driver is `Vendor - Category - version` with no KB.**

## The decisions, so you build ONE thing

- **The classification is C#, not PowerShell.** The probe stops collapsing history to one scalar and instead
  reports the recent successful history as a small list. A pure, unit-tested C# classifier decides which
  entries are real patches. (Repo doctrine: PowerShell is prototype-only; an untested regex in a `.ps1` is
  exactly the kind of instrument this repo distrusts.)
- **Discriminator = a KB number in the title.** `\bKB\d{5,}\b`, case-insensitive. Covers cumulative/quality
  rollups, servicing-stack, .NET, MSRT (KB890830), and Office MSI KBs. Everything else succeeded-and-kept
  (drivers, odd vendor entries) is "other".
- **Back-compat, no fixture faking.** New probe (v14) sends `Recent[]`. Pre-v14 captures (every committed
  fixture) sent the three scalars and have no `Recent`. `UpdateInfo` reads **whichever is present** and
  exposes the same computed `LastInstall*` the rest of the code already uses — so existing fixtures behave
  exactly as today (zero fixture churn) and only fresh captures get the improved classification.
- **Scope is `UpdateInfo` and its readers only.** Do **not** touch the Updates view, the scan/install flow,
  `UpdatesApi`, or any service DTO. (`UpdateRow.LastInstall` in the Updates view comes from the Actions
  table — console install runs — not from `UpdateInfo`; leave it alone.)

## Pieces

### Piece 1 — probe: report the recent history, don't pre-judge it
`Kor.Operations.NetworkOps.Core/Probes/health.ps1`

Replace the body of the `$update = Try-Block 'update' { ... }` block (currently ~lines 258-271). Keep
`MicrosoftUpdate` and `NoAutoUpdate` exactly as they are. Replace the three `LastInstall*` scalars with a
`Recent` list of the kept successful entries (newest first), each `{ Title; Date (round-trip 's'); By }`:

```powershell
# --- patching: who schedules it, and the recent successful install history (classified in C#, not here)
$update = Try-Block 'update' {
    $sm = New-Object -ComObject Microsoft.Update.ServiceManager
    $mu = [bool]($sm.Services | Where-Object { $_.ServiceID -eq '7971f918-a847-4430-9279-4a52d1efe18d' -and $_.IsRegisteredWithAU })
    $au = Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' -ErrorAction SilentlyContinue
    # Succeeded installs (ResultCode 2), newest first, minus the daily Defender-signature / Store churn that is
    # never a "patch". Drivers AND OS patches are both kept: which is which is decided in tested C#
    # (Health/UpdateHistory.cs), never by a regex in this script.
    $hist = @((New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher().QueryHistory(0, 50) |
        Where-Object { $_.ResultCode -eq 2 -and $_.Title -notmatch '^9N|Store|Defender|Security Intelligence' } |
        Sort-Object Date -Descending)
    [pscustomobject]@{
        MicrosoftUpdate = $mu
        NoAutoUpdate    = if ($au) { $au.NoAutoUpdate } else { $null }
        Recent          = @($hist | ForEach-Object { [pscustomobject]@{ Title = $_.Title; Date = $_.Date.ToString('s'); By = $_.ClientApplicationID } })
    }
}
```

Also bump the probe version in the final output object (the `ProbeVersion  = 13` line, ~line 438) to
**`ProbeVersion  = 14`** — keep the exact existing spacing (two spaces before `=`).

### Piece 2 — model: raw in, classified out
`Kor.Operations.NetworkOps.Core/Health/HealthSnapshot.cs`

Add an item record and reshape `UpdateInfo` (currently the single line ~179). Keep it STJ-friendly. The two
probe fields stay positional; the raw history inputs are `[JsonPropertyName]` init props; the derived values
are `[JsonIgnore]` computed getters so stored health JSON never round-trips a stale classification:

```csharp
public sealed record UpdateHistoryItem(string Title, DateTime Date, string? By);

public sealed record UpdateInfo(bool MicrosoftUpdate, int? NoAutoUpdate)
{
    // The deserialization boundary. Probe v14+ sends Recent; pre-v14 captures sent the three scalars and
    // have no Recent. Classify from whichever is present, so old fixtures behave exactly as before.
    [JsonPropertyName("Recent")]            public IReadOnlyList<UpdateHistoryItem>? Recent { get; init; }
    [JsonPropertyName("LastInstall")]       public DateTime? RawLastInstall { get; init; }
    [JsonPropertyName("LastInstallTitle")]  public string? RawLastInstallTitle { get; init; }
    [JsonPropertyName("LastInstallBy")]     public string? RawLastInstallBy { get; init; }

    /// <summary>The last genuine OS security/quality update (KB-tagged) — the authoritative "last patched".</summary>
    [JsonIgnore] public UpdateHistoryItem? LastPatch => UpdateHistory.LastPatch(Recent)
        ?? (RawLastInstall is { } d ? new UpdateHistoryItem(RawLastInstallTitle ?? "", d, RawLastInstallBy) : null);

    /// <summary>The last non-patch install that got through the probe filter (driver / vendor / other); null on pre-v14 data.</summary>
    [JsonIgnore] public UpdateHistoryItem? LastOther => UpdateHistory.LastOther(Recent);

    [JsonIgnore] public DateTime? LastInstall => LastPatch?.Date;
    [JsonIgnore] public string? LastInstallTitle => LastPatch?.Title;
    [JsonIgnore] public string? LastInstallBy => LastPatch?.By;
}
```

Add `using System.Text.Json.Serialization;` if not already present. Grep for `new UpdateInfo(` across the
solution and fix any positional construction to the new shape (there is likely none outside deserialization —
list what you found in the response). Do not change any other record in this file.

### Piece 3 — the classifier (new, pure, the thing the test pins)
`Kor.Operations.NetworkOps.Core/Health/UpdateHistory.cs` (new file)

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.NetworkOps.Core.Health;

/// <summary>
/// Which Windows-Update history entries are genuine OS patches vs drivers/other.
/// A real Windows/Office security or quality update carries a KB number in its title
/// ("2026-10 Cumulative Update ... (KB5127648)", "Servicing Stack ... (KBxxxxxxx)",
/// ".NET ... (KBxxxxxxx)", "Update for Windows (KB5124010)", MSRT "(KB890830)").
/// A driver is "Vendor - Category - version" with no KB
/// ("Silicon Laboratories Inc. - Ports - 11.2.0.167", "NVIDIA - Display - 31.0.15.3699").
/// Defender signatures and Store apps are already dropped upstream in the probe.
/// COVERS: KB-tagged Microsoft/Office updates; excludes driver/vendor entries.
/// DOES NOT: specially classify a feature-update or out-of-band whose title carries no KB (rare —
/// it reads as "other", so a box freshly on a new build with no later KB can show an older patch
/// date); and it classifies by TITLE only, so a renamed/odd title is a same-class fault it misses.
/// </summary>
public static class UpdateHistory
{
    private static readonly Regex Kb = new(@"\bKB\d{5,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsOsPatch(string? title) => title is not null && Kb.IsMatch(title);

    public static UpdateHistoryItem? LastPatch(IReadOnlyList<UpdateHistoryItem>? recent)
        => recent?.Where(h => IsOsPatch(h.Title)).OrderByDescending(h => h.Date).FirstOrDefault();

    public static UpdateHistoryItem? LastOther(IReadOnlyList<UpdateHistoryItem>? recent)
        => recent?.Where(h => !IsOsPatch(h.Title)).OrderByDescending(h => h.Date).FirstOrDefault();
}
```

Author the regex literally with your editor as the verbatim C# string shown (`@"\bKB\d{5,}\b"`). Do not
generate it through any string processing (a `\b` outside a C# verbatim/raw string is not a word boundary).

### Piece 4 — device page: name it honestly, keep the driver visible but not as the patch
`Kor.Operations.NetworkOps.Core/Health/PcComponents.cs` (the OS component `info:` rows, ~lines 82-89)

- Rename the existing `"Last update installed"` row label to **`"Last security/quality update"`**. Its value
  expression is unchanged (it already reads `s.Update.LastInstall` / `LastInstallTitle`, which now resolve to
  the last real patch).
- Add one row directly after it, shown **only when a driver/other is newer than the last patch** (or there is
  no patch on record) — so the recent driver is explained, never mistaken for the patch:

```csharp
("Last driver / other update",
    s.Update?.LastOther is { } o && (s.Update.LastPatch is null || o.Date > s.Update.LastPatch.Date)
        ? $"{o.Date.ToLocalTime():yyyy-MM-dd}" + (o.Title is { Length: > 0 } ot ? $" · {ot}" : "") : null),
```

Leave the `"Microsoft Update"` row and everything else in the block exactly as is.

### Piece 5 — the test that finds the whole class (rule 11)
`Kor.Operations.NetworkOps.Tests/UpdateHistoryTests.cs` (new file)

Pin the classifier and the rule together. Include a short header stating what it covers and does **not** (copy
the COVERS/DOES NOT wording from Piece 3), plus: *this is a classification unit test, not a differential over
the live probe — that `Recent` actually arrives populated is proven live, not here.* Cases:

1. A `Recent[]` mixing a cumulative KB patch, a servicing-stack KB, a .NET KB, MSRT `(KB890830)`, and several
   drivers incl. the exact `"Silicon Laboratories Inc. - Ports - 11.2.0.167"` and `"NVIDIA - Display - 31.0.15.3699"`
   → `LastPatch` is the newest KB-tagged entry; `LastOther` is the newest driver; **no driver is ever `LastPatch`**.
2. The masquerade case: a driver dated **after** the newest KB patch → `LastPatch` stays the (older) KB,
   `LastOther` is the driver.
3. Null/empty `Recent` → both null.
4. `UpdateInfo` back-compat: an `UpdateInfo` built from only the legacy scalars (no `Recent`, as old fixtures
   deserialize) → `LastInstall`/`LastInstallTitle` equal those scalars (behavior unchanged), `LastOther` null.
5. Rule wiring through `HealthRules.Evaluate` (mirror the existing `not-patched` test setup — set
   `CollectedAt`): last **KB patch** > 35 days old while a driver is 3 days old → `not-patched` **fires**
   (the driver no longer masks the gap); a KB patch 3 days old → `not-patched` does **not** fire even with an
   old driver present.

### Piece 6 — the one version coupling
`Kor.Operations.NetworkOps.Tests/HeadlessDisplayTests.cs` (~line 100)

Change the pinned literal `Assert.Contains("ProbeVersion  = 13", probe);` to
`Assert.Contains("ProbeVersion  = 14", probe);` (two spaces, matching the `.ps1`). This is the only test that
asserts the probe version.

## Deliberately not done (confirm in the response)

- No change to `NetworkOpsUpdatesView`, `UpdatesApi`, `UpdateRow`, `UpdateScanner`, any service endpoint, or
  any DTO other than `UpdateInfo`.
- No fixture files regenerated or hand-edited (back-compat keeps every committed fixture valid).
- `microsoft-update-off` and the reboot rules unchanged. No new `NEED`/catalog/version-bump of the service or
  app project files (Claude decides the service version at deploy; this is a probe + Core + test change).

## Claude's run after apply (not yours)

Build Core + App + the NetworkOps test project (`set -o pipefail`); run `UpdateHistoryTests`, `HealthRulesTests`,
`HeadlessDisplayTests`, `ProbeContractTests`; prove the shipped Core.dll carries `UpdateHistory` + the embedded
`health.ps1` carries `ProbeVersion  = 14` and the `Recent` emission (UTF-8 for member names, UTF-16 for the
embedded-script literal); deploy the service to APP01 on Ian's word; fleet recheck; confirm on the device page
that "Last security/quality update" shows the last real KB (and KOR-1001 shows its Silicon-Labs driver on the
"Last driver / other update" line, not as the patch).

## Read bound (~16 KB)

- `health.ps1` — the `$update` block (~lines 255-272) and the final output object around `ProbeVersion` (~lines 430-445).
- `HealthSnapshot.cs` — the `UpdateInfo` record (~line 179) and the `Update` property on `HealthSnapshot` (~line 25).
- `HealthRules.cs` — ~lines 124-138 (the `not-patched` / `microsoft-update-off` block).
- `PcComponents.cs` — ~lines 78-90 (the OS component `info:` rows).
- `HeadlessDisplayTests.cs` — ~lines 95-101 (the `ProbeVersion` assert) and one existing `not-patched` test in
  `HealthRulesTests.cs` to mirror its setup.
- Grep the solution for `new UpdateInfo(`.

Do not open MeshCentral, the database, or any machine. No new files except `UpdateHistory.cs`,
`UpdateHistoryTests.cs`, and the RESPONSE. No deletes.

## Response

What changed per piece; confirm the scope limits above held, the classifier regex is a verbatim C# literal,
`UpdateInfo` computed members are `[JsonIgnore]` and the raw inputs `[JsonPropertyName]`, and `git diff --check`
is clean. Do not build, run, apply, deploy, or commit.
