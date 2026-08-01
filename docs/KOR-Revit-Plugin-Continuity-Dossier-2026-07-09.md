# KOR Structural — Revit / BIM 360 Plugin Continuity Dossier

**Subject:** Knowledge transfer and asset recovery from departing Revit/BIM lead (in-house plugin developer)
**Prepared:** July 9, 2026
**Prepared for:** Ian Lalonde, Operations
**Classification:** Internal — Operations / IT

---

## 1. Situation

KOR's Revit lead — the sole author and operator of an unknown number of in-house Revit plugins and BIM 360 / Autodesk Construction Cloud (ACC) integrations — is departing. Three compounding factors elevate this from routine offboarding to a continuity risk:

1. **Single point of failure.** He is the only person who has ever built, deployed, or maintained these tools. There is no second developer, no documented inventory, and (as far as is known) no source code on company infrastructure.
2. **Opacity.** He works secretively. The true inventory of what exists — plugins, cloud integrations, scheduled jobs, external accounts — is not independently known to KOR. Any list he provides cannot be assumed complete.
3. **Language barrier.** Verbal knowledge transfer will be low-fidelity. The handover plan must lean on demonstration, recordings, written artifacts, and independently verifiable evidence rather than conversation.

The objective is that on his last day, KOR can **rebuild, redeploy, and operate every plugin without him**, and that nothing in production is silently tethered to his personal accounts.

---

## 2. Risk Assessment

| # | Risk | Mechanism | Severity | Time to failure |
|---|------|-----------|----------|-----------------|
| R1 | BIM 360/ACC integrations die silently | APS (Forge) app registered under his **personal Autodesk account**; account deactivated or client secret rotated/expired after departure | **Critical** | Days to months after exit — fails without warning |
| R2 | Plugins cannot be rebuilt | Source code incomplete, stale, or never handed over; build only works on his machine | **Critical** | First time a change or new Revit version is needed |
| R3 | Annual Revit upgrade breaks everything | Revit API changes each release year; no one can recompile the add-ins for Revit 2027 | **High** | Next Revit version adoption |
| R4 | Unknown server-side components fail | Scheduled tasks, polling services, Design Automation jobs, webhooks, or databases only he knows about | **High** | Whenever the host machine reboots, a credential expires, or a job errors |
| R5 | Deployment mechanism lost | No one knows how add-ins reach user workstations (share, installer, script, manual copy) | **Medium** | First new hire or machine rebuild |
| R6 | Third-party license orphaning | Paid SDKs/components licensed to him personally | **Medium** | License renewal or reactivation |
| R7 | Inventory is incomplete | Secretive working style; handover list omits tools deliberately or accidentally | **High** | Discovered only when something breaks |
| R8 | Silent data egress / off-network source | Code hosted on personal GitHub/Gitee or personal cloud; company IP leaves with him | **Medium–High** | Immediate and permanent |

**The two critical risks (R1, R2) define the handover's two acceptance tests:** (a) every APS app is owned by a company-controlled Autodesk account, and (b) every plugin builds from source on a KOR machine and runs identically to production. Everything else supports those two outcomes.

---

## 3. Asset Classes at Risk

### 3.1 Source code and repositories

C#/.NET assemblies loaded by Revit via `.addin` manifests. The code may live in local folders, a personal Git host, or company infrastructure — location currently unknown. Full Git history matters (not a zip of "latest"): history reveals abandoned approaches, version-year branching strategy, and whether the delivered source actually corresponds to production binaries.

### 3.2 Build environment

Revit add-ins reference `RevitAPI.dll` / `RevitAPIUI.dll` from a local Revit installation, typically one project configuration or branch **per Revit release year**. Reconstruction requires: Visual Studio version, target frameworks, NuGet feeds (including any private feeds), reference paths, post-build steps (many Revit devs copy the built DLL + `.addin` into the add-ins folder via post-build script), and any code-signing certificates.

### 3.3 Autodesk Platform Services (APS / formerly Forge) applications

Any tool that touches BIM 360/ACC data from outside the Revit process — and some inside it — authenticates through an APS app with a **client ID and client secret**. These apps are registered to a specific Autodesk developer account. Key facts:

- APS supports **self-service ownership transfer**: the owner opens the app in the APS "My Apps" portal, selects Transfer Ownership, and invites the new owner by email. The invitee has **7 days** to accept before the invite expires. This must be done **while he is still employed and cooperative**.
- APS also supports adding **collaborators** to an app — useful as an interim step so a KOR account can see credentials before full transfer.
- In ACC/BIM 360 **Account Admin → Settings → Custom Integrations**, account administrators can see which custom integrations are authorized against the KOR hub. Caveat: the client ID is not displayed after the integration is created, so entries may show only app names — matching names to client IDs may require his input or APS-side records.
- Autodesk's newer **Secure Service Accounts (SSA)** exist for server-to-server auth; if he adopted these, they have their own key lifecycle to inventory.

