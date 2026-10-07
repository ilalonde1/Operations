# CODEX — adversarial hardening audit of NetworkOps: the act-layer, the Mesh channel, detection robustness, authorization

**Read-only. Open ONLY the files listed below, at HEAD, in this repo (`C:\VIsual Studio Projects\Operations`).**
No other files, no `git log`/`git diff` over a range, no network paths, no SQL connections, no live hosts.

**Do NOT run `dotnet build`, `dotnet test`, or anything else.** Read, think, write the report, stop.
Verification runs on Claude's side.

**Reasoning: MAX. Be adversarial.** Do not congratulate. A one-line "checked, correct, because…" is the right
answer where an invariant holds; spend the words on what breaks.

## Why

NetworkOps is KOR's own RMM: a service on APP01 (Kestrel HTTPS :8445, Entra JWT), a WPF Command Center, a
`netops` CLI, and a MeshCentral remote channel. In the last few days it gained the ability to **act** on
machines — not just watch them — plus new detection rules and an embedded MCP server. Before anything else is
added, find where the new surfaces are unsafe or wrong.

The **act-layer has the highest blast radius in the company.** It runs: PowerShell **as SYSTEM** on any Windows
box (the agent / SCM channel), Python **in hostd as root** on the two ESXi hosts, and now **arbitrary PowerShell
through each device's MeshCentral agent** (the only reach to a workgroup / off-domain box like BK01). A defect
here mis-fires on live production infrastructure — a server, a domain controller, a SAN host — not a lab.

**Already audited — do NOT re-audit:** the agent trust boundary + transport
(`CODEX-NETWORKOPS-AGENT-AUDIT*.md`), the service/map/workstation pass
(`CODEX-NETWORKOPS-WORKSTATIONOPS-AUDIT-2026-10-03.md` + `-REAUDIT`), and the migrate-to-korops SYSTEM payload +
Outlook add-in (`CODEX-KOROPS-MIGRATE-OR-UPDATE-AND-ADDIN-AUDIT.md`). This audit is the **fix-execution engine**,
the **Mesh channel**, the **new detection rules/probes**, and the **authorization/audit boundary** — none of
which those passes covered.

Environment facts you need: WinRM and DCOM/RPC are blocked fleet-wide, so the only reach to a PC is the
NetworkOps agent (a script body as SYSTEM, no `param()`/file args) or, for a workgroup box, the MeshCentral
agent. The service runs on APP01 as a domain account; ESXi over an SSH key on APP01; Mesh over the
read-mostly `networkops` MeshCentral account that was just granted the **Remote Commands** right on its two
device groups. All `KOR_NETWORKOPS_*` secrets live only on APP01. Every action is meant to be audited to
`NetworkOps.Actions`. **Standing rule: no disruptive fix fires on live infra without explicit confirmation.**

---

## Part 1 — the fix-execution engine (where a defect costs the most)

Open:
- `Kor.Operations.NetworkOps.Core/Actions/FixCatalog.cs`
- `Kor.Operations.NetworkOps.Service/Sweep/ActionRunner.cs`
- `Kor.Operations.NetworkOps.Service/Store/NetworkOpsStore.CommandCenter.cs` (StartAction / ClaimAction / CompleteAction / AbandonRunningActions / CountInFlightActions)
- `Kor.Operations.NetworkOps.Service/Api/ApiHost.cs` (the fix + run-many endpoints) and `Api/SessionApi.cs` (the ad-hoc run)

Break each invariant; give `file:line` and the exact sequence.

1. **Audit lifecycle.** Every mutating action writes an `Actions` row *before* it runs and closes it on **every**
   exit (Done/Failed/Refused/abandoned). Find a path that runs without a row, or leaves one `Running` other than
   on a hard process kill. Trace the FIX path (ClaimAction → dispatch → CompleteAction) and both new branches
   (ESXi, Mesh) for a throw *between* claim and complete that never completes the row.
2. **Disruptive confirmation.** A fix with `Disruptive = true` must **never** reach execution with
   `confirmed = false`. Trace every entry: the device-window Fix, the To-clear fan-out (queues `confirmed:false`
   then a MessageBox then `confirmed:true`), the MCP `run_fix` tool, the CLI `fix` verb. Find one where a
   disruptive fix reaches the runner without `confirmed = true`.
