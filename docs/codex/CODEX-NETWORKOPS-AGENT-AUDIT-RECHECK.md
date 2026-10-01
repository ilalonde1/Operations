# CODEX — re-check: do the fixes close the NetworkOps agent audit's findings?

**Size: 17 files, ~107 KB (~27k tokens). Run with reasoning MEDIUM.**

**Read-only. Open ONLY the files listed below, at HEAD, in this repo (`C:\VIsual Studio Projects\Operations`).**
No other files, no `git log`/`git diff` over a range, no network paths, no SQL.

**IMPORTANT: Do NOT run `dotnet build`, `dotnet test`, or anything else.** Read, think, write the report, stop.

## Why

Your audit (`docs\codex\CODEX-NETWORKOPS-AGENT-AUDIT-RESPONSE.md`) found one Critical and eleven more. Every one was
checked against the source, held, and was fixed in one commit. Before the agent goes on 38 PCs: **does each fix
actually close what you found — and did any fix open something new?** Be as hostile as the first time.

## What was changed, finding by finding

1. **Pre-created descendants (Critical).** Data folder moved from `C:\ProgramData\KorOperations\Agent` to
   `C:\Program Files\KorOperations\Agent\data` (non-admins cannot create under Program Files). The installer creates it
   with its DACL in one call if absent, refuses a link, deletes and rewrites `agent.key`, deletes the old ProgramData
   folder. At every start the agent (`DataFolder.Secure`) refuses a data folder that is a link, sets owner + protected
   DACL on it, resets `agent.key`/`agent.log(.1)` to inherit-only, deletes everything else (links as links) and
   recreates `work`.
2. **Cancelled caller leaves a job claimable.** `AgentHub.RunAsync` withdraws (claims) the job in `finally`.
3. **Stopped agent leaves SYSTEM PowerShell.** `ProcessTree` (kill-on-close job object) per job; cancellation kills it;
   `AgentLoop.RunAsync` waits up to 15 s for running jobs before returning.
4. **Concurrent installs.** Per-PC `SemaphoreSlim` in `AgentInstaller.RunAsync` (one process; the service is single-instance).
5. **Install confirmed by an old poll closing.** `AgentHub.Seen` records the key hash a poll authenticated with; the
   installer waits for that hash to equal the new key's, with the poll after the revocation.
6. **Held poll survives revocation.** `AgentHub.Revoke` cancels a per-connection token linked into every held poll and
   clears `LastPollUtc`/`KeyHash`; called after the new hash is saved and on removal.
7. **Partial installs invisible / remove ignores DeleteService.** `SaveAgentAsync` clears contact; confirmation writes it;
   rollout selects unconfirmed rows; `RemoveAsync` checks `DeleteService` and waits for the service to disappear.
8. **Idle helper inheritance / unbounded read.** `bInheritHandles = false`, no pipe; the child's exit code is the answer.
9. **Unauthenticated work.** `AgentApi.UseAgentGate` (registered in `ApiHost` before routing): body limit per route,
   256 in flight, key checked before the endpoint (and so before body binding).
10. **Unbounded concurrency.** 3 per PC in `MachineRunner` (both routes); the agent refuses beyond 4.
- **Smart-quote work dir.** `AgentApi.IsSafeWorkDir` (ASCII local path regex, no `..`); the work dir is passed per poll
  into `NextJobAsync`; `OnTargetPayload.Build` refuses U+0027 and U+2018–U+201B.
11. **Rollout reverses removals.** Removed rows excluded; offline PCs skipped, not a stop.
12. **Grace / versions.** `AgentState.InstalledUtc`; `AgentRules.IsOlder` used by rule and rollout.

## Files (open only these)

- Agent (`Kor.Operations.NetworkOps.Agent\`): `AgentLoop.cs`, `ProcessTree.cs`, `ConsoleIdle.cs`, `DataFolder.cs`, `AgentSettings.cs`, `Program.cs`
- Service (`Kor.Operations.NetworkOps.Service\`): `Agents\AgentApi.cs`, `Agents\AgentHub.cs`, `Agents\AgentInstaller.cs`,
  `Agents\AgentRollout.cs`, `Agents\MachineRunner.cs`, `Store\NetworkOpsStore.Agents.cs`, `Api\ApiHost.cs` **lines 45–95 only**
- Transport: `Kor.Operations.NetworkOps.Transport\RemoteAgentInstall.cs`
- Core: `Kor.Operations.NetworkOps.Core\Health\AgentRules.cs`, `Kor.Operations.NetworkOps.Core\OnTarget\OnTargetPayload.cs`
- Your first report: `docs\codex\CODEX-NETWORKOPS-AGENT-AUDIT-RESPONSE.md`

## Questions

1. For each numbered finding: **closed, partly closed, or not closed** — one line each, with `file:line` for anything
   less than closed.
2. Did any fix introduce a NEW fault? Look hardest at: `Revoke` racing a poll that has authenticated but not yet called
   `Seen`; the gate's semaphore and early returns; `DataFolder.Secure` deleting on a folder an administrator prepared;
   `ProcessTree` and a job that exits before `Add`; the per-PC semaphores never being removed; `MachineRunner` holding a
   slot while waiting `PickupWait`.
3. Anything in finding 9 still worth doing before rollout (the gate still looks the key up in SQL per request)?

## How to answer

Write **`docs\codex\CODEX-NETWORKOPS-AGENT-AUDIT-RECHECK-RESPONSE.md`** (that one file only; change no code).
**At most ~1,000 words.** The per-finding table first, then any new Critical / Material / Minor with `file:line`, the
triggering sequence and the smallest fix, then **exactly one ship-blocker or "none"**.
