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

## Branding and tidy-up (2026-10-01)
`meshcentral-config.json` is the live config. `branding/` holds what it refers to:
- `kor-remote-logo.png`, made by `make-branding.py` from the app's logo (navy taken out, so it sits on any background);
- `custom.css`, KOR's colours on the login page and the signed-in header (MeshCentral links `styles/custom.css` on every page);
- `apply-branding.sh`, run ON the VM with the three files beside it: backs up the live config, validates the JSON,
  installs, restarts MeshCentral once and restores the backup if it does not answer in 60 s.
A `custom.css` change alone needs no restart: install it to `/opt/meshcentral/meshcentral-web/public/styles/`.
`agentCustomization` renames what a NEW install shows (tray, consent bar); never set `serviceName`: NetworkOps'
installer and the Webroot override rely on the existing service and folder.

## Home support: the one internet door (2026-10-02)
Agents only, on TCP 4445 (`agentPort` in meshcentral-config.json). The website and sign-in stay office + VPN only.
- Public DNS (Register.ca): `remote.korstructural.com` CNAME -> `office1` (184.71.160.54).
- DC01 zone `remote.korstructural.com` -> 192.168.1.27, so inside the office the same name goes straight to this VM.
- Netgate: NAT WAN2 (opt1) TCP 4445 -> 192.168.1.27:4445, with its associated pass rule.
- ufw on this VM: 4445/tcp from anywhere (22 and 443 stay LAN + VPN only).
Verified 2026-10-02 from outside: 4445 answers; `/`, `/login` and `/agentinvite` are 404 there (agent routes only).

## The Home support download (2026-10-02)
KOR Remote > Home support > Add Agent > MeshCentral Assistant (Application, connect on user request) gives `MeshCentralAssistant-Homesupport.exe`, wired to `wss://remote.korstructural.com:4445` (check: the exe contains `MeshServer=wss://remote.korstructural.com:4445/agent.ashx`, never the internal name). Zipped with `branding/home-help/READ ME FIRST.txt` as `KOR-Remote-Help.zip` and served at `/downloads/KOR-Remote-Help.zip` on the office/VPN-only site (the "Home help download" link in the header, custom.js); not on the public agent port (404). The exe and zip are not in git: regenerate them from KOR Remote if the group or server changes.

## The real certificate: Let's Encrypt, waiting on ONE record (2026-10-02)
The name is internal, so Let's Encrypt proves it through DNS (DNS-01). Register.ca has no API, so one public record delegates
only the proof to an acme-dns account (auth.acme-dns.io), registered by `cert/setup-cert.sh` (run on the VM 10-02, done):

    Register.ca, zone korstructural.com:   _acme-challenge.kor-mesh01.int   CNAME   bd02418b-02ca-4117-9bf5-671681132d2c.auth.acme-dns.io

- `kor-mesh-cert.timer` (daily 03:30) runs `/usr/local/sbin/kor-mesh-cert`: until that CNAME is in public DNS it only logs
  "waiting"; then it issues (acme.sh 3.1.6 pinned, RSA 2048 -- MeshCentral reads keys with node-forge) and renews every ~60 days.
- `/usr/local/sbin/kor-mesh-cert-install` puts it in MeshCentral's own layout (`webserver-cert-public.crt`, `-private.key`,
  `-chain1.crt`; the name must equal config.json `cert` or MeshCentral regenerates its own), restarts, and proves MeshCentral
  serves the new certificate and the agents come back -- else it restores the backup (`meshcentral-data/certbackup-*`).
- Proven live 10-02 02:18 with a stand-in certificate (new hash, same name): served in 20 s, 35 of 36 connections back in 3 min;
  then the original certificate was put back. acme-dns account proven (an update was visible in public DNS in 5 s).
- Log: `/var/log/kor-mesh-cert.log`. Secrets: `/etc/kor-mesh-cert/acmedns.env` (root-only), never in git.
- Once live, no PC needs MeshCentral's root trusted: Let's Encrypt is trusted everywhere.

## The KOR Remote window (2026-10-02)
On KOR-1001, KOR Remote opens as an Edge app window: just the page, with no tabs, toolbar or bookmarks. Shortcuts: `Desktop\KOR Remote.lnk` and a Start-menu entry of the same name, both running `msedge.exe --app=https://kor-mesh01.int.korstructural.com/ --window-size=1600,1000`. The icon is `branding/kor-remote.ico` (the orange K circle), copied to `%LOCALAPPDATA%\KOR Remote\`. An app window has no editable address bar; its `...` menu shows and copies the URL.
