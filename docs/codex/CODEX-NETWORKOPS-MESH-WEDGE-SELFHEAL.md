# NetworkOps — restart a wedged Mesh agent, and self-heal the wedge from the viewer

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-NETWORKOPS-MESH-WEDGE-SELFHEAL-RESPONSE.md`, in the same
format the other CODEX responses use (what changed per piece, what was deliberately not done,
`git diff --check` clean, do not commit).

## Why this exists

On 2026-10-15 KOR-206's MeshCentral agent was **Running but wedged**: it held its control link to
KOR-MESH01 — so MeshCentral still reported it `conn`-connected and the KOR Remote viewer reached
**State 2 "Setting up…"**, not "offline" — while its desktop session never negotiated. Connect
showed black; the `Mesh Agent` service would not even stop cleanly (stuck in `Stop Pending`). It
cleared by hand: force-kill the agent process by PID, then `Start-Service` (NetworkOps action 788,
which then showed a fresh `:443` established back to MESH01).

NetworkOps gets exactly one per-agent signal from MeshCentral — `conn`, a bitflag (see
`MeshCentralClient.MeshNode`). So `mesh-silent` can only ever see "agent socket down." A
wedged-but-connected agent is invisible to the background sweep by construction. This brief does
two things and no more: (A) makes the hand-fix a first-class, audited catalog fix; (B) detects the
wedge where it is actually observable — the viewer, when a Connect sits at Connecting/Setting-up
past a threshold — and offers that fix there.

## The decisions, so you build the one thing

- **No new background rule.** Do not add a `mesh-wedged` sweep rule. We have one instance and no
  clean captured telemetry, and MeshCentral's `conn` cannot see this mode. A background detector is
  a later phase gated on a second instance. If a piece seems to need one, stop and say so.
- **The restart fix runs through the SCM channel, never through Mesh.** The agent is the thing
  that is down, so `Target` is the default `FixCatalog.Windows`. `Target = MeshAgent` would route
  through `RunViaMeshAsync` and is wrong here.
- **Kill by PID, do not `Stop-Service`.** `Stop-Service` is exactly what hung on 206 (the wedged
  service would not stop). Mirror action 788: `Stop-Process -Id <pid> -Force`, then `Start-Service`.
- **Detection lives in the viewer, and the decision is pure.** The time-independent "looks wedged"
  test goes in `KorRemoteViewerModel` (unit-tested without WebView2, as that class already is); the
  window supplies elapsed time via a watchdog like the existing `StartWatchdog`.
- **Reuse the fix path and the overlay the app already has.** Invoke the fix through the device
  window's existing flow / `NetworkOpsClient.RequestFixAsync`; surface the prompt in the existing
  `Overlay`/`ShowsOverlay` space — no new modal window.
- **Nothing about GPO / Windows Update. The read-only MeshCentral listing account is untouched.**

## Pieces

### Piece 1 — the catalog fix (`Kor.Operations.NetworkOps.Core/Actions/FixCatalog.cs`)

- Add a constant beside the others: `public const string RestartMeshAgent = "restart-mesh-agent";`
- Add one entry to `All`, a **Windows-target** fix (omit `Target`), matching the record shape and
  the house wording of its neighbours:

  ```csharp
  new(RestartMeshAgent, "Restart the remote-control agent",
      "Force-restarts this PC's MeshCentral (KOR Remote) agent -- kills the agent process and starts the service again -- for when the agent is connected but a Connect will not render because its desktop session is wedged. Briefly drops any Mesh session to this PC; the user's own work is untouched. Runs as SYSTEM through the audited SCM channel, not through the agent, so it reaches domain PCs -- a workgroup box (BK01) reachable only via Mesh cannot be fixed this way.",
      Disruptive: false, TimeoutSeconds: 120, ["mesh-silent"]),
  ```

- Offered for `mesh-silent` and invokable by id (Piece 4 calls it by id). Do **not** give it `"*"`.
- Confirm `Invalid` needs no case (it takes no param) and `ParamFromFinding` is unaffected.

### Piece 2 — the script (`Kor.Operations.NetworkOps.Core/Actions/restart-mesh-agent.ps1`)

Emit the **same result shape** `Actions/start-service.ps1` uses (so `ActionRunner.ResultLine` reads
its `.Result`); read that sibling first and match its object-vs-array form exactly. Logic mirrors
action 788:

```powershell
$name = 'Mesh Agent'
$svc  = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
# if absent: Result = "Mesh Agent service is not installed on this PC", Installed = $false, and stop.
$before = '{0}/{1} pid={2}' -f $svc.State, $svc.StartMode, $svc.ProcessId
$killed = ''
if ($svc.ProcessId) {
    try { Stop-Process -Id $svc.ProcessId -Force -ErrorAction Stop; $killed = "killed pid $($svc.ProcessId)" }
    catch { $killed = "could not kill pid $($svc.ProcessId): $($_.Exception.Message)" }
}
Start-Sleep -Seconds 5
$started = ''
try { Start-Service -Name $name -ErrorAction Stop; $started = 'started' } catch { $started = "start failed: $($_.Exception.Message)" }
Start-Sleep -Seconds 5
$svc2  = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
$after = if ($svc2) { '{0}/{1} pid={2}' -f $svc2.State, $svc2.StartMode, $svc2.ProcessId } else { 'gone' }
$est = 0
if ($svc2 -and $svc2.ProcessId) {
    $est = @(Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue |
        Where-Object { $_.OwningProcess -eq $svc2.ProcessId -and $_.RemotePort -eq 443 }).Count
}
# Result = "Mesh Agent: <before> -> <after> (<killed>; <started>; 443-established=<est>)"
# fields: Installed=$true, Running=($svc2 -and $svc2.State -eq 'Running'), Before, After, Est443=$est
```

Confirm the script is picked up by the existing `Actions\*.ps1` embed glob in the Core `.csproj`;
add an explicit `<EmbeddedResource>` only if there is no wildcard. `FixCatalog.Script` resolves it
with no code change.

### Piece 3 — the wedge decision (`Kor.Operations.App/NetworkOps/KorRemoteViewerModel.cs`)

Pure, testable, no timers:
- `public static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(20);`
- `public bool LooksWedged(TimeSpan trying) => _page is { Page: "desktop", State: 1 or 2 } && trying >= StuckAfter;`
  (false on the login page, before `StuckAfter`, and once `IsConnected`).
- `public string WedgedHint => "This PC's remote-control agent is connected but the screen won't start -- it looks wedged. Restart the agent?";`

### Piece 4 — the viewer offers the fix (`KorRemoteViewerWindow.xaml.cs` + `.xaml`)

- Add a watchdog `DispatcherTimer` (copy `StartWatchdog`): start/restart it when a connect attempt
  begins (`StartAsync`, `NavigateTo`, and in `OnPage` while State is 1/2 and not connected);
  measure elapsed since the attempt began; when `_vm.LooksWedged(elapsed)` first becomes true and we
  have not already offered this attempt, show the restart prompt in the existing overlay space
  (reuse `Overlay`/`ShowsOverlay`; add a button — no new window). Stop it on `State == 3`, on
  `NavigateTo`, and on `Closed`. Offer once per attempt; do not spam.
- The button runs `restart-mesh-agent` through the device window's existing flow if it can take a
  fix id, else `NetworkOpsClient.RequestFixAsync(deviceId, <FixRequest with fixId = FixCatalog.RestartMeshAgent>, ct)`
  (match the real `FixRequest` members and the `deviceId` reachable from `_device.ViewModel`), poll
  `GetActionAsync` to completion, show the outcome in `Toast`, and on success reload the viewer
  (`_reroutes = 0; View.Source = new Uri(_url);`) so it reconnects to the restarted agent. Handle the
  refusal / needs-confirmation returns (non-disruptive, so confirmation should not arise).
- Reference `FixCatalog.RestartMeshAgent` from Core; do not hard-code the string in the App.

### Piece 5 — versions

Bump `<Version>` in `Kor.Operations.NetworkOps.Service` (a new catalog fix ships server-side) and
the App's version, so the deploy is identifiable.

### Piece 6 — gates

- **`Kor.Operations.NetworkOps.Tests/FixCatalogTests.cs`:** `restart-mesh-agent` exists; id is
  exactly `"restart-mesh-agent"`; `Disruptive == false`; `Target == FixCatalog.Windows`; offered by
  `For("mesh-silent")`; `Invalid` returns null for a null param. If a test asserts every
  Windows-target fix has an embedded script, the new script must satisfy it.
- **`Kor.Operations.App/Kor.Transmittals.App.Tests/NetworkOps/KorRemoteViewerTests.cs`** (and
  `NetworkOpsViewModelTests.cs` if that is where the model is covered): `LooksWedged` is false at
  `State == 3`, false before `StuckAfter`, true at `State == 1/2` once `trying >= StuckAfter`, false
  on the login page and when not ready.
- Do not weaken or delete existing guards to make them pass.

## Claude's run after apply (not yours)

Build the NetworkOps Service + Core + their test project and the App + App test project; run the two
gate classes; render the viewer (the App render harness) and read it; publish the Service to APP01
and install the App locally on KOR-1001 on Ian's word; prove the deployed binaries carry the change
(grep the UTF-16 bytes of `restart-mesh-agent`); then recheck a Connect against a real PC.

## Read bound

About 70 KB:
- `Kor.Operations.NetworkOps.Core/Actions/FixCatalog.cs` (whole) and `Actions/start-service.ps1`;
  the Core `.csproj` (the `Actions\*.ps1` embed).
- `Kor.Operations.NetworkOps.Service/Sweep/ActionRunner.cs` lines 100–162 (the Windows-channel
  path only — confirm no runner change is needed); `Kor.Operations.NetworkOps.Service/*.csproj` for
  the `<Version>`.
- `Kor.Operations.NetworkOps.Core/Health/MeshRules.cs` (whole — to see what `mesh-silent` is, and
  to confirm you are NOT changing it).
- `Kor.Operations.App/NetworkOps/KorRemoteViewerModel.cs` (whole),
  `KorRemoteViewerWindow.xaml.cs` (whole), `KorRemoteViewerWindow.xaml` (the toolbar/overlay).
- `Kor.Operations.App/NetworkOps/NetworkOpsClient.cs` lines 126–171 (`GetFixesAsync`,
  `RequestFixAsync`, `FixRequest`, `GetActionAsync`); `NetworkOpsDeviceViewModel.cs` enough to find
  the device id and the `_device.FixAsync` signature; the App `.csproj` for its version.
- `Kor.Operations.NetworkOps.Tests/FixCatalogTests.cs`;
  `Kor.Operations.App/Kor.Transmittals.App.Tests/NetworkOps/KorRemoteViewerTests.cs`.

Do not open the live database, MeshCentral, or any machine. App projects with `UseWPF` have no
implicit `System.IO` — add `using System.IO;` in any new App file that needs it. Any C# regex must
be authored as a raw string, not assembled through an escaped one.

## Files

Creates: `Kor.Operations.NetworkOps.Core/Actions/restart-mesh-agent.ps1`; the RESPONSE.

Edits: `Kor.Operations.NetworkOps.Core/Actions/FixCatalog.cs`; the Core `.csproj` (only if the embed
is not a wildcard); `Kor.Operations.NetworkOps.Service/*.csproj` (`<Version>`);
`Kor.Operations.App/NetworkOps/KorRemoteViewerModel.cs`, `KorRemoteViewerWindow.xaml.cs`,
`KorRemoteViewerWindow.xaml`; the App `.csproj` (version);
`Kor.Operations.NetworkOps.Tests/FixCatalogTests.cs`;
`Kor.Operations.App/Kor.Transmittals.App.Tests/NetworkOps/KorRemoteViewerTests.cs`.

No deletes.

## Response

What changed per piece; what was deliberately not done (name the no-background-rule decision and
confirm `Target` stayed `Windows`); the two gate classes added/edited; `git diff --check` clean. Do
not build, run, apply, deploy, or commit.
