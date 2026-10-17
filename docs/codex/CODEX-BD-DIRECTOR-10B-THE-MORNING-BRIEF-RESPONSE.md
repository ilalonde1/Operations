# BD Director 10b response

## 1. Data: Morning hours and sections

Added `Morning`, optional `DirectorHourContent.Sections`, and `HourSection`. Morning validation requires two or three distinct BD/COO/IT sections, one to four moves per section, no top-level moves, and consecutive ranks unique across the whole hour. Minutes and card validation now traverse sections. BD citations require accepted BD cards, COO citations accepted Operations cards, and IT citations are refused. Legacy BD and Operations hours retain their existing move format and limits. Email rendering includes each role's plan, moves and decisions; Morning subjects start with “Your morning”.

The existing database contradicted the brief: migration 360 restricts kinds to BD/Operations, and migration 361 restricts move ranks to 1–7. Added **unapplied** migration `366_MorningHour.sql` to allow Morning and ranks 1–12, and changed the store's matching rank guard. Apply that migration before importing Morning hours or recording ranks above seven. It changes constraints, not existing hour contents.

Doctrine history also assumed all citations were in top-level moves. Updated `ListRecordsAsync` and `DoctrineTrackRecord` to include Morning hours and their section moves, preserving the existing hour/rank result keys.

Authored gates cover valid two/three-section and twelve-move hours; top-level moves, empty/oversized sections, duplicate/gapped ranks, unknown/duplicate roles and null sections; legacy hours; card roles/statuses; total minutes; email sections; schema limits; and doctrine records. None was executed.

## 2. Worker: delivery preference

`DirectorHourEmailJob` now selects today's Morning hour first, falling back to today's BD hour only when Morning is absent. Existing emailed stamps and Monday draft behavior remain in place. An already emailed Morning hour does not cause a second BD delivery.

Authored a mocked-store gate for preference, fallback, the Morning subject and suppression after sending. No worker or mail path was run.

## 3. Tool: MorningBrief

Added the net8.0-windows console project, `Program.cs`, `MorningFacts.cs` and pure `MorningPromptComposer.Compose(facts, date)`. Added the project to the existing solution and referenced it from the App test project. The only tool verb is `compose --date <yyyy-MM-dd> --to <folder>`.

Prior-art searches covered `tools/`, the morning brief filename in `docs/codex`, `BdPromptComposer`, the existing CLI/hour delivery code, Deltek services and MCP wrappers, and the NetworkOps store. The implementation reuses those services, options and read patterns; it references no NetworkOps service project and adds no model client.

BD facts include all live pursuits grouped by owner/region, the top ten with no touch for at least fourteen days, open/late actions by owner, the most recent earlier date's hour results, flagged doctrine records and accepted BD card ids/titles. Pursuit warmth uses the existing maximum of logged activity, buyer email touch and pursuit opening time. Unassigned pursuits are included. “Value” is explicitly labeled proposed fee; unknown fees sort last and no currency is inferred.

COO facts include CAD-equivalent AR aging and the ten largest overdue invoices, WIP totals and top projects, four completed weeks of firm utilization, last week's employee outliers, the canonical over-budget project list, and accepted Operations cards. Deltek and Financials option assignments match the cited MCP configuration. Configuration is read beside the published executable and from environment variables; no settings or credentials were created here.

Two service mismatches needed small source changes. `FirmAnalyticsService` originally exposed annual/all-time totals only, so it now has a parameterized date-window overload using the same billable-hours definition. `WipFinancialsService` could swallow failed reads and return zeros, so the composer opts into a strict overload; existing callers retain their old behavior. An absent WIP signal is also reported as unavailable. Weeks start Sunday to match employee data. Employee utilization is billable/recorded hours, as in the existing employee tool; it cannot establish workload against contracted capacity or identify missing timesheets. The prompt states that limitation.

IT uses the machine environment variable `KOR_NETWORKOPS_WATCHDB`, with process fallback, and direct SELECTs adapted from the cited store methods. It includes open findings grouped by severity/device, findings opened or cleared in the last 24 hours, and requested actions. Changes carry their full window-count denominator when capped at 1,000. Retired devices are excluded. Opening and clearing the same finding within the window remain visible in one row with both timestamps.