3. **Input binding.** A param cannot escape its quoting. Check `StartService` / `RemoveStaleDatastore` regexes
   (anchored? applied on BOTH the API refusal and the runner?); the ESXi `python {path} '{param}'` (a datastore
   name containing a single quote — does the regex truly preclude it?); and that `RunCommand` / `RunCommandMesh`
   (arbitrary script **by design**) are reachable **only** by an authenticated, authorised operator — never by a
   finding-driven or automatic path.
4. **Channel dispatch + CanRun.** `CanRun` is the one gate and the runner must agree. Find a `(device, fix)`
   where `CanRun` says yes but the runner's branch cannot serve it (or vice versa), where `Target` routes to the
   wrong channel (a Mesh fix down the SCM path, or SCM down Mesh), or where `EsxiHostOf` / `HostOf` / the Mesh
   lookup resolve a **different** device than the one the row was audited against.
5. **Fan-out targeting.** `RunFixManyAsync` must run the fix on exactly the devices that have the finding, by the
   correct `FindingId`/`DeviceId`, with no cross-device bleed, and the `deviceId|kind` "running" key must not
   collide two distinct issues into one "already fixing".

## Part 2 — the Mesh-run channel (new transport, new auth, newly-written parser)

Open: `Kor.Operations.NetworkOps.Transport/MeshCentralClient.cs`; `ActionRunner.cs` (`RunViaMeshAsync`);
`Kor.Operations.NetworkOps.Service/Mesh/MeshState.cs`, `MeshSweepJob.cs`;
`Kor.Operations.NetworkOps.Service/Jobs/Scheduling.cs` (the startup-warm trigger).

6. **Reply parsing & termination.** `RunCommandAsync` must return the command's output **once** and terminate
   promptly. Break it: a reply whose terminal frame (`action:"msg"`, `type:"runcommands"`, our `responseid`)
   never arrives — does it wait out the full fix timeout? output that only streams as `console` with no terminal
   frame (the fallback path); a reply carrying a **different** `responseid` (two concurrent Mesh runs) mixing
   output; a frame that is not valid JSON inside the receive loop (is `JsonDocument.Parse` guarded, or does one
   bad frame fault the whole run?); very large output (the 32 MB guard; and MeshCentral may **truncate** the
   terminal `result` — the method's own docstring admits this — is a truncated JSON verdict then mis-read as
   success?). When the idle CTS cancels a receive mid-frame, is the WebSocket always disposed with no leak?
7. **Connected-agent gating under stale state.** `RunViaMeshAsync` gates on `link.Node.AgentConnected`, which is
   from the **last** Mesh sweep (≤5 min old, or the +20 s warm). Construct the window where the agent
   disconnected *after* that sweep → the fix dispatches to a dead agent. What does the operator see, and when
   does it fail? A Mesh fix whose result is lost (we never learn if it ran): is every catalog script safe to
   have half-applied, or can one leave a box worse off?
8. **Account scope + trust.** The `networkops` account now holds Remote Commands on its groups. In
   `RunCommandAsync`'s protocol use, is anything **beyond** running a command reachable (file transfer, terminal,
   config)? Is `MeshTrust` certificate pinning (SHA-256) enforced on the control-channel WebSocket here — fail
   **closed** on a wrong/rotated cert — or only on the node-listing path?
9. **Startup-warm vs cron race.** The one-shot startup MeshSweep trigger and the 5-minute cron share the same
   `JobName`. Confirm the `JobDispatcher` per-job gate serialises them (no two sweeps racing `MeshState`). If a
   sweep throws, is `MeshState` left empty (act-layer blind) with no retry until the next cron tick?

## Part 3 — detection correctness & probe robustness  (Part 3.10 is a rule-11 task: find the WHOLE class)

Open: `Kor.Operations.NetworkOps.Core/Probes/server.ps1`; `Kor.Operations.NetworkOps.Core/Rack/esxi-health.py`;
`Kor.Operations.NetworkOps.Core/Rack/ServerRules.cs`, `EsxiRules.cs`, `VersionBaselineRules.cs`,
`NetworkRules.cs` (the firewall rules); `Kor.Operations.NetworkOps.Transport/SnmpChannel.cs`.

