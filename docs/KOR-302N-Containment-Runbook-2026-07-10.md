# KOR-302N — Containment, Preservation & Response Runbook

**Incident:** Unauthorized data handling / IP removal / unauthorized remote access — user `kor\mli`, machine KOR-302N
**Prepared:** 2026-07-10 — Ian Lalonde, Operations/IT
**Companion to:** `KOR-302N-Forensic-Findings-2026-07-10.md`
**Classification:** Confidential — Security Incident / HR & Legal

> **Authority note.** The forensic collection so far has been strictly read-only. The steps below are **containment actions that change state** (disabling accounts, blocking network paths, imaging). They are drafted for you/management to execute or authorize — sequence and timing should be cleared with HR and legal first, because the order affects both evidence integrity and the employee's notice. Nothing here has been executed.

---

## A. Decision to make first (2 minutes)

**Live-capture vs. immediate-isolation.** Two valid orders:

- **Live-first (preferred if a forensic examiner is reachable today):** before disconnecting, capture volatile state on KOR-302N — RAM image, `tailscale status`/`netstat`, logged-on sessions, open handles. Then isolate. Yields the full tailnet peer list and any in-memory keys.
- **Isolate-first (if speed/containment outweighs volatile capture):** cut network now; accept loss of volatile data. Disk and artifacts (already partly escrowed) remain.

Given a **standing external access path** is open, do not let the machine sit online unattended overnight. If no examiner is available within a few hours, isolate-first.

---

## B. Preserve mailbox & identity evidence — do BEFORE disabling the account

Disabling or deleting a mailbox can purge data and audit trails. Preserve first.

1. **Litigation hold on the mailbox** (Microsoft Purview / Exchange admin), account `mli@korstructural.com`:
   - Purview → Solutions → **eDiscovery** → create a case "KOR-302N-2026-07" → add `mli@korstructural.com` as a custodian with **hold**; *or* the quick path: Exchange admin → recipient → mailbox → **Litigation hold = On, duration unlimited**.
   - PowerShell (Exchange Online): `Set-Mailbox mli@korstructural.com -LitigationHoldEnabled $true -LitigationHoldDuration Unlimited`
2. **Export the mailbox audit + message trace** (before any change): mailbox audit log, and a message trace for the last 90 days (look for auto-forwarding, external sends, large attachments). Preserve the **inbox/transport rules** and any **forwarding** (`Get-Mailbox mli@... | fl ForwardingAddress,ForwardingSmtpAddress,DeliverToMailboxAndForward`; `Get-InboxRule -Mailbox mli@...`).
3. **M365 sign-in logs** for the account (Entra ID → Sign-in logs) — preserve last 30 days (unusual IPs/locations, the Tailscale/Google identity linkages).
4. Note: a **357 MB PST export of this mailbox already exists** on the machine (`2026-06-29_...Emails.pst`) and is in evidence — compare its date range against the live mailbox and the message trace.

---

## C. Cut the standing external access (do promptly — this is the live risk)

The machine is bridged to an **outside, personally-controlled Tailscale network** (`tailbc55d0.ts.net`, owner `elton.rheek@gmail.com`) and also runs **TeamViewer**.

1. **Firewall (perimeter):** block outbound to Tailscale and TeamViewer so access dies even before you touch the endpoint:
   - Tailscale coordination/DERP: `*.tailscale.com` (esp. `controlplane.tailscale.com`, `*.derp.tailscale.com`) and UDP **41641**; block/deny the STUN/DERP ranges. Simplest: block DNS + TLS to `tailscale.com` and drop UDP 41641 egress.
   - TeamViewer: block `*.teamviewer.com` and the TeamViewer master ports; deny the app by hash if you have EDR.
