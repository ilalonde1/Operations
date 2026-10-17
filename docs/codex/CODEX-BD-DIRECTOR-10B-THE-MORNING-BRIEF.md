# BD Director 10b — the morning brief: BD, COO and IT in one hour

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-10B-THE-MORNING-BRIEF-RESPONSE.md`, in the format set
out under Response (do not open earlier responses).

## Why this exists

Ian runs BD, operations and IT at KOR Structural (a sixty-person structural engineering firm). He wants one
thing every morning: what to do today, in all three roles, reasoned like a senior MBA adviser and drawn from
the firm's own data and the 49-book library. Today only the BD hour exists. It is written in a local Claude
Code session, imported with `BdKnowledgeIngest hour import`, emailed at 06:05 by `DirectorHourEmailJob` and
shown on the Director's Today screen. This brief makes that hour three sections (BD, COO, IT), fed by a
composer that gathers every fact so the morning session only writes judgement.

**Rule 14, not negotiable: no Anthropic API anywhere.** The session that writes the brief is Claude Code run
headless on KOR-APP01 from a scheduled task. Nothing in code calls a model or any outside service.

## The decisions, so you build the one thing

1. **A new kind, `Morning`,** added to `BdRhythmValues.HourKinds` (`BdRhythmStore.cs:53`, today BD and
   Operations). A Morning hour carries **sections**: `DirectorHourContent` gains optional
   `Sections` (list of `HourSection(Role, Title, Plan, Moves, Decisions)`, Role in BD|COO|IT). A Morning hour
   has 2 or 3 sections and no top-level moves; each section 1–4 moves. Move ranks are unique across the whole
   hour (1..n), so the existing move-result table keys still work. BD and Operations hours are unchanged.
2. **Cards per section role:** a BD-section move may cite only accepted BD cards, a COO-section move only
   accepted Operations cards, an IT-section move none (no IT doctrine yet). Extend `DirectorHourFile.ValidateCards`.
3. **The composer is a new console tool, `tools/MorningBrief`** (net8.0-windows, because
   `Kor.Operations.Business` is). One verb: `compose --date <yyyy-MM-dd> --to <folder>`, writing
   `morning-<date>.md`. It only reads. It gathers:
   - **BD:** live pursuits by owner and region, pursuits with no touch in 14+ days (top 10 by value), open and
     late actions by owner, the last hour's move results, flagged doctrine cards (`ListRecordsAsync`), and the
     accepted BD cards as `#id title` so moves can cite them.
   - **COO (Deltek):** AR aging buckets and the 10 largest overdue invoices (`ArFinancialsService`); unbilled
     WIP total and top 10 projects (`WipFinancialsService`); firm utilization for the last 4 weeks
     (`FirmAnalyticsService.LoadFirmUtilizationSync`) and people under 50% or over 110% last week
     (`EmployeeAnalyticsService.LoadEmployeeWeeklyUtilizationSync`); the 10 projects most over budget
     (the logic `AtRiskProjectsTool` already uses over `ProjectAnalyticsService.LoadProjectRowsSync`); and the
     accepted Operations cards as `#id title`. Options are filled exactly as `Kor.Operations.Mcp/Program.cs:40–87`
     fills `DeltekOdbcOptions` and `FinancialsOptions` (config sections `DeltekOdbc`, `Financials`; the machine
     has the `Deltek` DSN).
   - **IT (NetworkOps database, read directly, no token):** open findings grouped by severity and device, and
     changes in the last 24 hours. Use the SQL behind `NetworkOpsStore.OpenFindingsOfRulesAsync` /
     `OpenFindingsAsync` (`NetworkOpsStore.cs:221–275`) and `ChangesSinceAsync`
     (`NetworkOpsStore.CommandCenter.cs:97`), copied into the tool, not by referencing the service project.
     Connection string from env var `KOR_NETWORKOPS_WATCHDB`, as `WatchdogVerb.cs` does.
   Each section of the prompt says what it could not read (a source down is a fact, not a crash) and states
   counts as X of Y.