Every source reports coverage as X of Y, or 0 of unknown when unreadable. Read failures are isolated and report exception types without provider messages or credentials. The prompt carries the three-role JSON contract, consequence ranking, facts, role-specific cards, book lookup, drafts for Ian, no outgoing messages, local-session rules, and the import/fix/retry instruction.

Authored fixture gates cover prompt content, missing sources, coverage, the stale-pursuit cutoff/order, employee week/outlier boundaries and prohibited remote-client markers. No composer invocation, database read, Deltek connection, package restore or build was performed.

## 4. CLI: import and verification

Morning import loads both BD and Operations card lists before invoking the shared citation gate, and reports the move count across sections. The brief's cited role check is actually for doctrine/synthesis; those roles remain BD/Operations. Morning is an hour kind, not a doctrine role.

Added read-only `hour verify <date> --kind Morning --after <ISO timestamp>`. It checks the stored hour's parsed content, kind, date and import timestamp, returning nonzero for missing, invalid or stale content. Both argument parsing and the separate per-verb option allowlist were updated. Authored source wiring gates and adjusted existing import-gate assertions. No import or verification was executed.

## 5. App: Today

Today now prefers Morning for each date in its existing seven-day lookup. Role headings contain each section's title, plan, moves and decisions. Section moves share the existing move view instances, so Done / Not today / Not doing, folding, citations and the progress strip operate across the entire hour. Citation cards are loaded for both doctrine roles, independent of the selected Doctrine tab role; a COO citation opens its Operations card.

Authored a view-model gate for aggregate progress, Operations citations and rank-specific result recording. Extended `BdScreensRenderTests` to produce `director-today-morning.png` with three sections and assert headings, decisions, buttons and citations. The PNG was not generated or inspected because this brief forbids running the render gate.

## 6. Runner

Added `tools/run-morning.ps1`. It takes the published CLI folder and composer executable as parameters, composes under `C:\KorMorning\<date>`, runs the installed Claude Code command from the CLI folder, then verifies a Morning import newer than the session start. It checks each exit code, writes a timestamped stage/success/failure log, and exits nonzero on failure. The CLI folder is placed on the child process path so the prompt's published commands resolve.

The header states its prerequisites and limits. It installs nothing, registers no scheduled task, and persists neither credentials nor session output. The runner was authored only; no session, installation, sign-in, publish or task registration occurred. Its source gate checks that independent database verification follows the session and includes the freshness cutoff.

## 7. App: Doctrine roles

Added the BD/Operations selector, defaulting to BD. Role changes reload cards, records and runs. Run filtering now happens in SQL before the result limit, avoiding a busy BD history hiding Operations runs. Accept, Reject, Accept all waiting, Retire and Synthesize use the selected role; stale cross-role card actions are refused. A slower earlier load cannot overwrite a newer role selection. Synthesis uses the selected role's cards/records and run guard. Ordinary BD questions retain their BD context; card-amendment questions use the card's role.

Authored gates cover Operations loading, rejected cards being excluded from bulk acceptance, keeping BD waiting cards untouched, retirement, Operations synthesis and a delayed BD refresh. The render fixture also selects Operations and checks its displayed cards. Existing doctrine mocks were updated for role-scoped reads. None of these gates was executed.

## 8. Gates and handoff

New behavioral/source gates live in `MorningBriefTests`; the existing render, doctrine and rhythm regression classes were updated where contracts changed. They are authored, not passing claims. No pre-fix gate run was performed, and no audit item was marked verified.

Only source editing, bounded source searches/reads and Git diff/status checks were performed. `git diff --check` is clean. No build, test, render, runtime tool, live database, share, Deltek, APP01 access, migration application, publish, deployment or commit occurred. Earlier response files were not opened. Pre-existing unrelated workspace changes were preserved.

The brief's after-apply validation and rollout remain for Claude/Ian. That handoff now also includes applying migration 366 before a live Morning import, publishing the updated Business dependencies with the tool, and supplying the runner's published paths and existing configuration under the signed-in task account.
