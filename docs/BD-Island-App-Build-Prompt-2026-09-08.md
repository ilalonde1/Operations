# Seed prompt — build the Island view and the project team into the app

**Paste everything below the line into a fresh Claude Code session in `C:\VIsual Studio Projects\Operations`.**

Filed to the repo's existing convention for session prompts (`docs/BD-Audit-Prompt-2026-06-19.md`,
`docs/BD-Fix-Prompt-2026-06-19.md`, `docs/BD-Duplicate-Sweep-Prompt-2026-09-04.md`).
Every figure below was measured on 2026-09-08 against `KOR-APP01\SQLEXPRESS` and `DELTEK_VP`.

---

## The job

Rory Beirne, Principal Vancouver Island, asked for two things that do not exist in the app:

1. **A focused Island view** — the live application file for his market, with the single-family
   and tenant-improvement noise gone.
2. **The architect on every past job** — "It would be good to have reference jobs that we did with
   a firm, when calling them, even old jobs."

Both are data that already exists and nothing reads. This session builds the reading.

⚠ **Read `docs/island-pipeline/NOTE-architect-role-and-won-definition-2026-09-08.md` first.** It is
the characterisation; this prompt is the build.

## ⛔ The correctness rule that governs everything here

**A Deltek project record is NOT a won job.** A record is created when we decide to *chase*
something. Of **10,018** top-level projects (`WBS2 = ' ' AND WBS3 = ' '`), only **2,411 have ever
been billed**. This single mistake put four false claims in front of the Principal, including
telling him we had won a job we had lost.

| Field | Values, measured 2026-09-08 |
|---|---|
| **`PR.Stage`** ⭐ | `InPursuit` 179 · `LOST` 86 · `DNP` 8 · `~WDEF~` 9,345 (2,411 billed) · null 350 |
| `PR.ChargeType` | `P` promotional 350 · `R` regular 9,345 · `H` overhead |

**`ChargeType = 'R'` admits 7,255 never-billed records.** Billed comes from `PRSummarySub`
(`Billed`, `BilledFee`, `LaborCost`) joined on `WBS1`.
⚠ `LedgerAR.Amount` nets to zero even on real jobs. Do not use it as the billed test.

**There are already two definitions of "won" in this solution and they disagree:**

- `Kor.Operations.Data/Deltek/DeltekKorWonProjectAccessor.cs` → `WHERE pr.ChargeType = 'R'` — wrong
- `Kor.Operations.App/Crm/DeltekClientContextService.cs` → joins **AR** — right
- Neither uses `Stage`.

**Task zero is to collapse those into one accessor** before building anything on top. Proposed
definition, to confirm against a handful of known jobs before adopting:

> WON = `ChargeType <> 'P'` AND `Stage NOT IN ('InPursuit','LOST','DNP')`
> AND (`Billed > 0` OR a contract `Fee` is set)

Verification cases: `20330-01` Gracewood at Fairwinds Nanoose must come back **NOT won**
(`InPursuit`, fee 0, billed 0 — Rory confirmed we lost it). `31037-01` West Wave Parksville must
come back **won** (fee $150,000, billed $151,701).

## Part 1 — the architect on every job

`ClendorProjectAssoc` links a project (`WBS1`) to a firm (`ClientID`) with a **`Role`** column.
Nothing in this solution reads it — searched `ClendorProjectAssoc`, `PRClientAssoc`,
`PRContactAssoc`, `PRConsultant` across `*.cs`, `*.sql`, `*.ps1`, `*.py`; the one hit,
`DeltekClientContextService.cs`, reads the AR ledger instead. `PRConsultant` has **0 rows**.

| Role | Rows | Distinct projects |
|---|---|---|
| (blank) | 5,992 | 1,493 |
| (null) | 4,067 | 2,088 |
| `sysOwner` | 3,627 | 3,619 |
| **`Architect`** | **745** | **743** |
| `Billing` | 423 | 361 |
| Other · Electrical · Geotech · Mech · Contractor · Envelope · Landscape | 98 | — |

