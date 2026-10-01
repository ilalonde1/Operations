# KOR-MESH01 — MeshCentral (remote control for the PCs and servers)

The server behind **KOR Remote** (https://kor-mesh01.int.korstructural.com) and the Command Center's **Connect**
button. Built 2026-09-30. Everything here is what built it; rebuilding = running these again, in order.

| | |
|---|---|
| VM | Ubuntu 24.04 LTS cloud image, 2 vCPU / 4 GB / 40 GB thin, ESXi VMHost02 **192.168.1.16**, datastore UC3200, "VM Network" |
| Address | **192.168.1.27** static, MAC **00:50:56:1a:01:27** (static: see the trap below); DNS `kor-mesh01` + CNAME `mesh` on DC01 |
| Access | SSH key only, user `koradmin` (`%USERPROFILE%\.ssh\kor-mesh01` on KOR-1001); password login off; root off |
| Firewall | ufw: 22 and 443 from 192.168.1.0/24 and the VPN 10.0.254.0/24 only |
| Updates | unattended-upgrades, including the NodeSource origin (Node.js) |
| Backup | Veeam job `Kor-VMs-New` (no application processing, no indexing — as KOR-UNIFI01) |
| Power | host .16 autostart order 4, guest shutdown, 120 s; UPS shutdown chain wave 1 |
| Monitored | NetworkOps rack device "KOR-MESH01 (remote control, MeshCentral)" (collector `MeshServer`) |

## MeshCentral

- 1.2.5 from npm in `/opt/meshcentral`, service `meshcentral` (systemd), user `meshcentral`, Node.js 22 (NodeSource).
- Config: `meshcentral-config.json` here is the live one. Port 443 only (no 80, no Intel AMT 4433), no self-update,
  no self sign-up, two-factor required to USE anything (note: `force2factor` does not block the password sign-in itself;
  it blocks every device feature until an authenticator is enrolled — enrolment is what makes sign-in need the code).
- Device groups: **KOR PCs** (consent 71: the user is notified and sees a toolbar; no approval prompt) and
  **KOR Servers** (consent 64).
- Accounts: `ilalonde@korstructural.com` (admin, MFA) and `networkops@korstructural.com` — NetworkOps' read-only
  account: group membership only, NO device rights; its password is APP01's `KOR_NETWORKOPS_MESHPASSWORD`.
- Agents install under `C:\Program Files\KorOperations\MeshAgent` (`--installPath`), covered by the Webroot Global
  Folder override. The install is part of NetworkOps (`install-mesh`, Service/Mesh), not these scripts.

## Rebuild, in order (from KOR-1001)

1. `deploy-vm.ps1` — runs on APP01 through `netops run`: verifies the Ubuntu image's SHA-256, imports it on .16 with
   cloud-init as VMware guestinfo (`user-data.yaml`, `meta-data.yaml`), sets the static MAC **and reads it back**.
   Needs `device-pw.dpapi` (ESXi root, DPAPI) beside this folder; never committed. Refuses if KOR-MESH01 exists.
2. If the VM is unreachable: `fix-mac.ps1` (VM off, set the MAC, read it back, on).
3. `dns-add.ps1` on DC01 (`netops run --hosts KOR-DC01`).
4. `install-meshcentral.sh` over SSH with sudo: Node 22, MeshCentral, config, break-glass admin, systemd.
5. Host autostart (govc `host.autostart.add -start-order 4 -stop-action guestShutdown`, delays 120) and
   `veeam-add.ps1` on BK01 (192.168.1.18, its local Administrator; the session is removed afterwards).
6. NetworkOps: `KOR_NETWORKOPS_MESHPASSWORD` on APP01, then the service's next Mesh sweep links every device.

## ⚠ The trap that hit twice (KOR-UNIFI01 and this)

A direct-to-host OVA import leaves a vCenter-type ("vpx") MAC, and vSwitch0 (MAC change / forged transmits = reject)
drops every frame. The guest gets its IP (tools report it) and nothing answers. The static MAC change in the deploy did
NOT stick here the first time — so the deploy now reads it back and stops, and `fix-mac.ps1` is the cure.
