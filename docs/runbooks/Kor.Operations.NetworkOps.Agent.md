# NetworkOps agent — install, roll out, back out, troubleshoot

The endpoint agent (`Kor.Operations.NetworkOps.Agent`) is how APP01 reaches a PC without the network route.
It holds one HTTPS poll open to the NetworkOps service on KOR-APP01:8445 and runs what the service hands it:
the same health probe and fix scripts the network route runs, as SYSTEM. Design: `docs/KOR-NetworkOps-Design-2026-09-28.md` §4.2.

| On the PC | |
|---|---|
| Service | `KorNetworkOpsAgent` — "KOR NetworkOps Agent", LocalSystem, Automatic, restarts a minute after a crash |
| Program | `C:\Program Files\KorOperations\Agent\` (under the Webroot Global Folder ALLOW override) |
| Key, work folder, log | `C:\Program Files\KorOperations\Agent\data\` — SYSTEM and Administrators only, inheritance cut, re-made at every agent start. Under Program Files because only an administrator can create anything there (in ProgramData any user could pre-create the folder and own it — Codex audit 2026-09-30). Agent 1.0.0 used `C:\ProgramData\KorOperations\Agent\` (KOR-104N only, removed there). Nothing cleans that path up automatically, on purpose: the installer runs as an administrator, and a user can turn any part of a ProgramData path into a junction pointing somewhere else (Codex re-check 2026-09-30). |
| Log | `agent.log` there, rolled to `agent.log.1` at 1 MB |

| On APP01 | |
|---|---|
| Agent package | `C:\Program Files\KorOperations\NetworkOps\agent\` — deployed with the service; the version installs use |
| Record | `KorNetworkOps.NetworkOps.Agents` (migration 005): who has one, the SHA-256 of its key, last contact |
| Audit | `NetworkOps.Actions`, kinds `install-agent` / `remove-agent` — same tab as fixes ("Fixes run") |
| Log lines | `networkops-*.log`: `NetworkOps.Agents` (connections, jobs), `AGENT` / `ROLLOUT` (installs) |

## Install or upgrade one PC

PC window in the Command Center → **Install agent** (or **Reinstall agent** — reinstalling IS the upgrade).
It is done only when the agent has called in; the status line says from which address.

Without the page (SQL as `networkops_app`, e.g. for a test):

```sql
INSERT NetworkOps.Actions (DeviceId, Kind, RequestedBy, Status, BeforeJson)
SELECT DeviceId, 'install-agent', N'<your name>', 'Requested', N'{"action":"install"}'
FROM NetworkOps.Devices WHERE Name = N'KOR-104N';
```

What it does, from APP01 over `c$` and the Service Control Manager: stops a running agent, locks the data folder,
writes a NEW key (the old one stops working), copies the files, records the key's hash, starts the service, waits
up to 60 s for it to call in.

## Roll out to the fleet

Never scheduled: a rollout runs only when someone asks. Each run installs or upgrades the next
`AgentRolloutBatch` PCs (default 5) that answered the network in the last 2 hours and have no agent or an older
one — ONE AT A TIME, stopping at the first failure. Each PC gets its own audited `install-agent` row.

```sql
INSERT NetworkOps.JobTriggers (JobName, RequestedBy) VALUES ('AgentRollout', N'<your name>');
```

(or `POST /api/agents/rollout`). The trigger's `Result` says which PCs were done, where it stopped and why, and how
many are left. Run it again for the next batch. The first batch is the canary: look at those PCs before the next.

## Back out

* **Kill switch — all PCs at once, no PC touched:** set `KOR_NETWORKOPS_AGENTSENABLED=false` on APP01 (machine
  environment variable) or `"AgentsEnabled": false` in `appsettings.json`, then restart the service
  (`sc \\KOR-APP01 stop Kor.Operations.NetworkOps` / `start`). Every check and fix takes the network route again,
  exactly as before the agent; agents stay installed and idle; installs are refused. Undo by setting it back.
* **One PC:** PC window → **Remove agent** (or an action row with Kind `remove-agent`). Stops and deletes the service,
  deletes both folders, and refuses that PC's key from then on even if a copy survives.

## Troubleshoot

| Symptom | Look at |
|---|---|
| Finding **agent-silent** (PC on, agent not calling in) | `agent.log` on the PC: "cannot reach the server: …" names the cause (DNS, TLS, 401). Then Reinstall. |
| 401 in the agent log | Its key does not match the record (installed from elsewhere, or removed). Reinstall. |
| "the server answered 5xx" | APP01 service log at the same minute. |
| Finding **agent-outdated** | APP01 was deployed with a newer agent; Reinstall, or the next rollout picks it up. |
| Install "has not called in after 60 s" | The service is running but cannot reach APP01:8445: the PC's network, the APP01 firewall rule (LAN + VPN subnets), or the certificate pin. |
| A job "did not take … using the network route" in APP01's log | The agent stopped mid-session; the network route ran it instead. Nothing is lost; a job withdrawn this way is never run late. |

## Facts worth not rediscovering

* The agent trusts APP01 by the SHA-256 of its certificate (self-signed, to 2031-09-29), in the agent's `.exe.config`.
  When that certificate is replaced, ship an agent with the new pin BEFORE swapping the certificate, then reinstall.
* A PC's key is readable by that PC's local Administrators. Someone who is local admin on their own PC can pose as
  that one PC (and so fake its health results), and nothing more: every other PC's key is unknown to them.
* Idle time needs someone at the console: the agent starts a copy of itself in that session to read it; the copy's
  exit code is the answer (no pipe, nothing inherited from SYSTEM).
* Every job's process tree runs in a Windows job object: stopping, upgrading or removing the agent (or the agent
  crashing) ends every script it started. A PC runs at most 3 jobs at once from APP01 (either route); the agent
  refuses a 5th outright.
* One install or removal per PC at a time. An install is confirmed only by a poll made with its NEW key; replacing or
  removing a key cuts any connection held open with the old one at once.
* The agent puts itself in a kill-on-close job object at start: everything it ever starts is contained from birth.
* ⛔ **The rule this agent lives by** (gated by `PrivilegedPathTests`): code running as SYSTEM or as an administrator
  never acts on a path a non-administrator can create or replace, at any level. ProgramData, TEMP, user profiles and
  Public are out; Program Files and the Windows folder are in. The gate fails the build on the agent's and the
  installer's source if that changes.
* Every key check is against the key APP01 last installed: a request that authenticated with an older key before a
  reinstall is refused after it, and contact is recorded only for the current key.
* Hardened after the Codex audit of 2026-09-30 and its re-check (`docs/codex/CODEX-NETWORKOPS-AGENT-AUDIT-RESPONSE.md`,
  `...-RECHECK-RESPONSE.md`).
