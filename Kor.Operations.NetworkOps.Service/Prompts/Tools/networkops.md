## What NetworkOps is
KOR's own replacement for an MSP's monitoring and remote management, in C#. It watches every PC (hourly health
checks, through the endpoint agent or a one-shot service over the network), the rack (ESXi, SAN/NAS, Veeam, UniFi,
switches, UPSes, internet, Windows servers -- every 5 minutes), runs allow-listed fixes, learns what fixes things, and
shows it all in the Command Center (a page in Kor.Operations.App). T-Net does NO patching or monitoring for KOR.

## Where it lives
- `Kor.Operations.NetworkOps.Core` -- pure: health snapshot, rules, findings, learning, Knowledge, prompts. No I/O.
- `Kor.Operations.NetworkOps.Transport` -- channels: one-shot SCM run-on-target, SNMP, SSH, the agent installer, MeshCentral.
- `Kor.Operations.NetworkOps.Service` -- the Windows service on KOR-APP01: jobs (Jobs/Scheduling.cs is THE list of
  schedules), store (raw ADO.NET, KorNetworkOps DB), API (Api/ApiHost.cs, Entra + MFA), agents, Mesh, power chain.
- `Kor.Operations.NetworkOps.Agent` -- the endpoint agent (.NET Framework 4.8).
- `Kor.Operations.NetworkOps.Cli` -- `netops`: census, run, health, hardware on any PC, run ON the target.
- `Kor.Operations.NetworkOps.Tests` -- xUnit; `Kor.Operations.App/NetworkOps` -- the Command Center page.
- `db/KorNetworkOps/NNN_*.sql` -- migrations; Ian runs them as sa. Design: `docs/KOR-NetworkOps-Design-2026-09-28.md`.

## Build, test, deploy
- `dotnet test Kor.Operations.NetworkOps.Tests` (~20 s); app side: `dotnet test Kor.Operations.App\Kor.Transmittals.App.Tests\Kor.Operations.App.Tests.csproj --filter FullyQualifiedName~NetworkOps` (renders PNGs to %TEMP%\kor-networkops-screens -- LOOK at them).
- Bump `<Version>` in the service csproj, then `.\publish-networkops.ps1` from KOR-1001 (hash-verified deploy). Confirm with `/api/ping`.
- Secrets are KOR_NETWORKOPS_* machine environment variables on APP01, never in the repo or SQL.

## Gates that already exist (do not weaken them)
Knowledge entry for every rule family; every scheduled job in the catalog; the shipped rack config complete and every
channel pinned; privileged code never touches a user-writable path (PrivilegedPathTests); the agent proven against a
real listener; alerts stay off until Ian turns them on.
