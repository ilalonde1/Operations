# EXECUTION BRIEF — Phase A: FS01 off-site copy (Claude Code on KOR-BK01)

You are on **KOR-BK01** (Veeam B&R 12.3.2). Building **Phase A** of the backup redesign: an off-site
Backup Copy of KOR-FS01 to the existing T-Net Cloud Connect repository.
Full design: `docs/KOR-Backup-Target-Design-v3-2026-07-23.pdf`. Operator: Ian Lalonde.

Load Veeam PowerShell: `Import-Module Veeam.Backup.PowerShell -EA SilentlyContinue`

---

## ⛔ HARD RULES
1. **This is a CREATE task. Delete nothing. Modify no existing job.**
2. **Do NOT start an uncontrolled full-speed WAN seed.** Configure throttling FIRST (Task 1). After the job is created, **STOP and report to Ian** — he decides when/whether to start the seed (5.8 TB over the office internet).
3. **Do NOT touch** the existing `Kor-Replication` copy job, `Kor-FS01` backup job, or any other job/repo config.
4. **Cloud Connect fallback:** if creating the copy job via PowerShell is ambiguous, errors, or you're unsure the target resolves to the correct Cloud repo — **STOP, do not force it, and report** that this piece should be done via the GUI wizard. A misconfigured off-site job is worse than a slower path.
5. Report results and WAIT.

## Context / facts
- Source: the `Kor-FS01` backup (forward-incremental chain on Synology105, F:). ~5.8 TB full + increments.
- Target: the **Cloud Connect** repository `Kor-Replication` at `remote.t-net.ca` (25 TB, ~11.5 TB free — fits).
- The Cloud Connect connection already works (the existing `Kor-Replication` copy job uses it nightly).
- Office upload measured at **182 Mbps**. Goal: full speed off-hours, throttled during business hours (Mon–Fri ~8:00–18:00).

---

## TASK 1 — Network throttling rule (protect the office link) — do this FIRST
Create a global network traffic rule that throttles Veeam traffic to the Cloud Connect endpoint during business hours, unlimited off-hours. Target the provider network (resolve `remote.t-net.ca` to its IP/range).
- Suggested: throttle to ~50% of upload (~90 Mbps ≈ 11 MB/s) Mon–Fri 08:00–18:00; unlimited otherwise.
- Cmdlet path: `Get-VBRNetworkTrafficRule` / `Add-VBRNetworkTrafficRule` (or Main Menu → Network Traffic Rules in GUI if PS is unclear).
- Report the rule you created.

## TASK 2 — Create the Backup Copy job (attempt; fall back to GUI if fiddly)
- Name: **`Kor-FS01 Offsite Copy`**
- Source: the `Kor-FS01` backup / job.
- Target repository: **`Kor-Replication`** (Cloud Connect).
- Mode: **immediate copy** (mirror new restore points) preferred; periodic acceptable.
- Retention: sensible default (e.g., keep 7–14 restore points off-site). GFS can be tuned later.
- **Create it in a NOT-started / respect-throttle state.** Do not let it begin transferring 5.8 TB before Ian confirms timing.
- If any Cloud Connect target ambiguity → STOP per Hard Rule 4.

## TASK 3 — Report to Ian
- The throttling rule created (Task 1).
- The copy job config: source, target repo (confirm it's the Cloud repo), mode, retention, current state (started? throttled? idle?).
- Your recommendation on starting the seed now (evening = fine at full speed) vs waiting.
- Anything that needed the GUI fallback.
Then STOP — Ian decides when the seed runs.
