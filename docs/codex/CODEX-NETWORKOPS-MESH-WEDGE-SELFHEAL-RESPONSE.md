# NetworkOps Mesh wedge self-heal — source response

Date: 2026-10-15

Implemented the six pieces as source edits. No build, test run, render, payload execution,
live-machine access, publish, deployment, or commit was performed.

Prior art checked: the supplied brief; `FixCatalog`, `start-service.ps1`, the Core embed glob,
the Windows branch and result reader in `ActionRunner`, `MeshRules`, the viewer model/window,
the device window's fix flow, client request/action contracts, both gate classes, and existing
responses under `docs/codex/`.

## What changed

1. **Catalog.** Added `FixCatalog.RestartMeshAgent = "restart-mesh-agent"` and the requested
   wording. It is non-disruptive, has a 120-second timeout, takes no parameter, and is offered
   for `mesh-silent` only. `Target` is omitted and therefore stays `FixCatalog.Windows`.
   There is no `"*"` family. `Invalid` needs no new case, and `ParamFromFinding` is unchanged.

2. **Script.** Added `Actions/restart-mesh-agent.ps1`. Like `start-service.ps1`, it emits one
   `[pscustomobject]`, not an explicit array. It reports an absent service and returns; otherwise
   it force-kills the service's PID, waits five seconds, starts the service, waits five seconds,
   and reports `Result`, `Installed`, `Running`, `Before`, `After`, and `Est443`. Kill/start
   exceptions are preserved in the result. It never calls `Stop-Service`.
   The existing `Actions\*.ps1` embedded-resource wildcard and `FixCatalog.Script` resolve it;
   neither the Core project nor the runner needed an edit.

3. **Pure viewer decision.** Added `StuckAfter` (20 seconds), `LooksWedged`, and `WedgedHint`
   to `KorRemoteViewerModel`. `LooksWedged` includes an `IsReady` guard: the brief's literal
   expression lacked it, but the required gate explicitly excludes not-ready pages. Only a
   ready desktop in state 1 or 2 at/after the threshold qualifies. The model also holds the
   overlay prompt state and clears it when the page leaves the qualifying states.

4. **Viewer fix flow.** Added a dispatcher watchdog using elapsed `Stopwatch` time. Startup,
   navigation/reload, explicit Connect, and incoming connecting/setup states start attempts;
   repeated state 1/2 messages preserve the original start time. The prompt is offered once
   per attempt. Connected, login, disconnected, incompatible pages, navigation resets, and
   window closure clear the old attempt. The existing overlay contains the restart button.
   WebView2 is hidden while that prompt is visible so its child HWND cannot cover the button.

   The device window's `FixAsync` takes a finding and opens a chooser, so the button uses its
   existing `ViewModel.Client.RequestFixAsync` with `ViewModel.Device.DeviceId`,
   `FixCatalog.RestartMeshAgent`, and `Confirmed: false`. No modal was added. Refusals and
   unexpected confirmation requirements are shown in `Toast` without an automatic confirmed
   retry. The viewer polls `GetActionAsync` every three seconds, follows terminal statuses,
   prevents duplicate clicks while following the action, and cancels local polling on close.
   After five minutes it reports a pending action and leaves it available in NetworkOps history.

   `ActionRunner` marks a normally returned Windows payload `Done` even if its result says the
   service is absent or stopped. Therefore automatic reconnect additionally requires the
   payload's `Installed == true` and `Running == true`. A pure helper with authored gate cases
   reads either the single object or its transport array; missing/malformed output does not trigger reconnect.
   The action detail is shown in `Toast`. Success uses `NavigateTo(_url)`, resetting `_reroutes`
   and assigning `View.Source = new Uri(_url)` for the next attempt. Zero established 443
   connections is reported by the script but does not prevent retrying a running agent.

5. **Versions.** NetworkOps Service: `0.60.0` -> `0.61.0`. App: `2026.10.12` -> `2026.10.15`
   for assembly, file, and informational versions. The App bump is in `AssemblyInfo.cs`, not
   its project file: `GenerateAssemblyInfo=false` makes a project `<Version>` ineffective,
   as the existing project comment explains. The App project is unchanged.

6. **Gates authored, not run.** Edited these existing classes without weakening their guards:

   - `Kor.Operations.NetworkOps.Tests/FixCatalogTests.cs`: exact id, Windows target,
     non-disruptive flag, timeout, family restriction, no-input validation, unaffected finding
     parameter behavior, and PID-kill/start script guards. The existing embedded-script and
     plain-value gates also include the new entry.
   - `Kor.Operations.App/Kor.Transmittals.App.Tests/NetworkOps/KorRemoteViewerTests.cs`:
     states 1/2 immediately before, at, and after the threshold; connected, login, disconnected,
     absent-device, missing-function, and initial states; overlay notifications and clearing;
     successful/failed/refused/pending action results and absent/malformed payload evidence.

## Deliberately not done

- **No background `mesh-wedged` rule.** `MeshRules` and the background sweep are unchanged.
  The connected-but-wedged condition is inferred only in the viewer.
- **Target stays Windows.** No Mesh-agent execution path was introduced for this fix.
- No GPO, Windows Update, MeshCentral listing-account, database, or machine changes.
- No solution/project build, tests, render harness, live Connect, script execution, publish,
  apply/deploy, or commit. The authored gates have not been demonstrated failing before the
  change or passing after it. Live recovery and visual rendering remain for the follow-up run.
- The three pre-existing untracked insurance PDFs and the untracked Calgary vocabulary file
  were left untouched.

## Static verification

- Read back the source diff and new script; checked the actual client/DTO members, script
  resource naming, result handling, and version source.
- `git diff --check`: clean.
- Task scope: eight tracked files edited and two files created (the script and this response).
  No deletes. The App/Core project files, action runner, and mesh health rules are unchanged.

The model tests do not exercise WebView2 rendering, dispatcher timing, live API polling, SCM
reachability, or successful desktop negotiation after restart. A service that is running but
still cannot open a desktop remains a same-class fault for the live follow-up check.
