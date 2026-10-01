# NetworkOps morning brief, 1 October 2026

Measured overnight on the machines themselves. Every read was read-only, and **nothing below has been changed**: each step marked "OK needed" is yours to approve.

| # | Topic | The one-line answer | Your decision |
|---|---|---|---|
| 1 | Patching | Updates are switched off by **local leftovers** (29 of 29 PCs), not by a GPO. Servers last patched **2026-07-21**. Patching is on demand from the Command Center's **Updates** view: no GPO, no schedule. | Tick the machines that are due and install |
| 2 | Veeam | All three jobs back up. The shared Warning is BK01's **guest catalog**, failing since **09-10**. FS01 also has VSS and indexing failures. | Restart the catalog service; turn off FS01 guest indexing |
| 3 | Local admins | On **25 of 29 PCs** the signed-in user is an administrator (INTERACTIVE on 16). Six leftover local accounts. | Remove the leftovers; one GPO for admins; LAPS |
| 4 | MeshCentral SSO | Exact app-registration values and config, checked against the installed 1.2.5 code. | Create the app registration |
| 5 | Flex switches | .53 silent since **4 July** (SW02 port 10); .59 not seen since **2023** (SW02 port 46). | Walk to both ports |

Also built overnight: the **Prompt Library** (NetworkOps 0.9.2/0.9.3, live). It needs `db/KorNetworkOps/007_PromptLibrary.sql` run before sessions can report back.

## Patching

Measured 2026-09-30 22:28–22:35, on the targets (netops run, one call each; read-only).

### What switches it off today

- **No GPO sets Windows Update.** 0 of 9 domain GPOs sets any value under `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` or its `AU` key (checked with `Get-GPRegistryValue` per GPO, on DC01). RSoP on each machine agrees: 0 of 29 workstations and 0 of 4 servers have a Windows Update setting that came from Group Policy.
- **The settings are local leftovers**, written straight into the policy key, presumably by Ninja when T-Net ran it:
  - **Workstations: 29 of 29 online** have `NoAutoUpdate=1` + `AUOptions=1`, so automatic updates are fully OFF. The other 10 of 39 were offline and were not read.
  - **Servers: APP01, DC01, FS01, RDS01** have `AUOptions=3` ("download and notify", so nothing installs) and no `NoAutoUpdate`. BK01 did not answer on 445 and was not read.
- No WSUS (`WUServer`/`UseWUServer` absent: 33 of 33), no deferrals, no `DisableWindowsUpdateAccess`. The machines can reach Microsoft Update directly. They are only told not to act.
- The GPO named **"Ninja"** is a Software Installation of `NinjaOne-Agent-BMZ-MainOffice-Auto.msi`. It is **not linked** anywhere, so it is inert. Delete it during cleanup.
- **All 44 computer accounts sit in `CN=Computers`**, a container that no GPO can link to. The only thing that can target them today is a domain-root link with a filter.

### Where each machine stands

- **Workstations:** 29 of 29 have a September cumulative update (27 dated 2026-09-24/28, from the manual pushes; KOR-224 09-14; KOR-SPARE8 09-16). Pending reboot: 1 of 29 has CBS (KOR-1001); 26 of 29 have file-rename only.
- **Windows 10 22H2 on 2 of 29: KOR-224 and KOR-SPARE8.** Windows 10 went out of support on 2025-10-14. Upgrade or retire them; patching will not make them supported.
- **Servers:** the last OS update on APP01, DC01, FS01 and RDS01 is **2026-07-21**, 71 days ago. Only Defender intelligence and Store app updates have landed since. Uptime 4–5 days. Pending: file-rename only on each.

### How patching works: on demand, from the Command Center (NetworkOps 0.10)

Nothing installs on a schedule. The leftover "updates off" setting stays as it is: it stops Windows from acting by itself, but it does not stop an install NetworkOps starts. So **no GPO is needed**.

- **Knowing when.** At 08:00 and 13:00 on working days, every PC and Windows server asks Windows Update what it has waiting. It searches; it installs nothing. Each machine gets one "updates-due" finding:
  - **held:** a security update in its first 3 days (in case Microsoft pulls it);
  - **due:** a security update out 3 days or more;
  - **overdue:** out 14 days or more, or an out-of-band update Microsoft rates Critical, at once.
  - Drivers and feature upgrades are never offered.