4. **The prompt's instructions** (written by the composer, in the voice of `BdPromptComposer`): you are Ian's
   chief of staff with an MBA's judgement; write a Morning hour JSON with a BD, a COO and an IT section, at
   most 4 moves each, ranked by consequence today; every move has why-now facts from this prompt, cites the
   accepted cards that apply by id, may quote a book (search with `BdKnowledgeIngest claims search`); drafts
   are addressed to Ian to send; nothing goes to anyone else; then run `BdKnowledgeIngest hour import <file>
   --by "<session>"` and fix and re-run if it refuses. Never call an API. Never write anything else.
5. **Delivery:** `DirectorHourEmailJob` sends the Morning hour for today when one exists, else the BD hour as
   now. `DirectorHourEmail.Render` renders sections under role headings (BD, COO, IT), each with its plan,
   moves and decisions. The subject says "Your morning" for a Morning hour.
6. **Today screen:** when today's hour is Morning, Today shows the three sections under headings, each move
   with the existing Done / Not today / Not doing buttons and the "From the doctrine" line. The strip counts
   moves across sections.
7. **The runner, `tools/run-morning.ps1`,** for a scheduled task on KOR-APP01: compose to
   `C:\KorMorning\<date>\`; run `claude -p "Read <prompt> and follow it." --dangerously-skip-permissions`
   in the folder holding the published CLI; then confirm a Morning hour for today exists (query through the CLI
   or a read in the tool, not by trusting the session's exit code); write a log line; exit non-zero on any
   failure. It installs nothing and stores no secret.

## Pieces

1. **Data — the Morning kind and sections:** `HourKinds`, `HourSection`, `DirectorHourContent.Sections`,
   parse and validation rules (decision 1), card validation per section role (decision 2), `TotalMinutes`
   across sections, `DirectorHourEmail.Render` with sections (decision 5).
2. **Worker — the email job** sends Morning when present (decision 5); unchanged otherwise.
3. **Tool — `tools/MorningBrief`:** csproj (references `Kor.Operations.Business`, `Kor.Opportunities.Data`,
   `Microsoft.Data.SqlClient`), `Program.cs` with `compose`, a `MorningFacts` gatherer per role, and a pure
   `MorningPromptComposer.Compose(facts, date)` so the prompt is testable without a database.
4. **CLI — `hour import`** accepts kind Morning (the role check near `Program.cs:644`).
5. **App — Today** renders a Morning hour's sections (decision 6).
6. **Runner — `tools/run-morning.ps1`** (decision 7), with a header saying what it does and does not do.
7. **App — Doctrine for both roles:** `BdDoctrineViewModel.Role` is the constant `"BD"` today, so Operations
   cards (now being written) never show. Add a BD | Operations selector on the Doctrine tab; it reloads cards,
   records and runs for that role; Accept, Reject, Accept all waiting, Retire and Synthesize act on the selected
   role (Synthesize passes it to `PrepareSynthesisAsync`). Default BD.
8. **Gates (authored, not run):**
   (1) a Morning hour with three sections parses; one with top-level moves, a section with 0 or 5 moves,
   duplicate ranks across sections, or an unknown role is refused with a message naming it; (2) BD and
   Operations hours without sections still parse unchanged; (3) card validation by section role; (4) the email
   renders three role headings with their moves and the subject reads "Your morning"; the job prefers Morning
   over BD for the same date; (5) `MorningPromptComposer` with fixture facts contains each role's heading, the
   card lists by id, the "nothing goes to anyone" rule, the import command, and a "could not read" line when a
   role's facts are missing; (6) no API marker or model call in the tool or runner; (7) render
   `BdScreensRenderTests`: Today with a three-section Morning hour (`director-today-morning.png`); (8) the
   Doctrine role selector loads Operations cards and Accept all acts only on the selected role's waiting cards.

## Claude's run after apply (not yours)

Build all touched projects; run the gates; render and read the PNG; run `MorningBrief compose` on KOR-1001
against the live databases and read the prompt; write one Morning hour in session and import it; install the
App; publish the tool and CLI to APP01; with Ian, install Claude Code there and sign in; register the task.

## Read bound

About 135 KB, measured. **Read the line ranges given, not whole files**; search (`rg -n`) and read 40 lines
around a hit for anything else; never open a file over 30 KB whole. Lines as of the current HEAD; if a range has
drifted, search for the named symbol.

| File | Lines | For |
|---|---|---|
| `Kor.Opportunities.Data/Crm/DirectorHour.cs` | all (13 KB) | content records, parse, validation, email render |
| `Kor.Opportunities.Data/Crm/BdRhythmStore.cs` | 40–100, 195–235 | `HourKinds`, hour import and get |
| `Kor.Opportunities.Worker/Services/Reporting/DirectorHourEmailJob.cs` | all (6 KB) | the job |
| `Kor.Operations.Mcp/Program.cs` | 35–90 | filling Deltek options |
| `Kor.Operations.Mcp/Brief/BriefPrompts.cs` | all (5 KB) | the six COO topics, as a checklist |
| `Kor.Operations.Mcp/Tools/ArTool.cs`, `AtRiskProjectsTool.cs` | all (6 + 5 KB) | AR and at-risk logic to reuse |
| `Kor.Operations.Business/ArFinancialsService.cs` | 1–80 | ctor, `LoadAsync` |
| `Kor.Operations.Business/WipFinancialsService.cs` | 1–60 | ctor, `LoadAsync` |
| `Kor.Operations.Business/Analytics/FirmAnalyticsService.cs` | 1–60 | firm utilization |
| `Kor.Operations.NetworkOps.Cli/WatchdogVerb.cs` | all (8 KB) | direct database read, env var |
| `Kor.Operations.NetworkOps.Service/Store/NetworkOpsStore.cs` | 215–275 | open findings SQL |
| `Kor.Operations.NetworkOps.Service/Store/NetworkOpsStore.CommandCenter.cs` | 90–170 | changes SQL |
| `Kor.Opportunities.Data/Crm/BdPromptComposer.cs` | 1–120 | prompt voice and rules |
| `Kor.Operations.App/BusinessDevelopment/Director/BdRhythmViewModel.Screens.cs` | 1–140 | Today's moves |
| `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml` | 105–260, 379–490 | the Today and Doctrine tabs only |
| `Kor.Operations.App/BusinessDevelopment/Director/BdDoctrineViewModel.cs` | all (12 KB) | the Doctrine screen's role |
| `tools/BdKnowledgeIngest/Program.cs` | 255–300, and search `HourKinds` | hour import |
| `tools/BdKnowledgeIngest/BdKnowledgeIngest.csproj` | all | csproj pattern for a tool |

Do not open the live databases, the share, Deltek or APP01. App projects with `UseWPF` have no implicit
`System.IO`. The API guard (`tools/claude_api_guard.py`) refuses the `/ask` service's method name in Director
code; name local-Claude methods plainly.

## Files

Creates: `tools/MorningBrief/` (csproj, `Program.cs`, `MorningFacts.cs`, `MorningPromptComposer.cs`),
`tools/run-morning.ps1`, a test class `MorningBriefTests` in the App test project, the RESPONSE.

Edits: `DirectorHour.cs`, `BdRhythmStore.cs` (`HourKinds`), `DirectorHourEmailJob.cs`,
`tools/BdKnowledgeIngest/Program.cs`, `BdRhythmViewModel.Screens.cs`, `BdDirectorWindow.xaml` (+ `.cs` if
needed), `BdScreensRenderTests.cs`, the solution file if tools are listed there.

## Response

Plain markdown, one section per piece: what changed, the gates authored and not executed, what was not done and
why, and anything in the existing code that contradicted this brief. Do not build, test, run tools, open a
database or commit. `git diff --check` clean.
