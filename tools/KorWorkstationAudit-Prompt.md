You are auditing and tuning a KOR Structural domain workstation (domain int.korstructural.com, sole DC/DNS = KOR-DC01 at 192.168.1.30, file server KOR-FS01). Engineering firm: users run ETABS, Revit, Office. Their real project files live on FS01; local files may be scratch copies that are still load-bearing. You run autonomously — no questions. These rules bound you:

HARD RULES:
1. NEVER move, rename, or delete user-created files. Local documents, models, and downloads are analysis-only on this machine. Deletion is permitted ONLY for: %TEMP%, C:\Windows\Temp, browser/app caches, Windows Update download cache, thumbnail caches, and Delivery Optimization files.
2. NO changes to: network adapters, DNS, firewall rules, domain membership, GPO, certificates, mapped drives, or anything on a server or share (except writing audit output as directed). Misconfigurations get REPORTED, not fixed.
3. NO uninstalls, no registry edits, no third-party "cleaner" tools, no driver updates, no reboots. Windows Update may scan/download/install but must NOT reboot — if a reboot is pending, report it.
4. Startup/scheduled-task bloat may be DISABLED (never removed) — but never touch anything security-related (Defender, Veeam agents, NinjaRMM/ScreenConnect while T-Net handover is in progress, VPN, backup agents).
5. LEDGER every change to C:\_KORAudit\fixes-ledger.csv (timestamp, action, target, reason). Analysis findings do not need ledger rows; changes do.
6. All output goes to C:\_KORAudit\. At the end, attempt to copy the folder to \\KOR-FS01\Management\IT\Workstation-Audits\<HOSTNAME>\ (create path if needed); if the share is unreachable, note it and finish locally.

PHASE 1 — BASELINE AUDIT (read-only, collect everything before fixing anything):
- Identity/OS: hostname, OS + build, Windows 10 vs 11 (Win10 = end-of-support since Oct 2025 — flag prominently), install date, last boot, pending-reboot state.
- Domain health: Test-ComputerSecureChannel, DNS servers (must be 192.168.1.30 — report deviation), time sync source and offset (w32tm), gpresult summary.
- Security posture: Defender status + last scan + definition age, BitLocker status per volume, local Administrators group membership (list every member — flag non-standard accounts), UAC level, SMBv1 status, RDP enabled/exposed.
- Update health: Windows Update last success, pending updates count, update service errors.
- Hardware health: Get-PhysicalDisk health + free space per volume, RAM total, event log scan (last 30 days) for disk errors (Event 7, 51, 153), WHEA errors, and apps crashing repeatedly.
- Software: full installed-program inventory to software-inventory.csv (name, version, publisher, install date). Flag: expired trials, toolbars, duplicate versions of the same tool, anything with known-EOL versions.
- Performance: startup programs + measured boot impact, scheduled tasks from non-Microsoft publishers, services set Automatic that are stopped/failing.
- Local data exposure: total size of local user profiles; list folders >5GB and files not touched in >1 year under user profiles (report-only — candidates to migrate to FS01).

PHASE 2 — STANDARD:
- Check \\KOR-FS01\Management\IT\Workstation-Audits\KOR-Workstation-Standard.md (fall back to C:\_KORAudit\).
- If it EXISTS: diff this machine against it — missing software, extra software, config drift, security-posture drift. Write drift-report.md.
- If it does NOT exist: generate a DRAFT KOR-Workstation-Standard.md from this machine's healthy state — required software set, security config (Defender/BitLocker/UAC/local-admin policy), update posture, DNS/time expectations, startup hygiene. Mark it DRAFT — pending Ian's approval.

PHASE 3 — SAFE FIXES (each one ledgered):
- Clear the rule-1-permitted junk; record GB recovered.
- DISM /Online /Cleanup-Image /RestoreHealth, then sfc /scannow; report results.
- DISM /StartComponentCleanup (no /ResetBase).
- gpupdate /force; w32tm /resync.
- Defender: update definitions, run quick scan.
- Windows Update: scan, download, install — no reboot.
- Disable ledger-worthy startup bloat per rule 4.

PHASE 4 — REPORT. Write to C:\_KORAudit\:
- audit-report.md — health grade at top (GREEN/AMBER/RED with one-line justification), then findings by phase. Lead with anything matching known fleet issues: Windows 10 EOS, pending reboots, disk health warnings.
- decisions-needed.md — every deferred item (uninstall candidates, misconfigs found, migration candidates, security concerns) with a one-line recommendation each.
- fixes-ledger.csv and software-inventory.csv as accumulated.
Then attempt the share copy per rule 6.

Run the phases in order. Anything ambiguous goes in decisions-needed.md, not a question. Take the time you need.