- **Being told.** Due and overdue machines go in the email digest. The 08:00 run on the day after Patch Tuesday mails a list of what arrived. The digest is still off until you switch AlertsEnabled on.
- **Installing.** Command Center → **Updates**. Tick the machines (or "Tick everything due"), then choose:
  - **Install:** no restart; the machine shows "restart pending".
  - **Install and restart if needed:** a 5-minute warning on screen, and it asks first if someone is using the PC.
  - Installs run a few at a time. Each machine is searched again as soon as its install finishes, and every install is recorded in that machine's history.
- **The servers' rules, enforced by the service:**
  - DC01 only goes in a batch of its own.
  - APP01 installs but is never restarted from here; restart it yourself.
  - BK01 is listed but can't be reached (445 is closed to APP01).
  - FS01 and RDS01 fail with "access denied" until KOR\app-admin is an administrator on them.
- **Order for the servers, by hand:** BK01 (through Connect), RDS01, FS01, APP01, then DC01 last and alone, after a fresh Veeam restore point of it.
- **Separately:** KOR-224 and KOR-SPARE8 run Windows 10, which is out of support. Upgrade or retire them. The unlinked "Ninja" GPO can be deleted.

## Veeam: why the three jobs are Warning

Read on KOR-BK01 at 22:28 and 22:30 on 2026-09-30, read-only (Veeam 12.3.2.4854).

**The headline:** every one of the three jobs is backing up. The data is in the restore points. The warning they share is Veeam's **guest catalog**: the search index and malware-scan metadata, kept on BK01, failed to take the results. That has been going on since **10–11 September**, so it is not new tonight. Kor-FS01 has a second, separate problem: VSS, and indexing inside the FS01 guest.

### The warning all three share: catalog publishing (BK01 side)
Every session that ended in Warning since 09-10 carries one or both of these lines:

> Failed to publish malware detection metadata to the catalog
> Failed to commit catalog transaction "E:\VBRCatalog\Publications\Upload\<guid>" … Proxy [UploadAndMonitoring]: unable to call method. All attempts have failed … File "E:\VBRCatalog\Publications\Upload\<guid>\PubCommands.txt" does not exist.

Kor-RDS01 also logs: *"Failed to publish guest file system index to the catalog"*.

How many sessions carry it, over the last 30 days:

| Job | Sessions | Results | With the catalog warning | First seen |
|---|---|---|---|---|
| Vcenter | 27 | Warning 15 · Success 12 | 15 of 27 | 2026-09-11 |
| Kor-VMs-New | 27 | Warning 16 · Success 11 | 15 of 27 | 2026-09-11 |
| Kor-FS01 | 31 | Warning 16 · Failed 5 · Success 9 · running 1 | 20 of 31 | 2026-09-10 |

What is on BK01 now:
- The **Veeam Guest Catalog Service** (VeeamCatalogSvc) is Running and set to Automatic.
- `E:\VBRCatalog` exists (registry CatalogPath; share `\\KOR-BK01\VBRCatalog`, port 9393).
- `Publications\Upload` is **empty**: the folder each job writes is gone by the time the catalog tries to commit it.
- E: has 24.8 TB free. C: has 21.9 GB free.

**What it means:** backups and full-VM restores are unaffected. What is missing is the **file-level search index**: "find a file across restore points" in the console and Enterprise Manager. The **malware-scan metadata** is missing too. Restoring a file by browsing a restore point still works.

**What I did not find:** why the upload folders disappear. Nothing above proves the cause. A lead worth checking, not yet tied to this: memory records that FS01 data deduplication was switched on, with "Veeam BK01" left as an open item. Get the dedup date and compare it with 09-10.