**743 of 10,018 projects carry a named architect.** By project count: Arcadis IBI 81 · DIALOG 68 ·
Chris Dikeakos 61 · GBL 48 · Pacific Coast 44 · Musson Cattell Mackey 29 · Rositch Hemphill 28 ·
Yamamoto 26 · Ramsay Worden 19 · Keystone 17 · NSDA 15 · CEI 14 · Chandler 14 · Integra 13 ·
JWDA 11 · Christopher Bozyk 11 · VIA 11 · Perkins&Will 9.

⭐ **Arcadis reads as 2 in the BD system and 81 here**, because `CanonicalOrg.KorProjectsCount`
counts who *paid* and this counts who we *worked with*. That gap is the whole point of the feature.

**Build:** on the org/client card, beside the existing project list, show *"projects we worked on
together"* from `Role = 'Architect'` — WBS1, name, year, and whether it was billed. Wire the same
for the other roles (`Contractor`, `Geotech`, `Mech`, `Electrical`, `Envelope`, `Landscape`), which
are thin now but cost nothing extra once the join exists.

⚠ **State the coverage in the UI, not just the code.** 743 of 10,018 is **7%**. A card that shows
"3 projects together" when the truth is "3 recorded, of an unknown larger number" will mislead
someone into a phone call. Show the denominator or a "recorded on N of M" line.

## Part 2 — the Island view

`opportunities.vIslandApplications` (migration 316) is the single definition. **Do not re-implement
the predicate** — that is exactly what went wrong: it was written twice in one session and the two
versions disagreed, 1,130 rows against 1,156, and the wrong one shipped.

Columns: `OpportunityId, SourceName, Municipality, Title, Applicant, Address, EstimatedValue,
FiledDate, IsDated, Scope, IsBigTicket`.

As of 2026-09-08: **3,020 applications, 1,156 big-ticket, 718 dated, 270 in the last twelve months.**

| Municipality | Total | Big ticket | Dated |
|---|---|---|---|
| Nanaimo | 878 | 388 | **0** |
| Langford | 359 | 193 | 193 |
| Saanich | 968 | 191 | 191 |
| Victoria | 121 | 92 | 92 |
| Courtenay | 410 | 85 | 85 |
| Qualicum Beach | 112 | 78 | 78 |
| Parksville | 53 | 44 | 44 |
| Campbell River | 36 | 31 | **0** |
| Colwood | 30 | 27 | 27 |
| Comox (2 layers) | 43 | 19 | **0** |
| RDN | 10 | 8 | 8 |

⚠ **438 of the 1,156 have no date at all.** Any "last N months" filter must say so or it lies by a
third. Only Saanich, Victoria, Campbell River and Parksville name an applicant.

**Build:** an Island screen filtered to these twelve sources, defaulting to `IsBigTicket = 1`,
grouped by municipality, sortable by date where dated, showing applicant where published, and
linking to the org card when the applicant resolves to a `CanonicalOrg`.

## Hard constraints

- **Prototype in PowerShell if you must; ship C#.** `feedback_powershell_is_prototype_only_port_to_csharp`
  is binding.
- **Deltek access is ODBC via the `Deltek` DSN in app code**, four-part naming
  (`[DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.X`) only for SSMS convenience. `Clendor` is the
  client/vendor lookup — there is no `ClientInfo` table.
- ⚠ **Do not `OUTER APPLY` across the linked server per row** — one round trip each, and a
  10,000-row pull will not finish in two minutes. Pull `PR` and `PRSummarySub` as two flat sets and
  join locally.
- **Migrations for this repo go in `Kor.Opportunities.Data/Schema/`**; the last applied is **316**.
  (`KOR.Drafter/db/` is the other project's folder.)
- **Check `CompositionModules/` and DI first** before adding a service — `feedback_architecture_first`.
- **Run `--filter` while iterating; the full suite only before a publish** (~7 minutes).
- **Claude runs publishes; Ian runs server deploys.**
- **Ian runs Codex.** If a Codex pass is wanted, write `docs/codex/CODEX-<TOPIC>.md` and stop.

## Definition of done

- One accessor, one definition of won, used by both current call sites, and `20330-01` comes back
  NOT won while `31037-01` comes back won.
- An org card shows the projects we worked on with that firm, with the coverage denominator visible.
- An Island screen reads `vIslandApplications` and defaults to big-ticket, with the undated caveat
  on screen rather than in a comment.
- A test that fails if any of it regresses. Per repo rule 11 the check must state what it compares,
  what it does not, and one same-class fault it would miss.