10. **THE PROBE-CRASH CLASS — do not stop at one.** A probe that throws a **terminating** error when the thing it
    queries is absent kills the *whole* probe, and `$ErrorActionPreference='SilentlyContinue'` does **not** catch
    terminating errors. One instance was just fixed in `server.ps1` (`Get-MpComputerStatus` threw "Invalid class"
    after the Defender feature was removed, blanking the entire server read). **Enumerate every other such call**
    in `server.ps1` and `esxi-health.py` (and the SNMP walks in `SnmpChannel.cs` / `NetworkRules.cs`) that can
    throw terminating when its subsystem is missing/disabled/old — e.g. `Get-NetConnectionProfile`, `Get-WinEvent`
    on an absent log, `vssadmin`, `Get-HotFix`, each `esxcli` subcommand on an older ESXi, a GETBULK walk over an
    OID subtree the device does not implement. Deliver: the **list** (`file:line` + the absent-state that triggers
    it), the single **invariant** ("a missing subsystem reads as absent, never as a dead probe"), and the **one
    check or harness** that would catch the whole class — not N patches. If the list is empty after the fix, say
    so and why.
11. **version-baseline truth.** No current build flagged end-of-support; no insecure build passed. Attack
    `IsBelow` across its three shapes (bare ESXi build integers vs dotted Windows `Version` vs pfSense), the regex
    parse of `os.build` / `os.caption` / `fw.model`, the EoS-date comparison, and `baseline.stale` (45 d): does a
    **stale or missing** baseline silently pass everything (fail-open), and is that the intended posture?
12. **AV-posture completeness.** `server.av-conflict` / `server.av-none` across every real posture: Defender in
    **Passive** mode (`AMRunningMode` = "Passive", which the rule does not treat as active — is Passive+Webroot
    correctly "one active"?), Defender+Webroot both Normal, Webroot present-but-stopped, **both absent**
    (`av-none` Critical — correct and wanted?), and an MDE / EDR-block posture. Can the ProbeVersion-2 gate
    (judge only when `WebrootStatus` is present) misread anything as "no AV"?
13. **Shared mutable state under concurrency.** Any public mutable **static** touched by a rule, parser or
    collector that the 5-minute sweep and an on-demand check (`TriggerPoller`) running together — or xUnit's
    parallel test classes — could corrupt. (This class has bitten the repo before via a mutable static
    vocabulary.) Name it, or confirm none.

## Part 4 — authorization, secrets, audit (the boundary)

Open: `Kor.Operations.NetworkOps.Service/Mcp/NetworkOpsMcpTools.cs`, `NetworkOpsMcpActions.cs`;
`Kor.Operations.NetworkOps.Cli/McpBridge.cs`; `Kor.Operations.NetworkOps.Service/Api/ApiAccess.cs`, `ApiHost.cs`.

14. **/mcp authority.** The MCP tools (especially `run_fix`, `acknowledge`, `recheck`) must run behind the same
    CommandCenter Entra auth as the app and be audited as the **real** user. Find a path where a tool mutates
    without a validated token, is audited as the service or the wrong principal, or where `run_fix` bypasses the
    `CanRun` / disruptive-confirmation gate Part 1 relies on.
15. **Secret non-leak (Debug, fleet-wide).** No log line or API/MCP response may emit any `KOR_NETWORKOPS_*`
    secret (DB connection string, Mesh password, SNMP community), the ESXi key, or the signing cert. Name any call
    site that could — the Mesh `x-meshauth` header build, an SNMP community placed in a fact/metric, a raw
    connection-string exception surfaced to a client.

## Part 5 — deploy / release safety (quick pass)

Open: `tools/deploy-networkops-service.ps1`; `tools/release-kor-ops-app-local.ps1`; `Core/Deploy/` (if present);
the Store's Abandon / CountInFlight.

16. **Restart safety.** `inFlight` counts Requested+Running; `AbandonRunningActions` clears only **Running**. A
    `Requested` action that survives a restart and then runs against now-stale state — harmful? Review the gate's
    `-Force` path.
17. **Destructive mirror/delete class.** Beyond the `cli`-wipe just fixed in `release-kor-ops-app-local.ps1`, find
    any **other** `robocopy /MIR`, `Remove-Item -Recurse`, or `Directory.Delete` in the deploy/release/service
    that can delete data someone needs (logs, ProgramData config, the `cli`, a user profile) — the same class as
    that wipe.

## How to answer

Write the report to **`docs\codex\CODEX-NETWORKOPS-HARDENING-AUDIT-2026-10-06-RESPONSE.md`** (that file only;
change no code). **At most ~2,200 words.** Rank findings by **blast radius × likelihood**; each: `file:line`, the
invariant broken, the concrete trigger (inputs → wrong outcome), **CONFIRMED vs PLAUSIBLE**, and the smallest fix.
A reasoned refusal ("checked, correct, because…") is a valid, valuable answer. For Part 3.10 give the full class
list plus the one harness. End with the **single** most important thing to fix before anything else is added — or
"none" if nothing deserves it.
