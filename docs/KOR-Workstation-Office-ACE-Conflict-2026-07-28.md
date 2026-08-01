# KOR Workstations — Access Database Engine vs Click-to-Run Office conflict

**Date:** 2026-07-28
**Trigger:** kevinw (KOR-206-N) reported Outlook calendar slow to load and crashing "for the past few days"
**Scope:** 27 workstations swept read-only; 13 affected
**Author:** Ian Lalonde

---

## Bottom line

Kevin's ticket was two unrelated faults wearing one costume, and the crash half turns out to be a **fleet-wide condition on 13 machines**, not a one-off.

The **Microsoft Access Database Engine 2016 redistributable** installs its own down-level copies of the Office shared DLLs into `C:\Program Files\Common Files\microsoft shared\Office16\`. Click-to-Run Office binds to those instead of its own, and the result is crashes in `mso20win32client.dll` and a daily `opushutil.exe` delay-load failure.

**The fix is to update the redistributable, not to delete the folder.** The folder holds the ACE OLEDB providers that Excel/Access data access depends on, and it belongs to a registered MSI product.

---

## Evidence

### The crash

```
2026-07-28 15:03:53  Application Hang   OUTLOOK.EXE 16.0.20131.20154
2026-07-28 15:03:59  Outlook event 59   Skype Meeting Add-in disabled —
                                        "This add-in caused Outlook to crash"
2026-07-28 15:04:00  Application Error  OUTLOOK.EXE faulting module
                                        mso20win32client.dll v16.0.5023.1000 (dated 2020-07-14)
