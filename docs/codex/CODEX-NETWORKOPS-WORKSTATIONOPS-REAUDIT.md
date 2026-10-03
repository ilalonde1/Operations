# CODEX — second-opinion re-audit of NetworkOps + WorkstationOps (after the 2026-10-03 fixes)

**Read-only. Open ONLY the files listed below, at HEAD, in this repo (`C:\VIsual Studio Projects\Operations`).**
No other files, no `git log`/`git diff` over a range, no network paths, no SQL connections.

**Do NOT run `dotnet build`, `dotnet test`, or anything else.** Your test runner hangs here for 15+ minutes.
Read, think, write the report, stop. Verification runs on Claude's side.

**Reasoning: HIGH.**

## Why

An internal multi-agent adversarial audit on 2026-10-03 found ~30 issues across the NetworkOps /
WorkstationOps module and fixed them (service 0.28.0). The full account is
`docs/codex/CODEX-NETWORKOPS-WORKSTATIONOPS-AUDIT-2026-10-03.md` — read it first; it names each finding,
the fix, and how it was verified. Your job is the **second set of eyes**: (1) are those fixes correct and
free of regressions, and (2) what did seven reviewers miss on the two highest-risk surfaces. Be
adversarial. Do not congratulate; do not restate what works except as a one-line clean verdict.

This runs as a Windows service on KOR-APP01; agents on workstations execute server-sent PowerShell as
SYSTEM; a CLI and a WPF app are clients; state is in SQL. The trust boundary (an agent runs what APP01
sends) and the map an engineer trusts to find hardware are the surfaces where a defect costs the most.

## Part 1 — verify the fixes (did any of these introduce a bug?)

Open and check each against its claim in the audit report:

- `Kor.Operations.NetworkOps.Core/Smbios/SmbiosParser.cs` — the two new length guards (`ParseDimm` at the
  size field, `ParseChassis` at the type byte). Is EVERY other field read in that file also guarded against
  a formatted area shorter than the field's offset? Name any still-unguarded read.
- `Kor.Operations.NetworkOps.Core/Network/NetworkMap.cs` — the `GroupBy` dedup at the two dictionaries; the
  expired-lease filter (`nowUtc` from `site.Now`); the VMware-only core-port occupant; the box-branch
  `ConnectedNow` fold. Does the expired-lease change ever drop a *current* lease? Can the VMware-only
  branch mislabel a port that has one real host NIC plus VMs?
- `Kor.Operations.NetworkOps.Service/Network/NetworkMapService.cs` — the immutable `Snapshot` and
  `LiveState` volatile swaps, the lease-parse isolation, the 15-minute live-read freshness guard. Is every
  reader of the published map going through `_published`? Any remaining field read outside it?
- `Kor.Operations.NetworkOps.Service/Agents/AgentEnrolment.cs` — the per-PC miss limit and the peek-then-
  claim `Redeem`. Walk two concurrent `Redeem` calls for the same valid code: is double-spend still
  impossible? Can the per-PC miss map grow unbounded (a key per attacker-chosen name)?
- `Kor.Operations.NetworkOps.Service/Agents/AgentApi.cs` — the cached `PackageZip` and `UseBeforeKeyGate`.
  Is the zip cache invalidated when a deploy changes the package? Does the before-key gate bound both
  routes, and can its semaphore leak on an exception path?
- `Kor.Operations.NetworkOps.Service/Store/NetworkOpsStore.cs` — the AD-sync sanity floor and the
  `Source='AD'` qualification. Does the floor ever refuse a legitimate large decommission? Does the
  `Source='AD'` INSERT-only-when-free path ever strand a real AD machine silently with no signal?
- `Kor.Operations.NetworkOps.Service/Jobs/Scheduling.cs` — the per-job-name gate in `JobDispatcher`. Can it
  deadlock, or serialize two *different* jobs that should run in parallel? Does a job that throws release
  the gate?
- `Kor.Operations.NetworkOps.Transport/MeshTrust.cs` + `Kor.Operations.NetworkOps.Service/Mesh/install-mesh.ps1`
  — the Let's-Encrypt issuer requirement. Does parsing `rawCert` on the CA path throw on any input the TLS
  stack can hand it? Do the C# rule and the PowerShell rule actually agree now?
- `Kor.Operations.NetworkOps.Transport/OnTargetChannel.cs` — the `CleanupProblem` hook, the drain loop, the
  `MarkForDelete` return check. Can the drain loop spin forever if launches keep arriving during shutdown?
- `Kor.Operations.NetworkOps.Transport/EsxiShell.cs` — the catch-all dispose. Is `ssh` ever used after it
  could be disposed?

For each: **correct**, or a concrete regression with `file:line` and the sequence that triggers it.

## Part 2 — fresh adversarial pass on the two highest-risk surfaces (at HEAD)

**The trust boundary.** `Kor.Operations.NetworkOps.Agent\` (`Program.cs`, `Enrol.cs`, `AgentLoop.cs`,
`AgentSettings.cs`, `ConsoleIdle.cs`, `DataFolder.cs`, `ProcessTree.cs`), `Service\Agents\AgentApi.cs`,
`AgentHub.cs`, `AgentEnrolment.cs`, `MachineRunner.cs`, `Core\OnTarget\OnTargetPayload.cs`. A PC on the
LAN/VPN with no key, and a PC that holds its own key — can either run a script APP01 did not issue for it,
act as another PC, replay or forge a job, or escalate through the `--enrol` install? The 2026-09-30 agent
audit (`CODEX-NETWORKOPS-AGENT-AUDIT*.md`) is the baseline; find what it and the internal pass both missed.

**The map builder.** `Core\Network\NetworkMap.cs`, `NetworkMapText.cs`, `Core\Rack\NetworkRules.cs`. Give a
concrete controller/DHCP/live-API/core-SNMP input that yields a wrong placement, a wrong name, a crash, or
a loop, that the tests under `Tests\NetworkMapTests.cs` do not already cover.

## How to answer

Write the report to **`docs\codex\CODEX-NETWORKOPS-WORKSTATIONOPS-REAUDIT-RESPONSE.md`** (that file only;
change no code). **At most ~1,800 words.** Part 1 as a table (fix → correct / regression). Part 2 as
ranked findings: `file:line`, what goes wrong, the concrete trigger, the smallest fix. End with **exactly
one** thing you would fix before trusting this on the fleet (or "none" if nothing deserves it). A reasoned
refusal of a suspected issue is a valid, useful answer — the last audits each produced one and were right.
