## What the endpoint agent is
A 33 KB .NET Framework 4.8 Windows service (`KorNetworkOpsAgent`, LocalSystem) on each PC. It holds one HTTPS poll
open to APP01:8445 (certificate pinned) and runs whatever the service hands it -- the same health probe and fix
scripts the network route runs -- then posts the result. It has no rules of its own. Runbook:
`docs/runbooks/Kor.Operations.NetworkOps.Agent.md`.

## Where it lives
- Agent: `Kor.Operations.NetworkOps.Agent` (AgentLoop, JobRunner, ProcessTree = kill-on-close job objects, ConsoleIdle,
  DataFolder). Installs to `C:\Program Files\KorOperations\Agent`, data in `...\Agent\data` (SYSTEM + Administrators only).
- Server: `Service/Agents` -- AgentHub (in-memory broker; authoritative key per PC; withdraw-before-fallback),
  AgentApi (gate: key checked before any body is read), MachineRunner (agent or network route, 3 jobs per PC),
  AgentInstaller (install/upgrade/remove over c$ + SCM, one per PC at a time, confirmed only by the NEW key),
  AgentRollout (on demand: JobTriggers 'AgentRollout', 5 online PCs at a time, stops at the first failure).
- Findings: `agent-silent`, `agent-outdated` (Core/Health/AgentRules.cs).

## Things learned the hard way
- Two Codex audits (docs/codex/CODEX-NETWORKOPS-AGENT-AUDIT*.md): every finding was real. Read them before changing it.
- Privileged code never acts on a path a non-admin can create or replace (ProgramData, TEMP, profiles) -- the gate is
  PrivilegedPathTests. A ProgramData junction once could have made the installer delete Program Files.
- xUnit 2 never calls IAsyncDisposable on a test class: an orphaned test agent hangs `dotnet test`. Use IAsyncLifetime.
- Kill switch: KOR_NETWORKOPS_AGENTSENABLED=false on APP01 + service restart sends everything back to the network route.