2. **On the endpoint (during examination, after volatile capture):** stop and disable the `Tailscale` service, uninstall Tailscale and TeamViewer, and remove the `C:\ProgramData\Tailscale` profile **only after** it has been imaged (it is already copied to evidence).
3. **Enumerate the full tailnet before removal** — run this live on KOR-302N (Admin cmd/PowerShell). It lists every peer (hostnames + 100.x IPs), including the off-domain test PC:
   ```
   "C:\Program Files\Tailscale\tailscale.exe" status --json > C:\Escrow\tailscale-status.json
   "C:\Program Files\Tailscale\tailscale.exe" status
   ```
   (If the CLI path differs, it installs under `C:\Program Files\Tailscale\`.) Save the output to evidence. The **test PC is `100.87.155.69`**; `status` will give its hostname and the other bridged nodes.

---

## D. Disable the user & rotate what he could reach — AFTER B and C

1. **AD (KOR-DC01):** disable `kor\mli`; do not delete (preserve SID/history). `Disable-ADAccount mli`. Move to a quarantined OU. Reset the password to sever cached creds.
2. **Entra ID / M365:** block sign-in, **revoke all sessions/refresh tokens** (`Revoke-AzureADUserAllRefreshToken` / Entra → user → Revoke sessions), remove app passwords, disable his MFA methods (they may be attacker-controllable).
3. **Revoke KOR SaaS access** he held: Deltek, VPN, any BD tool logins (Apollo/Hunter admin if he had them), GitHub org if any, file-share permissions.
4. **Rotate shared secrets he had access to** as a developer/IT-adjacent user: service-account passwords, API keys visible to him (KOR_APOLLO_APIKEY, KOR_HUNTER_APIKEY, any in appsettings he could read), Wi-Fi PSK if shared, and any admin creds used on machines he administered.
5. **Audit his admin footprint:** he had local-admin-level tooling. Check other machines (esp. KOR-307-N and any he RDP'd to) for the same Tailscale/TeamViewer install, and for scheduled tasks/services running as him or reaching external endpoints.

---

## E. Secure the physical & off-domain assets

1. **Locate and secure the Kingston USB** — DataTraveler 3.0, **28.8 GB, serial `E0D55E6CBD0FF640995012F0`** (the 2026-07-08 device), plus the other external drives (1 TB WD `X0C-00SJG0`, 2 TB Samsung T7 `D432104Y0SNLN6S`, Patriot `1000000000CA`, 128 GB Kingston `EE03DA5152E9`). Bag/tag; do not browse them on a production machine — image them.
2. **The test PC (`100.87.155.69`)** — locate physically if on-prem (check with the perimeter team / switch MAC tables while it may still be reachable), or via the Tailscale status output. Preserve and image it; it may hold the removed source and the other end of the RDP sessions.
3. **KOR-302N** — full disk image; then collect the registry hives (`NTUSER.DAT`, `UsrClass.dat`) that were locked while he was logged on (shellbags, RDP MRU, TypedPaths, Tailscale/TeamViewer per-user state).

---

## F. Recovery (parallel track — does not depend on him)

1. The ~200 deployed plugin DLLs from KOR-302N and KOR-307-N are **escrowed**. Begin **ILSpy/dotPeek decompilation** of the core libraries (`RvtLib2025.dll`, `ML.dll`) and top tools to rebuild a source baseline KOR owns and can compile.
2. Stand up a **KOR-controlled Git repo** for the recovered/rebuilt source; adopt his own per-Revit-year build pattern (documented in the continuity dossier) so KOR can build for 2020–2027.
3. Keep the plugins running in production (they are self-contained local DLLs) while recovery proceeds; there is no immediate outage risk to drafting.

---

## G. Identity research — `elton.rheek@gmail.com` (findings + how to resolve)

**Decoded from the machine's Tailscale state (fact):**
- Tailnet: **`tailbc55d0.ts.net`**, coordination via standard `controlplane.tailscale.com`.
- Account: login `elton.rheek@gmail.com`, display **"Elton Rheek"**, Google user ID **6339171258025277**, with a Google profile photo; bound to the local SID for `mli` on KOR-302N.
- KOR-302N tailnet IP `100.72.43.71`; test PC `100.87.155.69`; `RouteAll=true`, `CorpDNS=true` on the profile.

**Open-source lookups (this investigation, public sources only):**
- Web search for "Elton Rheek" / the email: **no attributable results** ("Rheek" appears only as an unrelated musician handle; a differently-spelled "Elton Rhee" in NY is unrelated).
- **Apollo** people-match: returns the name "Elton Rheek" for the address but an **otherwise empty record** — no employer, title, LinkedIn, GitHub, or location.
- **Hunter** email-verifier: **valid / deliverable** Gmail (score 89), not disposable, **0 public web sources**.

**Assessment:** the address is a **real, deliverable Google identity with essentially no public/professional footprint** — consistent with a low-profile or **pseudonymous** account. It does **not** match the employee's known name, but a Gmail alias is trivial to create, so this neither confirms nor rules out that it is the employee himself or an outside associate. **This is a lead, not a conclusion.**

**To resolve definitively (needs non-public means — via legal):**
- Tailscale **admin console** for `tailbc55d0.ts.net` (owner/billing email, device list, auth-key history, connection logs) — obtainable from Tailscale with legal process, or if the account is company-associated.
- **Google** legal process (preservation letter + subpoena) tied to user ID `6339171258025277` for subscriber/recovery info and login IPs.
- Correlate against the employee's **own artifacts** now in evidence (the PST, browser history once the disk is imaged, the `My_Account_Coonector.txt` referenced on the machine).

---

## H. Escalation

Treat as a security + HR incident with likely IP theft and unauthorized access. Engage **HR and legal counsel before the confrontation/termination step**; counsel decides on outside forensics and any referral to law enforcement. IT owns containment, preservation, and recovery (this runbook). Keep all findings evidence-based; do not record conclusions about motive or identity that the evidence does not support.

---

### Quick checklist
- [ ] Decide live-first vs isolate-first (A)
- [ ] Litigation hold + preserve mailbox audit/rules/sign-ins (B)
- [ ] Block Tailscale + TeamViewer at firewall (C)
- [ ] Run `tailscale status --json` live → evidence (C)
- [ ] Disable `mli`, revoke sessions, rotate secrets (D)
- [ ] Seize/image Kingston USB + other drives (E)
- [ ] Locate/image test PC 100.87.155.69 (E)
- [ ] Full disk image KOR-302N + collect hives (E)
- [ ] Start ILSpy decompile of RvtLib2025/ML.dll (F)
- [ ] Legal: Tailscale + Google process on the identity (G, H)
