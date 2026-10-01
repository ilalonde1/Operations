# CODEX — adversarial audit of the NetworkOps endpoint agent (before it goes on 38 PCs)

**Size: 24 files, ~115 KB of source (~29k tokens). Run with reasoning MEDIUM.**

**Read-only. Open ONLY the files listed below, at HEAD, in this repo (`C:\VIsual Studio Projects\Operations`).**
No other files, no `git log`/`git diff` over a range, no network paths, no SQL.

**IMPORTANT: Do NOT run `dotnet build`, `dotnet test`, or anything else.** Verification happens on Claude's side;
your test runner hangs here for 15+ minutes. Read, think, write the report, stop.

## Why

KOR is replacing its MSP's remote-management agent with its own. The agent below is a Windows service that runs
**as SYSTEM on every workstation** and executes whatever PowerShell the NetworkOps service on KOR-APP01 hands it.
It is about to be installed on the whole fleet. Before that: **find what is actually wrong** — a way to make a PC
run something APP01 did not send, a job that runs twice or on the wrong PC, a state the installer leaves a PC in,
a finding that lies, a leak, a race. You are adversarial: do not congratulate, do not summarise what works.

It has been tested (194 unit/integration tests, including the real agent exe against a real HTTPS listener) and
proven live on one PC (install, upgrade over a running agent, remove, reinstall, service redeploy, agent stopped →
network fallback, reconnect catch-up). So what is left is what tests and one PC do not show.

## How it works (so you do not have to reverse-engineer it)

- **Agent** (`.NET Framework 4.8`, no dependencies): long-polls `POST /agent/v1/poll` on APP01:8445 every ~25 s,
  pinning APP01's self-signed certificate by SHA-256. The answer is a job: a complete PowerShell script, already
  wrapped by `OnTargetPayload.Build` to publish its result to `{workDir}\{jobId}.json` (workDir is what the agent
  reported in its poll). It writes the script to its work folder, runs `powershell.exe -File`, reads the result,
  posts `POST /agent/v1/jobs/{jobId}/result`. Jobs run concurrently with polling.
- **Identity**: header `X-Kor-Agent: <PC name>` + `Authorization: KorAgent <key>`. The installer (APP01, over `c$`
  and the remote SCM, as a domain account that is local admin on every PC) writes a fresh 256-bit key into
  `C:\ProgramData\KorOperations\Agent\agent.key` (folder ACL: SYSTEM + Administrators, inheritance cut) and stores
  its SHA-256 in `NetworkOps.Agents`. Every install writes a new key.
- **Server**: `AgentHub` is an in-memory broker. `MachineRunner` sends a job to the agent if it is connected
  (polled within 90 s) and agents are enabled, otherwise to the one-shot SCM "network route"; a job the agent does
  not take within 40 s is withdrawn (an `Interlocked` claim) before the fallback so it cannot run twice.
- **Idle time**: the agent starts a copy of itself with `--idle` in the console user's session
  (`WTSQueryUserToken` + `CreateProcessAsUser`, stdout over an anonymous pipe) and passes the number to the script
  as `KOR_CONSOLE_IDLE_SECONDS`.
- **Kill switch**: `AgentsEnabled=false` → everything takes the network route, installs refused.
- **Findings** on each health check: `agent-silent`, `agent-outdated`. **Rollout**: on demand, N PCs one at a time,
  stop at first failure.

## Files (open only these)

Agent (`Kor.Operations.NetworkOps.Agent\`): `Program.cs`, `AgentSettings.cs`, `AgentLoop.cs`, `ConsoleIdle.cs`,
`DataFolder.cs`, `AgentLog.cs`, `Protocol.cs`, `App.config`, `Kor.Operations.NetworkOps.Agent.csproj`

Service (`Kor.Operations.NetworkOps.Service\`):
- `Agents\AgentApi.cs`, `Agents\AgentHub.cs`, `Agents\AgentInstaller.cs`, `Agents\AgentRollout.cs`, `Agents\MachineRunner.cs`
- `Store\NetworkOpsStore.Agents.cs`
- `Sweep\ActionRunner.cs`, `Sweep\HealthSweeper.cs`, `Sweep\TriggerPoller.cs`
- `Api\ApiHost.cs` — **lines 25–95 and 160–210 only**

Transport (`Kor.Operations.NetworkOps.Transport\`): `RemoteAgentInstall.cs`;
`ServiceControlManager.cs` — **lines 36–58 and 99–192 only**

Core (`Kor.Operations.NetworkOps.Core\`): `OnTarget\OnTargetPayload.cs`, `Health\AgentRules.cs`

Database: `db\KorNetworkOps\005_Agents.sql`

## Questions — answer each, worst first

1. **Trust.** Is there any way for a PC to run a script that the real APP01 did not issue for that PC? Consider the
   certificate callback, redirects, proxies, the `workDir` the agent itself reports being spliced into the script,
   the job id becoming a file name, and the window between writing the `.ps1` and PowerShell reading it.
2. **Identity.** Can one PC's key act as another PC (fetch its jobs, post its results)? Can a removed or replaced
   key still do anything? Anything unauthenticated that reaches SQL or holds a connection open?
3. **Exactly once.** Can a job run twice (agent AND network route), run on the wrong PC, or run after its caller
   gave up? Walk the claim/withdraw/complete paths in `AgentHub` under concurrent polls, a poll cancelled mid-hold,
   the service restarting mid-job, and two jobs for one PC at once.
4. **`ConsoleIdle`.** `CreateProcessAsUser` with `bInheritHandles = true` from a SYSTEM process: which of the
   agent's handles can the user-session child inherit? Any leak of the token, process or pipe handles on any path?
   Anything a user could do to that child to influence SYSTEM?
5. **Installer states.** For each step of `RemoteAgentInstall.InstallAsync` / `RemoveAsync` and
   `AgentInstaller.RunAsync`, what does the PC and the `Agents` row look like if it fails right there? Is any of
   those states silent (no finding, no failed action) or unrecoverable by simply reinstalling?
6. **Findings that lie.** Can `agent-silent` fire on a healthy agent (e.g. right after APP01 restarts and the hub is
   empty, or the `Touch` throttle) or stay quiet for a dead one? Can `agent-outdated` misfire on version strings?
7. **Rollout.** Can it touch a PC it should not (retired, rack, removed agent, offline), skip one it should, or
   leave a `Running` action forever?
8. **Resource and failure behaviour** on a PC: unbounded concurrent jobs, log growth, a PowerShell that never
   exits, a result file at the size limit, the agent running for months, a laptop sleeping mid-poll.
9. Anything else that would make you refuse to put this on 38 machines.

## How to answer

Write the report to **`docs\codex\CODEX-NETWORKOPS-AGENT-AUDIT-RESPONSE.md`** (that one file only; change no code).
**At most ~1,500 words.** Sections **Critical / Material / Minor**; each finding: `file:line`, what goes wrong, the
concrete sequence that triggers it, and the smallest fix. Then name **exactly one ship-blocker** (or say "none" if
none deserves it). If an area looks sound, one line saying so — a clean verdict is useful, an invented finding is not.
