# AGENTS.md — build & test guidance for Kor.Operations

## Build / test scope — IMPORTANT (avoids ~10-min hangs and false failures)

**Do NOT build or test the full solution.** It is slow, and ~20 tests rebuild both reference
buildings from real drawings over SMB (10–14 minutes). **Build/test ONLY the project(s) you
changed, in Debug.**

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
- The first build still compiles `Kor.Operations.App` (one-time cost); it will NOT touch the
  broken EmailFiler/Transmittals projects.
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
