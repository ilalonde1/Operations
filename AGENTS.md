# AGENTS.md — build & test guidance for Kor.Operations

## Build / test scope — IMPORTANT (avoids ~10-min hangs and false failures)

**Do NOT build or test the full solution.** **Build/test ONLY the project(s) you changed, in Debug.**

### Corrected 2026-09-03 — timings, and the failures that are NOT yours

The "10–14 minutes over SMB" figure that used to be here is **stale**. The reference drawings are
mirrored locally now (`DrawingCache`). Per `CLAUDE.md`, measured 2026-08-29:

| command | scope | time |
|---|---|---|
| `dotnet test --filter "Speed!=Slow"` | every edit | ~22 s (678 tests) |
| `dotnet test` | geometry + publish | ~7 min (731 tests) |
| `…App/EngineeringTools.Tests` | the WPF side | ~15 s |

⚠ **The App suite has KNOWN failures that are not caused by your change.** As at 2026-09-03,
`dotnet test Kor.Operations.App/Kor.Transmittals.App.Tests/… --filter "Speed!=Slow"` gives
**556 passed / 3 failed**, and those three are pre-existing:
`EmptyCatchBlockTests.No_empty_catch_blocks_in_App`,
`EmailMetadataExtractorTests.ValidMsgFile_ReturnsExpectedCoreMetadata` (pwsh MSG),
`IntelExtractorTests.SqlEnrichmentTrackingStore_recordAttempt…` (needs a live DB; returns early
without one). **Diff the failure list against this baseline before concluding you broke something** —
that mistake has been made.

⚠ **`Directory.Build.props` at the repo root makes warnings errors everywhere** (commit 438e5bfa,
2026-09-02; the NuGet audit stays a warning). A new warning fails the build.
⛔ **Never re-add `UseSharedCompilation=false`** — it took an App edit from 14 s to 37.5 s.

### Corrected 2026-08-21 — the two warnings that used to live here were both wrong