```

Outlook was build **20131.20154** but loaded a **2020-vintage** shared DLL. The correct C2R copy (`16.0.20228.20110`) sits unused under `root\vfs\ProgramFilesCommonX64\`. The stale copy is Authenticode **Valid, CN=Microsoft Corporation** — a legitimate Microsoft component in the wrong place, not a sideload. That mattered to check given the two mailbox compromises on record.

### Causal proof — 8/8 clean split

Holding the Office build constant at `20228.20110` and varying only the presence of the Access Engine:

| Access Engine | Machines | `opushutil.exe` crashes / 14d |
|---|---|---|
| **Present** | KOR-104N, KOR-207, KOR-307-N, KOR-206-N | 23, 16, 16, 17 |
| **Absent** | KOR-305, KOR-306, KOR-308, KOR-320 | **0, 0, 0, 0** |

`opushutil.exe` fails with `0xc06d007f` — a delay-load failure, exactly what a bad shared-DLL bind produces. No machine without the Access Engine exhibits it; every machine with it does.

### What is actually in the folder

34 files, 147.5 MB, all dated 2020-07-14/15:

`ACEOLEDB.DLL`, `ACEODBC.DLL`, `ACEDAO.DLL`, `ACEEXCL.DLL`, `ACEEXCH.DLL`, `ACETXT.DLL`, `ACEWSS.DLL` … plus `MSO.DLL`, `Mso20win32client.dll`, `Mso30win32client.dll`, `MSORES.DLL`, `EXPSRV.DLL`, `VBAJET32.DLL`

The `ACE*` files are the Access Connectivity Engine. The `MSO*` files are its dependencies. Registered in Uninstall as **"Microsoft Access database engine 2016 (English)"**.

> **Do not delete or rename this folder.** Anything reading `.xlsx`/`.mdb` via `Microsoft.ACE.OLEDB.*` breaks. The KOR Operations codebase itself does not reference ACE (grepped clean), but Deltek reporting, Bluebeam, Newforma and any Excel-driven workflow are unverified consumers.

---

## Affected machines

| Down-level version | Date | Machines |
|---|---|---|
| `16.0.5023.1000` | 2020-07-14 | KOR-206-N, KOR-206, KOR-207, KOR-208-N, KOR-213, KOR-223N, KOR-224, KOR-307-N |
| `16.0.5495.1002` | 2025-03-22 | KOR-104N, KOR-202, KOR-210, KOR-216 |
| `16.0.5452.1000` | 2024-05-15 | KOR-217 |

**Clean (14):** KOR-101, 204, 205, 213-N, 302N, 304, 305, 306, 308, 310, 314, 319, 320, Kor-RDS01

Note the 2025-03-22 group — four machines received a **newer** Access Engine last year, which is the precedent for the recommended fix. They still show the conflict (104N: 23 opushutil crashes), so updating reduces version skew but does not by itself eliminate it. Removal is the only complete fix where ACE is genuinely unused.

---

## Unreported problems the sweep found

Nobody raised tickets for these.

- **KOR-207 (Mark Bakhtavar)** — see the dedicated section below. The initial "31 duplicate stores" reading was **2023 residue in an abandoned profile**, not live damage. The real problems there are Revit crashes and a failing search index.
- **KOR-224 (sgoyal) — 10 duplicate stores.** Apply the same staleness test before treating these as live.
- **KOR-308 (szheng) — Bluebeam `Revu.exe` crashed 14× between 07-15 and 07-28**, every one `ucrtbase.dll` `0xc0000409`. Unreported and ongoing.
- **`Kor.Operations.App.exe` crashed 9× in 14 days on KOR-206-N** — `System.InvalidOperationException: DialogResult…`. Our own application; separate defect.
- **EmailFilerv2 add-in load cost:** 546 ms (206-N), 782 ms (207), 469 ms (307-N) — against 0–63 ms for every other add-in. This is the known-stale VSTO pending redeployment to v22.

---

## Changes applied to KOR-206-N on 2026-07-28 (~19:05–19:16)

Kevin was logged off; Outlook closed and all files unlocked before any change.

| # | Change | Before | After | Rollback |
|---|---|---|---|---|
| 1 | `UCAddin.LyncAddin.1` LoadBehavior | `0x3` | `0x0` | `Set-KorOutlookAddinState -ComputerName KOR-206-N -ProgID UCAddin.LyncAddin.1 -LoadBehavior 3` |
| 2 | `UCAddin.UCAddin.1` LoadBehavior | `0x2` | `0x0` | `… -ProgID UCAddin.UCAddin.1 -LoadBehavior 2` |
| 3 | Windows Search catalog rebuild | 1014.4 MB | 4.8 MB, repopulating | none needed; rebuild is self-completing |
| 4 | RemoteRegistry service | Disabled / Stopped | **restored** to Disabled / Stopped | n/a — already restored |

**Not applied:** renaming `Common Files\microsoft shared\Office16`. This was approved, but aborted on discovering the folder belongs to the Access Database Engine. Renaming it would have broken Excel/Access data access across the machine.

Skype for Business Online was retired by Microsoft in **July 2021**; the Teams Meeting Add-in loads and works alongside it. Disabling the Skype add-ins removes dead weight and the confirmed crash trigger.

---

## KOR-207 (Mark Bakhtavar) — assessed 2026-07-28

### Correction to the first reading

The 31 duplicate `.nst` stores are **not a live fault**. Every one was created between **2023-04-10 and 2023-05-15**, in the `mark bakhtavar` profile that was superseded on **2025-08-30** when the `markb` profile was created. They are 614 MB of residue from a problem that resolved itself three years ago.

The **active `markb` profile has exactly one `.ost` and one `.nst`** — a clean store layout. My earlier statement that this machine was "worse than the ticket that started this" was wrong, and it was wrong because the tool reported file-count without file-age. Both defects are now fixed in `tools/WorkstationOps` (see below).

### What is actually wrong

**1. Search indexing failing on the active profile** — same fault class as KOR-206-N, and the attribution is now verified against the full path rather than the file name:

```
2026-07-17 21:20:49  markb@korstructural.com.ost  error=0x8034081f
2026-07-16 21:26:21  markb@korstructural.com.nst  error=0x80370005
2026-04-30 14:40:21  markb@korstructural.com.ost  error=0x8124081f
2026-03-19 21:45:36  markb@korstructural.com.nst  error=0x8034081f
```

Recurring since at least March. The KOR-206-N rebuild procedure applies directly.

**2. Revit crashing repeatedly — the most user-impacting problem here.** Five crashes inside 23 minutes on 2026-07-28:

```
12:20:22, 12:21:08, 12:27:49, 12:43:08, 12:43:41
Revit.exe v26.2.0.20 → ucrtbase.dll  exc=0xc0000409
```

`0xc0000409` is a C-runtime fail-fast. **KOR-207 is the only machine in the fleet with any Revit crashes at all**, and it is also running the oldest Revit 2026 build in service:

| Revit build | Machines |
|---|---|
| **26.2.0.20** | **KOR-207** |
| 26.0.4.409 | KOR-307-N |
| 26.4.0.32 | KOR-306, KOR-308 |
| 26.4.10.51 | KOR-213-N, KOR-302N, KOR-310 |

Updating KOR-207 to the fleet standard `26.4.10.51` is the obvious first move. Stated honestly: this is a correlation with n=1, not a proven cause.

**3. Bluebeam `Revu.exe` crashed 6×**, four of them faulting `nvoglv64.dll` (NVIDIA OpenGL).

### The NVIDIA driver hypothesis — tested and rejected

Worth recording so nobody re-derives it. KOR-207 runs `nvoglv64.dll 32.0.15.8142` (2026-01-07) against a fleet standard of `32.0.15.9651` (June 2026), which looked like an obvious answer. It is not:

| Host | Driver | Revu crashes | faulting nvoglv64 |
|---|---|---|---|
| KOR-207 | 8142 | 6 | 4 |
| **KOR-206** | **8142 (identical)** | **0** | **0** |
| KOR-308 | 8142 | **14** | 0 |
| KOR-310 | 9651 (fleet std) | 1 | 0 |

KOR-206 shares the exact driver and crashes zero times; KOR-308 crashes 14 times on that driver without the driver ever being the faulting module. **Driver version does not predict the crashes — do not push a driver update as the fix.**

The more interesting signal is that KOR-308's 14 Revu crashes carry the *same* `ucrtbase.dll` `0xc0000409` signature as KOR-207's Revit crashes. That points at something shared across Autodesk/Bluebeam on Windows `26100.8875` rather than anything machine-specific. Confirming it would need dump analysis, which is not reachable over `c$`.

### Separately: fleet-wide graphics driver fragmentation

Eleven distinct NVIDIA driver versions across 21 machines, from **2021-07-20** (KOR-320, `30.0.14.7141` — five years old) to 2026-07-03. Not the cause of anything here, but it is not a defensible baseline for a firm running Revit.

### Applied to KOR-207 on 2026-07-28 ~19:50

Mark was **actively working** — his `.ost` was locked and written seconds earlier — so only the change that is invisible to a running Outlook was made.

| Change | Before | After | Rollback |
|---|---|---|---|
| `UCAddin.LyncAddin.1` LoadBehavior | `0x3` | `0x0` | `Set-KorOutlookAddinState -ComputerName KOR-207 -ProgID UCAddin.LyncAddin.1 -LoadBehavior 3` |
| `UCAddin.UCAddin.1` LoadBehavior | `0x3` | `0x0` | `… -ProgID UCAddin.UCAddin.1 -LoadBehavior 3` |
| RemoteRegistry | Disabled/Stopped | **restored** Disabled/Stopped | n/a |

Preventive rather than reactive: KOR-207 carries the same ACE skew that produced the KOR-206-N crash, with the Skype add-in still enabled. Takes effect at Mark's next Outlook restart.

**Search index rebuilt 2026-07-28 21:42**, once Mark confirmed he was done and both stores were unlocked. Catalog `339.9 MB → 16.2 MB`, repopulating (41.4 MB at 21:44). Completes overnight.

> **Bug caught during this run — `Invoke-KorSearchIndexRebuild` silently no-opped and reported success.**
> Windows Search **ignores** the `SetupCompletedSuccessfully = 0` flag if the service is restarted immediately after the registry write. The flag stayed `0x0`, the old 339.9 MB catalog survived untouched, and the function returned `RebuildConfirmed: True` because its check was merely `$after -lt $before` — an 8 MB drift passed it.
> A manual stop/start ~5 minutes later worked immediately. Both defects are fixed: the function now polls for the catalog to actually collapse below 50% of its starting size, cycles the service up to 3 times if the flag was not honoured, emits a `Write-Warning` when the rebuild genuinely did not run, and only reports `RebuildConfirmed` on a real collapse.
> Worth remembering as a class of failure: **a remediation that reports success without verifying the state actually changed is worse than one that fails loudly.**

---

## KOR-308 (Stephanie Zheng) — Bluebeam crashes, unresolved

**14 `Revu.exe` crashes in 14 days**, all `ucrtbase.dll` `0xc0000409` (C-runtime fail-fast). Seven on 07-27 alone, three inside twenty minutes. She rebooted at 13:40 that day; they resumed at 15:49. **Crash dumps exist from August 2025 in her previous profile** — so this has run for roughly a year and follows the machine across two Windows profiles.

### Eliminated, each against a control

| Hypothesis | Killed by |
|---|---|
| Revu version (20.3.15.47, June 2023) | **KOR-205** runs an *older* build (20.1.15.12, 2020), actively, with **0 crashes** |
| Usage volume | **KOR-202** has a 8055 KB recent-files store — **3× Stephanie's 2491 KB** — on the Revu 20 line, **0 crashes** |
| NVIDIA driver (32.0.15.8142) | **KOR-206** runs the identical driver, **0 crashes** |
| Access Database Engine conflict | KOR-308 is **clean** of the residue |
| Failing hardware | System log 2025-12 → 2026-07: **zero WHEA-Logger events**, no disk faults, all NTFS volumes report healthy |
| Bluebeam Studio config | All five compared machines carry the same `ServerCache` and Studio URLs |

> **Methodology note.** The first control run was flawed: it compared raw crash counts without checking whether those machines *use* Bluebeam. KOR-224's zero was meaningless — 368 days idle. Re-running against last-used dates showed KOR-205 and KOR-204 are genuinely active, which is what makes their zeros load-bearing. **Always confirm the control is exercising the thing you are controlling for.**

### One unexplained anomaly

Her `UserPreferences.xml` is **27 KB against 1655–1679 KB** on all five healthy comparators — a 60× gap, and 308 is the sole outlier. Her `.backup` copy is similarly small, so it is not a one-off truncation.

**Direction of causation is unknown.** Revu rewrites preferences on crash recovery, so a repeatedly-crashing client plausibly produces a truncated prefs file — meaning this may be a symptom rather than the cause. It is recorded because it is the only measured difference that survived every control.

### What remains

**Ten crash dumps, ~62 MB each, sit in `C:\Users\szheng\AppData\Local\CrashDumps`.** They are the only direct evidence left and they require a debugger — no `cdb`/`windbg` is installed on KOR-1001.

Two ways forward:

1. **Definitive** — install Debugging Tools for Windows and read the faulting stack, or send one dump to Bluebeam support. This is the only path that actually answers the question.
2. **Pragmatic** — upgrade KOR-308 to the fleet standard **21.10.0.19316**. Revu 20.3.15.47 is a 2023 build running on Windows 11 `26100`, and she is by far the heaviest crasher. **Stated honestly: this is not proven causal** — KOR-205 disproves version as a general explanation. It is cheap, it aligns her with 8 other machines, and it is worth doing regardless.

Recommended: do (2) now, and preserve one dump so (1) stays available if the crashes survive the upgrade.

---

## Recommended next steps

1. **Confirm who needs ACE.** If nothing on a given machine reads Excel/Access via OLEDB, uninstalling "Microsoft Access database engine 2016" is the complete fix. This is the only step that fully removes the conflict.
2. **Where ACE is required**, update to the current redistributable to reduce version skew.
3. **KOR-207 before anything else** — 31 duplicate stores plus failing search indexing is a worse user experience than the ticket that started this, and it is unreported.
4. **Redeploy EmailFilerv2 v22** to 206-N, 207 and 307-N; the stale build costs half a second of every Outlook launch and mis-tags manual filings.
5. **Fix the `Kor.Operations.App` DialogResult crash** — 9 occurrences in 14 days on one machine alone.

---

## Tooling

This entire investigation is reproducible via `tools/WorkstationOps`:

```powershell
Import-Module .\tools\WorkstationOps\Kor.WorkstationOps.psd1
Get-KorWorkstationHealth -ComputerName KOR-206-N | Select-Object -ExpandProperty Findings
```

The module works without WinRM: file access over `c$`, service control via `sc.exe`, and registry only inside `Use-KorRemoteRegistry`, which always restores RemoteRegistry to Disabled in a `finally` block.
