Read-only audit of the nine specified source files at HEAD, 2026-10-03. No builds, tests, deployment, network access, or SQL. Findings are static; external implementations and runtime behavior not established by these files are identified below. References use the listed files' basenames.

**1. High — deployment is not failure-atomic, and reruns can abandon repair.** `migrate-to-korops.ps1:125`, `:131–139`, `:190–191`, `:90–92` (questions 1–3).

On an already-migrated workstation, uninstall succeeds, the current directory is renamed, and `Move-Item` fails. The catch only reports: the app path is absent, successful uninstalls leave no add-in, and offline users' registrations still name the missing original path. The old install survives under a timestamped name but is never restored. A transient move failure can recover on rerun; a persistent lock/permission failure cannot. Even a rename failure occurs after users have been uninstalled.

A worse recovery trap occurs when placement succeeds but the file-count/enumeration check or later work fails: if the app executable and current manifest remain, an update rerun returns `AlreadyCurrent` without repairing users or Active Setup. Missing dependencies are not checked by that predicate. The previous directory is deleted at line 139 before any replacement registration is confirmed.

Concrete first-migration trap: uninstall Alice, place V25, fail Alice's install, complete cleanup of Newerforma. Rerun now skips Alice. Another: after a good deployment, Alice's registration is removed or restored from an old profile; disk state still passes the gate. The outage lasts until an independently successful repair, potentially indefinitely—not merely until the next operation.

Smallest viable correction: retain the previous package and registration identities until validation, restore them on failure, and separate package-current detection from per-user reconciliation. A local durable pending-repair record must survive interruption; a catch alone cannot recover a killed process.

**2. High — the recovery helper is writable by every local user.** `migrate-to-korops.ps1:84–85`, `:62`, `:159`, `:163`.

The recursive Users:Modify grant covers `user-step.ps1`, not just results. After SYSTEM writes it, any local user can replace it before another user's scheduled task or next Active Setup execution. This executes attacker-chosen code as that other user; it is not evidence of SYSTEM execution. Shared writable result files also allow fabricated `OK=True` to suppress another user's recovery. Fix: keep executable content administrator/SYSTEM-writable only, put results in separately ACLed per-SID locations, and verify registration rather than trusting shared text.

**3. High — fan-out exceeds the deadline and leaves installers uncontrolled.** `migrate-to-korops.ps1:31`, `:60–71`, `:125`, `:142`; `DeployCatalog.cs:27`; `ActionRunner.cs:95` (question 4).

There are TWO sequential 240-second waits per user: uninstall and install. Three users permit 1,440 seconds and four permit 1,920, excluding staging. Even two permit 960. With four hung uninstalls, the 900-second deadline can arrive before placement; with slow installs it can arrive after every old registration is removed and before Active Setup is configured.

The polling deadline bounds waiting approximately, not installer execution: `Start-Process -Wait` has no timeout, and there is no explicit task/process termination before unregistering. A running installer is not proven stopped and could overlap later work. Checking only `Running` can also accept a queued/not-yet-started task as finished. The actual agent kill mechanism is outside scope; the payload provides no safe response to that interruption.

Fix: use a single remaining-time budget with rollback reserve, require task completion plus explicit installer success, and stop/reap the task and installer tree on timeout before proceeding.

**4. High — Active Setup is conditional recovery, not a retry guarantee.** `migrate-to-korops.ps1:44–56`, `:155–166` (questions 3, 6).

For a first V25 run reaching line 160, a failed user's absent/older HKCU version remains below HKLM, while a confirmed successful user's equal `25,0,0,0` avoids churn. That comparison is correct under those conditions.

It fails for a user already stamped 25: an interrupted/failed same-version reinstall does not lower that stamp, so the next sign-in has nothing newer to run. Failure before configuring HKLM also provides no new recovery gate. Furthermore, the helper logs `OK=False` but never explicitly exits nonzero or manages a success-only retry marker. Active Setup's version stamp must not be treated as proof that VSTO installed; repeated recovery after a failed stub is not established here.

For V26, both hard-coded version writes must become `26,0,0,0`; changing `$Pkg` alone leaves V25 users skipped. The successful-user stamp must equal the machine gate. Fix: derive both from one deployment version, verify installed identity/version, and persist failed-user repair independently of Active Setup. Retain a usable old deployment for rollback when uninstall succeeds but install fails; waiting for another sign-in is not an acceptable guaranteed recovery strategy.

**5. High — payload failure can be recorded as deployment success.** `migrate-to-korops.ps1:180–185`, `:190–191`; `ActionRunner.cs:96–99`, `:265`.

An install failure still returns `Done=true` with `AddinFailed`; a caught placement exception returns `Done=false` as ordinary output. The deploy branch bases success solely on `OnTargetStatus.Ok`, without inspecting either field; `ResultLine` does not interpret them. Thus a normally completed script carrying failure can be reported as “ran” and queued for checking. Whether the excluded transport independently interprets these fields is unverified. Fix at this call site: require the payload's explicit deployment verdict and zero unresolved signed-in users before recording success.

