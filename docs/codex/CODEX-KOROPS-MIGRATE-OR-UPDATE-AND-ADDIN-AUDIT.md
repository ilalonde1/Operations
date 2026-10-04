# CODEX — adversarial audit of the KOR-Operations migrate-OR-update delivery + the Outlook add-in load path

**Read-only. Open ONLY the files listed below, at HEAD, in this repo (`C:\VIsual Studio Projects\Operations`).**
No other files, no `git log`/`git diff` over a range, no network paths, no SQL connections.

**Do NOT run `dotnet build`, `dotnet test`, or anything else.** The test runner hangs here for 15+ minutes.
Read, think, write the report, stop. Verification runs on Claude's side.

**Reasoning: HIGH.**

## Why

Commit `e39bf0b9` shipped three things that now reach the whole firm, and they have never had a second
set of eyes:

1. A fleet **migrate-OR-update** operation that runs as **SYSTEM on every workstation** through the
   NetworkOps agent: it closes Outlook and the app, replaces the install at `C:\KOR-Operations`, and
   re-registers each signed-in user's VSTO Outlook add-in. If it half-completes, a user loses Outlook
   filing or the app until someone intervenes — on up to 27 machines at once.
2. A change to the **Outlook add-in startup path** (`ThisAddIn_Startup`) meant to make Outlook load
   faster, plus logging that measures the load time.
3. App/Serilog logging changes and a remote-viewer change.

The NetworkOps **service, map, agent trust boundary and transport** were already audited
(`docs/codex/CODEX-NETWORKOPS-WORKSTATIONOPS-AUDIT-2026-10-03.md`,
`docs/codex/CODEX-NETWORKOPS-AGENT-AUDIT*.md`). **Do not re-audit those.** This audit is the *delivery
payload* and the *add-in*, which those passes did not cover.

Fleet facts you need: WinRM and DCOM/RPC are blocked fleet-wide, so the only channel is the agent, which
runs a **script body** (no `param()`, no file args) as SYSTEM; the dispatcher prepends `$Sha`. The fleet
is uniform (flat `C:\Newerforma`, add-in `1.0.0.52`) and KOR-204 is the single test machine. The target
package is `1.0.0.55` in `V25.zip` on the share.

Be adversarial. Do not congratulate; do not restate what works except as a one-line clean verdict.

## Part 1 — the SYSTEM migration payload (where a defect costs the most)

Open:
- `Kor.Operations.NetworkOps.Core/Deploy/migrate-to-korops.ps1` (the payload)
- `Kor.Operations.NetworkOps.Core/Deploy/DeployCatalog.cs` (`Script()` prepends `$Sha`; package path)
- `Kor.Operations.NetworkOps.Service/Sweep/ActionRunner.cs` (the deploy branch: computes the package
  SHA-256 and dispatches via `MachineRunner` — read-only, it was audited before, but trace how `$Sha`
  reaches the script)

Try to break each of these invariants and give `file:line` + the exact sequence when you can:

1. **No-brick on failure.** The script renames `C:\KOR-Operations` aside, then `Move-Item`s the staged
   build in (steps around "Place V25 at C:\KOR-Operations"). If any step **between** the rename and a
   working install throws — Move-Item, the file-count check, a locked file — what state is the machine
   left in, and is the user's Outlook add-in now pointed at a path that does not exist? Is every failure
   recoverable by a plain re-run, with nothing left requiring a human on the box? Name the worst
   reachable half-state.
2. **The "already current" early-return.** It gates on `(Test-Path appExe) -and -not (old app) -and
   $installedVsto -eq $Pkg`, where `$installedVsto` is read from the **on-disk** `.vsto`, not from any
   user's registration. After one good run the on-disk file is `$Pkg`, so a re-run returns early and
   touches no user. Construct a real situation where a user is left mis-registered (wrong path or old
   version) and this early-return wrongly skips the fix.
3. **The reinstall change (the point of this commit).** In the per-user step, `reinstall` now **always**
   uninstalls then installs (the old path-only early-return was removed). If the **install** throws or
   exits non-zero **after** the uninstall succeeded, the user is left with **no** add-in. How long does
   that window last, what brings them back (Active Setup? the next op run?), and is "no add-in until next
   sign-in" acceptable, or should install failure roll back to the prior registration?
