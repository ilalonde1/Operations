# AGENTS.md — build & test guidance for Kor.Operations

## Build / test scope — IMPORTANT (avoids ~10-min hangs and false failures)

**Do NOT build or test the full solution.** Several pre-existing, unrelated projects are
known-broken and will fail a solution-wide build/test. They are NOT caused by current work —
ignore them:

- `EmailFiler/EmailFilerv2` — missing `OfficeTools` MSBuild target (VSTO add-in).
- `Kor.Transmittals.App.Tests` — stale test stubs.

**Build/test ONLY the project(s) you changed, in Debug.**

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