**6. Medium — startup telemetry blocks startup and understates its own cost.** `ThisAddIn.cs:36–39`, `:82`; `ItemsToFileProcessor.cs:40–47`, `:328–337` (question 7).

Both calls synchronously append over SMB. Cold authentication, an unavailable server, or a slow share blocks Outlook until the filesystem call returns; swallowing exceptions does not bound latency. The reported duration includes the first append but excludes the second append, because interpolation reads the stopwatch before `SafeLog` executes. It measures neither the complete callback nor total VSTO loading.

Smallest fix: capture immutable messages and elapsed time on the Outlook thread, then enqueue both writes to one background worker without awaiting it. Keep COM access on the Outlook thread; label the metric “Startup body elapsed.” A local spool can preserve telemetry across immediate exit without requiring SMB on this path.

**7. Medium — deferred work has no shutdown guard; folder-based filing is temporarily unavailable.** `ThisAddIn.cs:53–60`, `:85–99`; `ItemsToFileProcessor.cs:96`, `:166`, `:198–200` (questions 8–9).

A WinForms timer is compatible with normal Outlook UI-thread startup and its message pump; STA alone does not guarantee delivery, and two seconds is a minimum delay, not a completion guarantee. No missing-pump defect is demonstrated in these files. However, neither Quit nor Shutdown stops/disposes the timer. If Outlook pumps messages during teardown, Tick can enter `SyncFolders` against a closing MAPI session. This is reentrancy/lifecycle exposure, not evidence of two concurrent UI threads. Fix: mark quitting and stop/dispose before `ProcessOnQuit`, also clean up on Shutdown, and guard Tick.

The blanket “filing still works” claim is too broad: drag/move into a newly needed `Emails To File/<project>` folder requires SyncFolders to create it. Existing folders persist, and `ProcessOnQuit` can consume them without this session's sync. Quick File (`:870–890`) and sent-item filing (`:830–842`) resolve projects independently and do not require those Outlook folders. The excluded ribbon/FileOnSend implementations prevent certifying every filing entry point. Show folder preparation state or ensure the needed folder on demand.

Dropping the startup preference read is checked, correct for the visible consumers: `_autoFileOnSend` is overwritten at `ThisAddIn.cs:110` before its only decision at `:114`; read failure still defaults true.

**8. Medium — raw launch arguments enter logs.** `App.xaml.cs:108–109`, `:124` (question 10b).

Debug logs receive the entire argument string: any supplied sensitive filename, personal data, or credential-shaped argument reaches `{Args}`. Information logging already duplicates it (`:64–66`); the startup-crash file writes arguments and exception text directly (`:41–43`). This demonstrates an unfiltered input path, not an observed leaked credential. The configured redactors' implementations are excluded, so their coverage cannot be certified. Fix: log argument mode/count and explicitly allowlisted values; sanitize crash output too. No repo-wide clean verdict is possible within the file restriction. `MinimumLevel.Debug` does not enable Verbose.

**9. Low — watchdog survives window closure.** `KorRemoteViewerWindow.xaml.cs:80`, `:123–133` (question 11).

Close before eight seconds without a bridge message: the dispatcher timer still fires and logs a misleading warning, retaining the window until then. Its callback only logs, so a disposed-WebView access is not demonstrated there. Stop/detach/null the timer on Closed, and prevent asynchronous startup from starting it after closure. DispatcherTimer has no Dispose requirement.

**Remaining checks.** `$Sha`: checked, correct on the inspected path (`ActionRunner.cs:87–95`, `DeployCatalog.cs:42`): SHA-256 becomes hex, with no request-supplied string. ASCII quote doubling protects the quoted assignment on that path; enforce 64 hex characters if the API gains other callers. The payload comparison (`:98–103`) detects package replacement after dispatch hashing, not malicious replacement before hashing. No independently trusted release digest/signature is checked before SYSTEM extraction; VSTO installation checks occur later. Pin a trusted release digest if share contents are not the release authority.

Serilog (10a): no queued/buffered sink is configured (`CompositionHelpers.cs:151–160`), so lack of periodic flush alone does not establish lost completed events. Ordinary synchronous File-sink behavior flushes events; in-flight writes and OS/power failure remain possible. Exact dependency internals are outside scope. `startup-crash.txt` catches only startup exceptions, not arbitrary crashes or process termination.

Monitor de-dup: checked, correct for distinct numeric monitor IDs (`KorRemoteBridge.js:53–60`): one entry per number, including zero when present. It cannot invent a missing “All”; first duplicate wins selection/title. Distinct valid numbers remain distinct. Numeric-prefix aliases could collapse under `parseInt`, but no legitimate second monitor with such IDs is established by the allowed files.

**One fix before deployment beyond KOR-204: make migration recoverable as a transaction, retaining a usable prior installation and restoring user registration when replacement fails.**