4. **User fan-out vs the timeout.** Per-user work runs through transient scheduled tasks, sequentially,
   each waiting up to 240 s, inside a 900 s op budget. With 3–4 signed-in users (RDS/shared boxes), can
   the op exceed its budget and be killed mid-user, and what state does that leave? Is the per-user wait
   bounded correctly if a `VSTOInstaller` hangs?
5. **`$Sha` binding.** `DeployCatalog.Script` builds `$Sha = '<hash>'` with `'`→`''`. The value is a
   service-computed SHA-256 hex today. Is there any path by which a non-hex or attacker-influenced value
   reaches this interpolation, and would the single-quote doubling hold if it did? Is the script's hash
   check (`$got -ne $Sha`) the only thing standing between a swapped `V25.zip` on the share and SYSTEM
   extracting it?
6. **Active Setup recovery.** Only users whose result shows OK get their per-user Active Setup `Version`
   bumped; a failed user is left to fire `reinstall` at next sign-in. Confirm that logic actually
   re-fires for the failed user and does **not** re-fire (churn) for the ones who succeeded. Does the
   HKLM `Version` vs HKU `Version` gate behave as assumed for a *future* version bump (V26)?

## Part 2 — the Outlook add-in load path (every user, every start)

Open `EmailFiler/EmailFilerv2/ThisAddIn.cs` and `EmailFiler/EmailFilerv2/ItemsToFileProcessor.cs`
(`SafeLog`, `GetFilingLogPath`).

7. **The instrument is on the hot path, over SMB.** `ThisAddIn_Startup` calls `SafeLog` twice (the
   "ADD-IN LOADED" line and the "returned in N ms" line); `SafeLog` does a **synchronous**
   `File.AppendAllLines` to a `\\kor-fs01\...` share. Does the logging added to *measure and fix* the
   slow load itself add latency to — or, if the share is slow/offline/cold, block — the very startup it
   reports? Does the reported "N ms" include or exclude its own writes, i.e. is the number honest?
   Propose the smallest change that keeps the measurement but takes the SMB write off the load path.
8. **The deferred-sync timer.** `_syncTimer` is a `System.Windows.Forms.Timer` started in Startup.
   (a) Is `ThisAddIn_Startup` guaranteed to run on a thread with a running message pump, so `Tick` ever
   fires? (b) If Outlook quits within the 2 s interval, can `Tick` fire during/after `Application_Quit`
   and call `SyncFolders()` on a tearing-down Outlook/MAPI COM object? (c) Is dropping the startup DB
   read (now re-read per-send) a real behavior change for anything other than load time?
9. **Filing before the first sync.** If a user files an email in the 2 s before the deferred
   `SyncFolders()` runs, the claim is "filing still works without the synced folders." Is that true —
   does any filing path depend on folders that only `SyncFolders` creates? Name the path if so.

## Part 3 — app logging + remote viewer (quick pass)

Open `Kor.Operations.App/CompositionModules/CompositionHelpers.cs`, `Kor.Operations.App/App.xaml.cs`,
`Kor.Operations.App/NetworkOps/KorRemoteViewerWindow.xaml.cs`, `Kor.Operations.App/NetworkOps/KorRemoteBridge.js`.

10. **Serilog.** The File sink is now `shared:true` at `MinimumLevel.Debug` with `CloseAndFlush` on exit.
    (a) Can log lines be lost on a crash that bypasses `OnExit` (no periodic flush), and does the
    separate startup-crash file cover that gap? (b) At `Debug` fleet-wide, does any existing `Log.Debug`/
    `Verbose` call write a secret — connection string, token, credential, PII? Name the call site if so.
11. **Viewer.** Is the 8 s no-bridge watchdog cancelled/disposed when the window closes early, or can it
    fire on a closed window? In `KorRemoteBridge.js`, does the monitor de-dup keep exactly **one** "All"
    plus each distinct real monitor, and can it ever drop a legitimately distinct second monitor?

## How to answer

Write the report to **`docs\codex\CODEX-KOROPS-MIGRATE-OR-UPDATE-AND-ADDIN-AUDIT-RESPONSE.md`** (that file
only; change no code). **At most ~1,800 words.** Rank findings by cost: `file:line`, what goes wrong, the
concrete trigger, the smallest fix. A reasoned refusal of a suspected issue is a valid, useful answer —
say "checked, correct, because…". End with **exactly one** thing to fix before this runs on the fleet
beyond KOR-204 (or "none" if nothing deserves it).