This file previously said `EmailFiler/EmailFilerv2` was known-broken (*"missing `OfficeTools`
MSBuild target"*) and that `Kor.Transmittals.App.Tests` was *"stale test stubs"*. **Both build and
both run** `[RUN]`. Believing otherwise means skipping the projects that hold the email add-in and
the App's test suite. What is actually true:

| project | how to build/test it |
|---|---|
| `EmailFiler/EmailFilerv2` | **MSBuild, not `dotnet`** — it is .NET Framework VSTO and `dotnet build` cannot load it. Locate MSBuild with `vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild/**/Bin/MSBuild.exe` (it is under `Program Files`, not `Program Files (x86)`, on this box) and run it against `EmailFilerv2.csproj`. Builds clean. **Old-style csproj: a new `.cs` file needs an explicit `<Compile Include>` or it is silently not compiled.** |
| `Kor.Transmittals.App.Tests` (contains `Kor.Operations.App.Tests.csproj`) | Runs green **with `--filter`**. Unfiltered it can hang headless. `dotnet test "Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj" --filter "FullyQualifiedName~<Area>"` |
| `Kor.Operations.FileSync.Service.Tests` | New 2026-08-21. Plain `dotnet test`, ~1 ms, no I/O. Holds the scheduling-coverage gate. |

**A stale warning in this file is not free** — it is machine-read, and it steers work away from
real code. If you find one wrong, correct it here in the same session, with the command you ran.

Pure Quantity Takeoff logic (models, VolumeCalculator, DiffService, ReportGenerator) lives in
the standalone **`Kor.Operations.EngineeringTools.Core`** library — NOT in the WPF app — so its
tests compile in seconds with no WPF build:

```
dotnet test "Kor.Operations.EngineeringTools.Core.Tests/Kor.Operations.EngineeringTools.Core.Tests.csproj" -c Debug -nologo
```

Only the PDF adapter + UI (which need PdfToSafe/WPF) live in the App; those unavoidably
compile the app. For App-side tests:

```
dotnet test "Kor.Operations.App/EngineeringTools.Tests/Kor.Operations.EngineeringTools.Tests.csproj" -c Debug --filter "FullyQualifiedName~<Area>" -nologo
```

- Use **Debug** (Release optimization is slow on the WPF app). Reruns are incremental.
- The first build still compiles `Kor.Operations.App` (one-time cost). It will not pull in
  EmailFiler/Transmittals — which **build fine**, see the table above; the word "broken" that used
  to sit here contradicted this file's own correction.
- Confirm change scope with `git status --short` — it should list only the files your task
  created/edited. That is the primary zero-regression check: code you never opened cannot
  regress.

## Active feature work

- **Quantity Takeoff & Issue Delta** — see `docs/architecture/Kor.Operations.QuantityTakeoff.plan.md`.
  New code lives in `Kor.Operations.App/EngineeringTools/QuantityTakeoff/` and is built/tested
  in isolation per the plan. AI is banned from the measurement path (deterministic math only).

- **Working the August 2026 audit** — `docs/audit-2026-08/START-HERE.md`, then
  `04-TODO-REGISTER.md`, which carries a `status` on all 182 items and is the single arbiter of what
  is done. `WORKLOG.md` holds the evidence behind each status change. **A fix is not `verified`
  until its gate has been run against the pre-fix code and seen to fail there** — one gate has
  already been recorded as *not* a gate for failing that check.

## FileSync scheduling

Cadences live in `Kor.Operations.FileSync.Service/Scheduling/FileSyncSchedulingCatalog.cs`, not
inline in the installer. Every `IJobRunner` must appear there **or** carry a written exemption in the
same file — `SchedulingCoverageTests` fails otherwise, which is how a job that is implemented,
registered in DI and never scheduled gets caught. `KorMapSync` was exactly that for months.
`WatcherSyncRunner` is exempt because `WatcherHostedService` drives it; `NoOpJobRunner` is a stub.

## Talking to the databases (added 2026-09-03 — this cost real time to rediscover)

A developer Windows account has **no rights** on the app databases. `HAS_DBACCESS` returns 0 for the
domain user on `KorOpportunitiesDb`, `KorEmailIndex`, `KorStandards`, `KorTransmittals` and the rest;
they are read by the service account. `sqlcmd -E` failing is expected, not a misconfiguration.

The app connects with **SQL auth**, and the connection string lives in a machine environment variable
on KOR-APP01 named `KOR_OPPORTUNITIES_OPPORTUNITIESDB`, readable over the remote registry via
`[Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine','KOR-APP01')` and the
`Session Manager/Environment` subkey. Connect with `System.Data.SqlClient` using that string.
**Never echo the string or the password into output, a file, or a report.**

Column names that are easy to guess wrong: `CanonicalOrg.DisplayName` (not `Name`),
`IntelPerson.DisplayName` / `.Email`, `OrgAlias.RawName` (not `Alias`),
`IntelNarrative.ParagraphText`, `OpportunityAwards.AwardedAtUtc` / `.AwardedToOrganization`,
`JobRuns.Success` (not `Status`), `MajorProjectsInventory.FirstSeenAtUtc` (there is no `CreatedAtUtc`).

⚠ `CanonicalOrg.NormalizedName` is a **computed column** — an INSERT that sets it fails.
`FuzzyNormalizedName` is *not* computed and must be set explicitly, or the row gets an empty fuzzy
key and can group with unrelated orgs.

## Ingestion sources — where the config actually lives

Per-source settings are key/value rows in **`opportunities.OpportunitySourceMappings`**, NOT in
`OpportunitySources.ConfigJson` (NULL for the JSON providers). Keys look like `json.pageSize`,
`json.maxPagesPerRun`, `json.maxRowsPerRun`, `json.sort`.

⚠ **Ingestion runs have a cancellation timeout.** Many small pages hit it: 120 pages × 1,000 rows ×
1.5 s pacing was cancelled at ~9 minutes, while the same corpus in 8 pages of 10,000 finished
comfortably. **Few and large beats many and small.**

⚠ **An IN-FLIGHT run is indistinguishable from a failed one.** `IngestionRuns.Success` stays 0 until
the row is finalised. Filter `EndedAtUtc IS NOT NULL` before judging a run — a 9-minute run read at
7 minutes was wrongly called failed and its sibling cancelled; it had inserted 295 rows.

**Health, not vibes:** `dotnet run --project tools/BdIntegrityCheck -- --db <connstr>` runs the
invariant suite — identity conflation, source freshness, dangling org references. Structural
violations set a non-zero exit; the identity and source checks are WARN worklists by design.

## Publish and deploy

`publish-opportunities.ps1` **publishes only** — it runs its own test gate and drops a timestamped
build under `_Publish`. Deploying is the separate step: stop the service, robocopy with
`/XF appsettings.Production.json`, start, then verify `FileVersion`. **Ian runs deploys, and Ian runs
Codex**, unless he says otherwise in the session.