### 3.4 Deployment plumbing

Revit discovers add-ins via `.addin` XML manifests in:

- `%ProgramData%\Autodesk\Revit\Addins\<year>\` (all users on the machine), or
- `%AppData%\Autodesk\Revit\Addins\<year>\` (per user)

How the DLLs and manifests reach ~every drafting workstation — network share with a copy script, an installer, group policy, or literally walking machine to machine — is undocumented. The mechanism must be reproduced by KOR staff before he leaves.

### 3.5 Server-side and scheduled components

Anything that runs outside Revit: Windows services or scheduled tasks polling BIM 360, APS **Design Automation** jobs, webhooks, small databases, cloud functions. These are the components most likely to be forgotten in a handover and to fail silently months later.

### 3.6 Third-party licenses

Paid SDKs or UI component libraries (common in the Revit ecosystem) may be licensed to him personally or to a personal email. Renewal and reactivation rights must move to KOR.

### 3.7 Undocumented operational knowledge

The "landmine list": hardcoded paths, machine-specific dependencies, known bugs and workarounds, which project teams depend on which tool, what breaks during the annual Revit version migration.

---

## 4. The Request List (what to ask him for)

### A. Inventory (the master list)

For **every** tool he has built or operates, a row with: name; purpose (one sentence); users/teams that depend on it; Revit versions supported; whether it touches BIM 360/ACC; where the source lives; where and how it is deployed; any external service, account, or credential it uses.

### B. Source code

1. Complete source for every plugin and tool, **with full Git history**, pushed to a KOR-controlled repository.
2. Explicit statement of where the code has lived until now (local disk, personal GitHub/Gitee, USB). If a personal remote exists, code is pushed to KOR **and** KOR requests deletion from the personal remote (see §8, IP).
3. Any shared/common libraries the plugins depend on that he also wrote.

### C. Build reproduction

4. Visual Studio version and required workloads; .NET target frameworks.
5. All NuGet feeds used, including private ones.
6. Reference paths for Revit API DLLs per supported Revit year, and the branching/configuration scheme for multi-year support.
7. Post-build/deploy scripts, signing certificates and their passwords.
8. A written build-from-clean recipe per plugin — then **proven** per Gate 1 (§7).

### D. APS / BIM 360 / ACC

9. List of every APS app: app name, client ID, auth mode (2-legged / 3-legged), scopes, callback URLs, which Autodesk account owns it.
10. **Ownership transfer executed** for each app to a KOR-controlled Autodesk account (service mailbox, not any individual's personal account), via the APS portal transfer flow. Interim: add a KOR account as collaborator immediately.
11. Client secrets handed over through a secure channel — then **rotated** by KOR after transfer, so the departing secret is dead.
12. List of webhooks, Design Automation activities/app bundles, SSA service accounts, hub/project IDs hardcoded anywhere.
13. Identification of which ACC Custom Integrations entries correspond to which client IDs.

### E. Deployment and operations

14. The exact deployment mechanism per plugin, demonstrated end-to-end (see §6, recordings).
15. Location of every server-side component: machine names, service/task names, run-as accounts, config files, connection strings.
16. Any credentials embedded in configs or code (flag for rotation).

### F. Licenses and accounts

17. Third-party components: what, license type, registered to whom, renewal dates, license keys.
18. Every external account used for KOR work (Autodesk, GitHub/Gitee, cloud providers, license portals) — enumerate, then transfer or decommission each.

### G. Knowledge capture

19. Per-plugin written Q&A (he may answer **in Chinese** — see §6).
20. The landmine list: hardcoded paths, machine-only behaviors, known bugs, annual-upgrade gotchas.
21. Screen recordings of the core lifecycles (§6).

---

## 5. Independent Verification (do not rely on his list)

Because the inventory cannot be trusted as complete, KOR builds its own in parallel. Gaps between the two lists become the interview agenda.

1. **Workstation sweep.** Script an inventory across all Revit workstations of both add-ins folders (`%ProgramData%` and every user's `%AppData%`) for all Revit years: every `.addin` manifest (which names its assembly path) and the referenced DLLs, with file hashes and versions. This yields the true deployed-plugin census and, via hash comparison, reveals version skew between machines.
2. **Binary escrow.** Copy every deployed in-house DLL (plus its `.addin`) into a preserved archive **now**, before anything is uninstalled or "cleaned up."
3. **Decompilation insurance.** C# decompiles to near-source with ILSpy/dotPeek. If delivered source is incomplete or doesn't match production binaries, working source can be recovered from the escrowed DLLs. This is the insurance policy, not the plan — but it removes his leverage as the only source-holder, which is worth knowing going into the conversation.
4. **ACC admin audit.** From KOR's own account-admin login: Account Admin → Settings → Custom Integrations. Record every entry. Anything there that isn't on his APS list is a finding.
5. **His workstation.** Image or preserve his machine before it is wiped/reassigned. Local repos, uncommitted work, build scripts, and secrets caches live there.
6. **Network/server census.** Check known servers for scheduled tasks or services running under his account or referencing Autodesk/Forge/APS endpoints; check for tasks that will break when his AD account is disabled.

---

## 6. Language-Barrier Protocol

- **Demonstration over explanation.** For each plugin: he performs the full cycle — open source, make a trivial change, build, deploy to a workstation, verify in Revit — while screen-recording with audio. Recordings survive the language barrier; conversation does not. One recording per plugin lifecycle, plus one for the APS/ACC admin side.
- **Written answers in Chinese are welcome.** Explicitly invite him to write documentation and Q&A answers in Chinese. Accurate Chinese + machine translation beats degraded English. Keep both versions.
- **Questionnaires, not meetings.** Send written per-plugin question sets in advance (machine-translated to Chinese as a courtesy); review answers asynchronously; use meeting time only for demonstrations and for gaps.
- **Checklists as shared language.** The request list in §4 becomes a literal shared spreadsheet with per-item status — item-by-item completion is legible to both sides regardless of language.

---

## 7. Acceptance Gates

Handover is complete only when all four gates pass. Schedule Gate 1 **early** — it generates the follow-up questions everything else answers.

- **Gate 1 — Clean rebuild.** For every plugin: clone from the KOR repo onto a clean KOR machine (not his), build using only the written recipe, deploy, and confirm behavior matches production. A plugin that fails this gate is not handed over, whatever the paperwork says.
- **Gate 2 — Credential independence.** Every APS app owned by a KOR-controlled Autodesk account; all client secrets rotated post-transfer; every integration re-verified working with the new secrets; ACC Custom Integrations list fully mapped to known apps.
- **Gate 3 — Deployment reproduction.** A KOR staff member (not him) deploys a plugin update to a workstation end-to-end using only the documented mechanism.
- **Gate 4 — Inventory reconciliation.** The workstation sweep, ACC audit, and server census each reconcile against his master list with zero unexplained entries.

---

## 8. Timeline and Administrative Items

Working back from his last day (**L**):

| When | Action |
|------|--------|
| L-15 to L-10 | Run workstation sweep, binary escrow, ACC audit (§5) **before** the handover conversation. Issue the §4 request list as a shared checklist. Add KOR account as APS collaborator on all apps. |
| L-10 to L-5 | Source lands in KOR repos. Attempt Gate 1 rebuilds immediately; log failures as questions. Record lifecycle demonstrations. APS ownership transfers initiated (7-day invite window — do not leave this to the last week). |
| L-5 to L-2 | Gate 2: transfers accepted, secrets rotated, integrations re-verified. Gate 3 deployment dry-run by KOR staff. Written Q&A returned; landmine list reviewed. |
| L-1 to L | Gate 4 reconciliation. Preserve his workstation. Final sign-off against the checklist. |
| L+1 | Disable accounts per standard offboarding; monitor integrations for auth failures over the following weeks (the R1 failure mode is delayed). |

**IP and HR notes** (flag for management/HR; not legal advice): code written in the course of employment is company property — the handover request is a right, not a favor. If source is on personal remotes, request migration **and** deletion, in writing. Consider making final sign-off, and any goodwill items (reference, farewell terms), contingent on gate completion. If any goodwill consulting arrangement post-departure is contemplated, it should be a documented rate-based agreement — not an implicit dependency that substitutes for the gates.

---

## 9. Sources

- [APS blog — You can now add collaborators to your app or transfer ownership](https://aps.autodesk.com/blog/you-can-now-add-collaborators-your-app-or-transfer-ownership-colleague)
- [APS blog — App Ownership Transfer](https://aps.autodesk.com/blog/app-ownership-transfer)
- [Autodesk Knowledge Network — Third-party Apps and Custom Integrations (BIM 360 Account Admin)](https://knowledge.autodesk.com/support/bim-360/learn-explore/caas/CloudHelp/cloudhelp/ENU/BIM360D-Administration/files/About-Account-Admin/GUID-0C83B441-C611-4574-8DA0-45D5CFC235FA-html.html)
- [APS — Manage API Access to BIM 360 Docs](https://aps.autodesk.com/en/docs/bim360/v1/tutorials/getting-started/manage-access-to-docs/)
- [APS blog — Secure Service Accounts (SSA) GA](https://aps.autodesk.com/blog/update-secure-service-accounts-ssa-goes-ga)
