# BRIEF 1 — "Client present" mode for the BD surface

Covers register items **1, 2, 18, 22, 23** plus one gate the register missed (see §3.5).

---

**IMPORTANT — do NOT run `dotnet build` or `dotnet test` after applying.** Verification happens on
the dev box on my side; your test runner hangs for 15+ minutes here and burns credits. Apply the
edits, grep your own diff if useful, then report. Stop there.

**Do NOT run any destructive git operation** — no `git clean`, no `git reset --hard`, no force push.
Untracked live work has been destroyed in this repo that way before.

---

## 1 · Why this exists

KOR is demoing this application to a prospective client that is an **architecture firm**. The BD
workspace renders KOR's competitive intelligence *about architecture firms* — including free-text
reads of how KOR intends to displace their incumbent structural engineer — on screen at load, with
no click. It also chips named architecture firms as `DIRECT COMPETITOR` and lists their executives
by name.

**None of this is a bug.** The data is correct and internally valuable. The problem is disclosure.
So the fix is not to delete anything: it is one switch that hides these surfaces when a client is in
the room, and leaves them untouched for internal use.

Two small copy defects ride along in §4 because they are on the same screens.

## 2 · The switch

Add a single app-wide flag. There is no feature-flag mechanism in this app today — this is the
first, so make it the smallest thing that works, not a framework.

- A static type in `Kor.Operations.App` that reads **`Demo.ClientPresent`** from `App.config`
  `appSettings` **once**, and exposes:
  - the `bool` itself, and
  - a `System.Windows.Visibility` that is `Collapsed` when the flag is on and `Visible` when it is
    off.
- Add the key to `Kor.Operations.App/App.config` defaulting to **`false`**, with a comment saying
  what turning it on does and that it is read at XAML parse time, so the app must be restarted.
- `Kor.Operations.App/CompositionModules/CompositionHelpers.cs` reads config through an
  `AppConfigKeys` constant class. Follow that convention for the key name rather than a bare string
  literal.

## 3 · The gates

Every one of these is a `Visibility` on an existing element. **Gate, do not remove** — internal
users still need all of it.

| # | file | what to gate | register item |
|---|---|---|---|
| 3.1 | `BusinessDevelopment/Workspace/DashboardView.xaml` | the `Read` column bound to `DisplacementRead` on the **Open Structural Seats** grid, and the `Capacity Read` column bound to `CapacityRead` on the **Competitor Watch** grid | 1 |
| 3.2 | `Opportunities/OrgDossierView.xaml` | the whole **`DISPLACEMENT BRIEF`** section, its header included | 2 |
| 3.3 | `Opportunities/CompetitionInfoView.xaml` | the **`Competes`** and **`Agent Profile (AI)`** columns | 2 |
| 3.4 | `BusinessDevelopment/Workspace/BdWorkspaceWindow.xaml` | the **`BD Scorecard`** nav button (`AttributionButton`) | 22 |
| 3.5 | `Opportunities/CompetitorProfileWindow.xaml` | the **`Vendor Profile (AI-researched)`** panel — the whole bordered block | *(not in the register — see below)* |

**3.5 is mine, not the audit's, and it matters.** Hiding the two grid columns in 3.3 does not stop
anyone double-clicking a name in the Awards **Winner** column, which opens
`CompetitorProfileWindow`. That window chips architecture firms with an overlap score — `NORR
ARCHITECTS & ENGINEERS LIMITED` and `ROBSON DESIGN BUILD LTD.` both render as **`DIRECT
COMPETITOR`** — and lists named executives at those firms underneath. Gating 3.3 without 3.5 leaves
the worse surface one double-click away.

## 4 · Two copy fixes — permanent, not gated

| # | file | change | register item |
|---|---|---|---|
| 4.1 | `Opportunities/CompetitionInfoSourcesWindow.xaml` | **Delete** the *"Coming next"* block — a literal roadmap panel announcing three unbuilt features, one click from Market History | 23 |
| 4.2 | `BusinessDevelopment/Workspace/PursuitBriefViewModel.cs` | Replace the two `"coming with the AI Crucible."` fallbacks with `"—"` | 18 |

4.2 renders in **every** Pursuit Brief, 100% of the time, and exports into the generated PDF.

## 5 · Traps you cannot infer from the code

1. **`DataGridColumn` is not in the visual tree.** A `{Binding}` on `DataGridColumn.Visibility`
   silently fails — no `DataContext` is inherited, and nothing errors. `{x:Static}` is resolved by
   the XAML parser and works. Use `{x:Static}` for **all** the gates in §3 so the file reads one way
   throughout, not two.
2. **3.5's panel already binds `Visibility`** to a `HasAgentProfile` property. Two `Visibility`
   setters cannot be stacked on one element. Decide how to combine them — do not quietly drop the
   existing condition.
3. The App's `RootNamespace` is `Kor.Operations` while the XAML classes are under
   `Kor.Operations.App.*`. Check the `xmlns` you add per file against that file's `x:Class`.
4. `App.config` here is the WPF app's, not the MCP service's. Do not touch
   `appsettings.Production.json` anywhere in this repo.

## 6 · What I need back

**Verify before you fix.** For each of the eight edits, confirm the thing described is actually
present before changing it, and report per item:

- **held** — found it, cite `file:line` as *you* found it, and say what you changed.
- **did not hold** — say why, cite what is there instead, and **do not invent a fix**. Four of this
  audit's "the capability is missing" findings were wrong, so a finding that does not hold is a
  useful result, not a failure.

Then one closing paragraph: anything you touched that is not on this list, and anything you saw on
these screens that worries you more than what I asked for.
