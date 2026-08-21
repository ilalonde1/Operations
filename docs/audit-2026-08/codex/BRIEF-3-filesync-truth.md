# BRIEF 3 — FileSync: a job that never runs, a log that never shows, a mode that lies

Covers register items **10, 11, 12**. Each fix ships with the test that locks it.

**Do NOT run `dotnet build`/`dotnet test`.** Write code AND tests, report, stop. I run them here —
and I specifically check each new test **fails against the old behaviour**. A test that passes
either way is worse than no test.

**Do NOT run any destructive git operation.**

## 1 · KorMapSync is never scheduled — item 10

`Scheduling/QuartzInstaller.cs` registers 5 jobs. `KorMapSyncRunner` is implemented and in DI but has
no `JobKey`, no `AddJob`, no trigger — it has never fired. Register it, mirroring the 5-job pattern
including `WithMisfireHandlingInstructionFireAndProceed`.

**TRAP — do not "fix" the other two.** 8 `IJobRunner` implementations exist; two are deliberately not
in Quartz: `WatcherSyncRunner` (driven by `WatcherHostedService`, `Program.cs:91` — event-driven, not
cron) and `NoOpJobRunner` (stub). KorMapSync is the only genuine omission.

## 2 · The log viewer shows nothing on a healthy service — item 11

`Kor.Operations.App/FileSync/FileSyncLogTailer.cs:68` reads `fi.Length`. `FileInfo` caches metadata;
on a file being appended by another process it reads 0 while the file has content. Measured:
`FileInfo.Length` 0 vs `Stream.Length` 43,165, same file, same instant. The class already opens with
`FileShare.ReadWrite|Delete` — take the length from the open stream.

## 3 · The panel reports a mode nothing consults — item 12

`FileSyncCommandCenterWindow.xaml:153` binds `GlobalMode`, written at
`ControlPlane/SqlControlPlaneStore.cs:54`. It reads `Shadow` while all seven jobs run `Live` and move
client files. The real authority is per-job `config.Mode` (`ConcreteTestReportsRunner.cs:84`). There is
no global mode governing anything and **no `KOR_FILESYNC_MODE` anywhere in the solution — grepped.**

Do not invent a global mode to make the label true. Derive the column from the per-job modes the
reader already has, and rename the header to match.

## 4 · The tests — the point of the brief

- **4.1** New `Kor.Operations.FileSync.Service.Tests`, conventions per `Kor.Opportunities.Data.Tests`.
  Service types are `internal` → needs `InternalsVisibleTo`.
- **4.2 Scheduling coverage** — every `IJobRunner` reachable by a scheduler or an **explicitly declared
  exemption with its reason** (`WatcherSyncRunner`, `NoOpJobRunner`). Adding a 9th runner without
  scheduling it must fail. This is the ratchet; the KorMapSync fix alone is just an instance.
- **4.3 Log tailer** — append to a file held open with Serilog's share flags; assert the tailer sees
  the lines. Must fail on `FileInfo.Length`. `FileSyncLogTailer` is public and
  `Kor.Operations.App.Tests` already references the App — put it there. Pure file I/O, headless.
- **4.4 Mode derivation** — all-Live reads Live, a mix reads mixed. A constant must not satisfy it.

## 5 · What I need back

Per item: **held** (cite `file:line` as you found it + what changed) or **did not hold** (say why,
cite what is there, invent nothing). Then:

- the KorMapSync cadence and why;
- how the exemptions are expressed, and what happens when someone adds a 9th runner;
- **for each test, the one line of production code that, if reverted, makes it fail.** That is what I check.
