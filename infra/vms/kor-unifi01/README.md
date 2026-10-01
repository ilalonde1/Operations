# KOR-UNIFI01 — the UniFi controller (UniFi OS Server)

KOR's own controller for the APs and switches, replacing Tenacious's hosted one. Built 2026-09-29. These files are what
built it; rebuilding = running them again, in order.

| | |
|---|---|
| VM | Ubuntu 24.04 LTS cloud image, 2 vCPU / 4 GB / 64 GB thin, ESXi VMHost02 **192.168.1.16**, datastore UC3200 |
| Address | **192.168.1.26** static, MAC **00:50:56:1a:01:26**; DNS `kor-unifi01` + CNAME `unifi01` on DC01 |
| Access | SSH key only, user `koradmin` (`%USERPROFILE%\.ssh\kor-unifi01` on KOR-1001) |
| Software | UniFi OS Server (podman, rootless as `uosserver`), UniFi Network ≥ 10.4.57; web `https://kor-unifi01.int.korstructural.com:11443`, inform `:8080` |
| Site | **"KOR Structural" (id `8evrvfqv`)** — NOT the empty "Default" site the UI may open on |
| Backup | Veeam job `Kor-VMs-New` (no app processing, no indexing); UniFi auto-backup staged for NetworkOps (`setup-backup-drop.sh`, `autobackup-state.sh`) |
| Power | host .16 autostart order 3, guest shutdown; UPS shutdown chain wave 1 |
| Monitored | NetworkOps rack device "UniFi network" (collector `UniFi`: `netops@` forced-command SSH) |

## Rebuild, in order (from KOR-1001)

1. `deploy-vm.ps1` (runs on APP01 through `netops run`; needs `device-pw.dpapi`, never committed) — same route as
   KOR-MESH01. ⚠ This version could destroy a previous KOR-UNIFI01 **only** if its UUID matched the one this build made.
2. `fix-mac.ps1` if the VM is unreachable (the vpx-MAC trap: see `../kor-mesh01/README.md`).
3. `dns-add.ps1` on DC01.
4. `install-uos.ps1`: UniFi OS Server (installer SHA-256 checked against Ubiquiti's).
5. Restore **only KOR's site** export (`network_site_BMZ_*.unf`). ⛔ Never Tenacious's full export: it is their whole
   multi-client controller (~60 other companies' sites and keys).
6. `setup-backup-drop.sh` for the read-only backup drop NetworkOps pulls from.