**Recommended fix (needs Ian's OK, because it changes BK01):**
1. Restart the Veeam Guest Catalog Service outside the backup windows. Jobs run at 17:00 and 21:00, with retries around 02:30–03:00.
2. Watch whether the next 17:00 run publishes cleanly.
3. If it still fails:
   - check that `\\KOR-BK01\VBRCatalog` is reachable from BK01 itself;
   - check whether antivirus is scanning or removing files under `E:\VBRCatalog`, and if so add a Veeam exclusion;
   - if neither, open a Veeam case with these log lines.

### Kor-FS01: VSS and guest indexing (FS01 side), on top of the catalog
- Last finished run: **2026-09-30 03:02 → 04:01, Warning**.
  > Failed to index guest file system. VSSControl: Index failed

  It also logged the catalog warning.
- The two attempts before it **Failed**:
  - **02:30**:
    > Cannot contact the VSS engine. … Cannot delete file [C:\WINDOWS\VeeamVssSupport\IndexData\Index1790741200_Dirs.tmp]. … being used by another process. Code: 32
  - **02:44**:
    > Failed to call RPC function 'Vss.Unfreeze' … Cannot create a shadow copy … VSS asynchronous operation is not completed. Operation: [Shadow copies commit]. Code: [0x80042306]
- In the last 30 days: 5 Failed and 16 Warning out of 31 sessions. Each failure so far was followed by a retry that went through with Warning.
- Running now: started 21:00, at **99%**, 21.6 TB processed, with no warnings logged yet.

**What it means:**
- Each night's FS01 backup completes, but on a retry.
- Guest indexing of the 21 TB file server keeps failing. It times out or collides with a leftover index file from the run before, so FS01 has no file-level search index either.
- VSS on FS01 is unreliable during the snapshot commit.

**Recommended fix (needs Ian's OK, because it changes the job):** turn off guest file-system indexing for the Kor-FS01 object.
- It is the step that fails.
- It is the most expensive step on 21 TB.
- FS01 files can still be restored by browsing the restore point.

Then see whether the VSS failures stop. If they don't, look at the VSS writers on FS01 (`vssadmin list writers`, run on FS01) after a failed run.

### Kor-VMs-New: catalog only, plus one transient UNIFI01 warning
- Last run: **2026-09-30 17:00 → 17:09, Warning**. All four objects (Kor-APP01, Kor-DC01, Kor-RDS01, KOR-UNIFI01) warned for:
  > Failed to publish malware detection metadata to the catalog

  This is the catalog warning, and the same as 09-24.
- The 09-29 Warning was a different one: KOR-UNIFI01 only.
  > Unable to perform guest file system indexing: Failed to establish a network connection to the host.

  On 09-30 it no longer appears; UNIFI01's only warning is the catalog one.
- **Fix:** the catalog fix above. Nothing specific to this job.

### Vcenter: catalog only
- Last run: **2026-09-30 17:09 → 17:23, Warning**.
  > Failed to publish malware detection metadata to the catalog

  This is the same as 09-24.
- 09-29 and 09-27 were Success. Every one of its 15 Warnings in 30 days carried the catalog warning.
- **Fix:** the catalog fix above.

### Also seen, not investigated
- BK01 has a **D: volume with 0 GB capacity**, probably an empty optical or virtual drive.
- Two copy policies, **Kor-FS01 Offsite** and **Kor-Replication**, are enabled but returned no backup sessions from `Get-VBRBackupSession`. Copy jobs keep their sessions elsewhere, so their health was not read here.

## Local administrators: who has admin on the PCs

Read at 18:03 on 09-30, on the PC itself (`net localgroup Administrators`, through NetworkOps), from **29 of 39 PCs**. The other 10 were offline: DALERHOME, KOR-100, 1000, 104, 213, 315, EDMONTON-01, LAPTOP01, SPARE100 and PERFORM2.

Every PC also has the expected entries: Administrator, KOR\Domain Admins and KOR\app-admin. The table lists only what is there **as well as** those.

| PC | Also an administrator |
|---|---|
| KOR-1001 | kor\ilalonde, KORAdmin |
| KOR-101 | KORAdmin, **INTERACTIVE** |
| KOR-104N | KOR Admin, kor\dsingh |
| KOR-202 | kor\jmarkulin |
| KOR-204 | **BMZ**, **INTERACTIVE** |
| KOR-205 | **INTERACTIVE** |
| KOR-206 | KORAdmin, **INTERACTIVE** |
| KOR-206-N | KORAdmin, **INTERACTIVE** |
| KOR-207 | kor\markb, KORAdmin, **INTERACTIVE** |
| KOR-208-N | kor\rbeirne, KORAdmin |
| KOR-210 | kor\jatkinson, KORAdmin |
| KOR-213-N | kor\cmurtagh, KORAdmin |
| KOR-216 | KORAdmin, **INTERACTIVE** |
| KOR-217 | KORAdmin, **INTERACTIVE** |
| KOR-218N | kor\ishabana, **INTERACTIVE** |
| KOR-223N | KORAdmin, **INTERACTIVE** |
| KOR-224 | KORAdmin |
| KOR-302N | KORAdmin, **INTERACTIVE**, **Superuser** |
| KOR-304 | kor\simons, **nucleusadmin**, **nucleuslt** |
| KOR-305 | KORAdmin, **INTERACTIVE** |
| KOR-306 | **INTERACTIVE**, **tadmin** |
| KOR-307-N | kor\nyu |
| KOR-308 | kor\szheng, KORAdmin |
| KOR-310 | KORAdmin, **INTERACTIVE** |
| KOR-314 | KORAdmin |
| KOR-319 | kor\mmousa |
| KOR-320 | **Admin** |
| KOR-SPARE2 | KORAdmin, **INTERACTIVE** |
| KOR-SPARE8 | KORAdmin, **INTERACTIVE** |

**How to read it**
- **INTERACTIVE**, on 16 of 29 PCs, means *anyone who signs in at the keyboard is an administrator*. That is the widest grant there is.
- A named user (for example kor\jmarkulin) is on 12 of 29 PCs, and one of those is you on KOR-1001. On 9 PCs this is the only grant. Counting both kinds, **the signed-in user is an administrator on 25 of 29 PCs**. The 4 where they are not: KOR-1001 (yours), 224, 314 and 320.
- **Leftover accounts nobody can vouch for:**
  - BMZ on 204 (KOR's old name).
  - Superuser on 302N.
  - nucleusadmin and nucleuslt on 304 (a vendor's?).
  - tadmin on 306 (T-Net's?).
  - Admin on 320.

  Each of these is a local password that nobody rotates.
- **KORAdmin** is a local account on 20 PCs, and on KOR-104N it is spelled "KOR Admin". If it shares one password everywhere, one compromised PC opens all of them.

**Proposed, nothing done (each step is your OK)**
1. **Remove the leftover accounts first.** This is the cheap, safe win. Before deleting an account, disable it for a week and see whether anything breaks. 304's nucleus accounts may belong to a vendor's software, so check what that software is first.
2. **One GPO decides who is an administrator.** Use Group Policy Preferences → Local Users and Groups, not classic Restricted Groups: Restricted Groups replaces the whole membership in one go with no pilot. The GPO would:
   - On "Administrators (built-in)", set "Delete all member users" and "Delete all member groups".
   - Add Administrator, KOR\Domain Admins and KOR\app-admin back.
   - Optionally add a new KOR\Workstation Admins group, for the few people who genuinely need admin.

   Link it to a pilot OU first (KOR-104N), then the workstations OU.
3. **Windows LAPS** (built into Windows 11, nothing to install) for the local Administrator and KORAdmin passwords: a unique password per PC, rotated, readable in AD by IT only.
4. **The cost of steps 2 and 3:** engineers lose self-service installs, such as Revit add-ins, Bluebeam and driver updates. NetworkOps' Fix… → Run a command runs as SYSTEM, which covers IT doing those installs remotely. **Whether to take admin away is a business call, not a technical one.** Remove INTERACTIVE at least: it is the grant nobody chose.

## MeshCentral: sign in with Entra (SSO)

Checked against the code installed on KOR-MESH01: MeshCentral **1.2.5**, `/opt/meshcentral/node_modules/meshcentral/webserver.js` (cited below as `ws:<line>`), and the live `/opt/meshcentral/meshcentral-data/config.json`. Nothing was changed.

### Which strategy to use: `oidc` with the Azure preset, not `azure`
- The `azure` strategy (`ws:8259-8277`) uses `passport-azure-oauth2`, a v1-endpoint library, and keys the account on the `unique_name` claim (`ws:8271`). That is the legacy path.
- Use **`oidc`** (`ws:8382` onward). When `custom.tenant_id` is set, the preset becomes `azure` (`ws:8390`) and the issuer becomes `https://login.microsoftonline.com/<tenant>/v2.0` (`ws:8396`), discovered by OpenID metadata (`ws:8438`).
- The default scopes are `openid profile email` (`ws:8418`). Nothing else is requested unless a `groups` block is added (see "Who gets in").

### The redirect URI, exactly as the code builds it
- origin = `https://` + domain `dns`, otherwise the certificate CommonName. The port is appended only if it is not 443 (`ws:8470-8472`).
- Live config: `settings.cert` = `kor-mesh01.int.korstructural.com`, `port` = 443, and no domain `dns` is set.
- path = domain url + `auth-oidc-callback` (`ws:8475`).
- So the redirect URI is **`https://kor-mesh01.int.korstructural.com/auth-oidc-callback`**.
- Logout returns to `https://kor-mesh01.int.korstructural.com/login` (`ws:8477-8478`).
- The callback route is a GET (`ws:7629`). The default response mode (query) fits it, so no form_post setting is needed.

### App registration (Ian creates it; Entra ID → App registrations → New)
| Field | Value |
|---|---|
| Name | `KOR Remote (MeshCentral)` |
| Supported account types | This organization only (single tenant) |
| Platform | **Web** |
| Redirect URI | `https://kor-mesh01.int.korstructural.com/auth-oidc-callback` |
| Front-channel logout URL | `https://kor-mesh01.int.korstructural.com/login` (optional) |
| API permissions | Microsoft Graph, delegated: `openid`, `profile`, `email`. These are the defaults; no admin-consent permissions. **Do not** add a `groups` block in MeshCentral: with the Azure preset it adds `Group.Read.All` to the request (`ws:8426`). |
| Client secret | **Yes, required.** The `oidc` client is a confidential Web client (`client_secret`, `ws:8528`). Create one with a 12–24 month expiry and **note the expiry date**. |
| Tenant ID | `d9be1f7f-aacf-461a-8d1b-5528b86d540f` (the same tenant as the NetworkOps API, `ApiTenantId` in `Kor.Operations.NetworkOps.Service/appsettings.json`) |
| Enterprise app → Properties | **Assignment required = Yes**. Then assign only the people who may use remote control (Ian, or a "KOR Remote users" group). |

**Where the secret lives:** only in `/opt/meshcentral/meshcentral-data/config.json` on MESH01, which is root-owned and was unreadable to koradmin without sudo when checked. Never in the repo and never in memory. Put the expiry date in the NetworkOps notes for KOR-MESH01, so the renewal is not a surprise.

### The config.json change: add inside `domains.""`, beside `newAccounts`
```json
"authStrategies": {
  "oidc": {
    "client": {
      "client_id": "<Application (client) ID>",
      "client_secret": "<client secret VALUE>"
    },
    "custom": {
      "tenant_id": "d9be1f7f-aacf-461a-8d1b-5528b86d540f",
      "buttonText": "Sign in with KOR (Microsoft)"
    },
    "newAccounts": true
  }
}
```
- `buttonText` is read as `custom.buttontext` (`ws:3609`; MeshCentral lower-cases config keys).
- Leave the domain's `"newAccounts": false`. The SSO-only `newAccounts: true` is read per strategy (`ws:2807-2812`), so local self-registration stays closed (`ws:1640`) while SSO users are created on first sign-in.
- Optional: add `"newAccountsUserGroups": ["<mesh user-group id>"]` under `oidc` (`ws:2828`). New SSO users then join a MeshCentral user group that already holds rights on "KOR PCs" and "KOR Servers". Without it, a new SSO account sees **no devices** until Ian grants it rights; nothing grants device rights automatically.

### How it fits with what is already there
- **Accounts are new and separate, not linked.** An SSO user is `user//~oidc:<sub>` (`ws:8619-8620`, `ws:2801`). The existing local `ilalonde@korstructural.com` stays a different account. Ian's first SSO sign-in creates a second account, which needs rights granted, or site admin given once from the local account.
- **MFA:**
  - `force2factor` deliberately skips SSO accounts, which are ids starting with `~` (`ws:3416`, `ws:3422`).
  - MFA for SSO comes from Entra Conditional Access. Confirm the "require MFA" policy covers this new app (all cloud apps, or add it).
  - Local accounts keep MeshCentral's own forced 2FA.
- **Local password login stays.** The login page shows it unless `showPasswordLogin` is set to false (`ws:3624`), and the snippet does not set it. **Do not set it:** the break-glass account depends on it.
- **NetworkOps' read-only `networkops@korstructural.com` is unaffected.** Its websocket logs in through the `x-meshauth` header → `obj.authenticate` (local password) (`ws:9165-9170`), a path independent of the strategies. Add no switch that disables local users (`nousers`, `ws:3063`).

### Steps (on MESH01, as koradmin)
1. Back up the config: `sudo cp /opt/meshcentral/meshcentral-data/config.json /opt/meshcentral/meshcentral-data/config.json.pre-sso`
2. Edit with `sudo nano`: add the block above, then validate it with `sudo python3 -m json.tool /opt/meshcentral/meshcentral-data/config.json > /dev/null`.
3. Restart: `sudo systemctl restart meshcentral`, then `sudo journalctl -u meshcentral -n 50`. Look for the auth-log lines "OIDC: PRESET: AZURE: Setup Complete" (`ws:8519`). A discovery failure disables OIDC and raises a server warning (`ws:8456`); it does not stop the server.

### Test
1. Browse to `https://kor-mesh01.int.korstructural.com`. The login page shows the SSO button **and** the password form.
2. SSO as Ian → Entra MFA → you land in MeshCentral as a new account. Grant it rights, or check the user group took effect.
3. Sign in with the local break-glass account: it still works, still with MeshCentral 2FA.
4. NetworkOps: the next MeshSweep (every 5 minutes) still reads its nodes. In the Command Center the rack's "KOR-MESH01" shows a fresh read, and Connect still opens a device.
5. A user who is **not** assigned to the enterprise app is refused by Entra (AADSTS50105).

### Rollback
`sudo cp /opt/meshcentral/meshcentral-data/config.json.pre-sso /opt/meshcentral/meshcentral-data/config.json && sudo systemctl restart meshcentral`. Local login was never touched. SSO accounts created in the meantime remain as inert users and can be deleted in My Users.

## The two silent Flex switches (.53 and .59)

Read live from KOR-UNIFI01's controller database at 22:45 on 09-30. **11 of the 13 devices** had checked in within 90 s. These two had not:

| Switch | MAC / serial | Last plugged into | Last heard | Firmware |
|---|---|---|---|---|
| USW Flex (USF5P) **192.168.1.53**, unnamed | f4:92:bf:ae:1f:23 | **BMZ-SW02 (US48P500, .57) port 10** | **2026-07-04 13:11 UTC**, 88 days ago | 7.1.26 |
| USW Flex (USF5P) **192.168.1.59**, unnamed | 74:83:c2:07:f0:b7 | **BMZ-SW02 (.57) port 46** | **never seen by this controller**; its last recorded connection is **2023-10-10** | 6.4.18 |

**What it means**
- **.53** was working until 4 July and then went silent. Either it was unplugged or moved, or port 10's PoE stopped powering it. A Flex is PoE-powered from its uplink, so a dead port means a dead switch. Anything plugged into it has been offline since then, or was moved.
- **.59** has not talked to any controller since October 2023. It is almost certainly gone: removed, or dead in a ceiling. Its firmware (6.4.18) predates the rest by years.

**To do, physically (nothing to change in software first)**
1. Trace SW02 ports 10 and 46 to the desk or room they go to; the patch panel labels will say where. Check whether a Flex is there and powered: its LED, or the port's PoE draw in UniFi.
2. If **.53** is found and powered but silent: power-cycle it. If it is still silent, factory-reset it (hold reset about 10 s) and adopt it again on KOR-UNIFI01.
3. If **.59** is not there: **Forget** it in UniFi. Only do that once you are sure, because a forgotten device needs a factory reset before it can be adopted again.
4. Give the three unnamed Flexes names (.56, .53 and .59). While there, renaming the `BMZ-` devices to `KOR-` is housekeeping.

