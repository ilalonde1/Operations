# Note for later — the architect role, and two definitions of "won"

Raised by Ian 2026-09-08 while working Rory's second round of feedback. Parked, not built.

## 1. The architect on every job IS in Deltek, and nothing reads it

`ClendorProjectAssoc` links a project (`WBS1`) to a firm (`ClientID`) with a **`Role`** column.
Role values, counted 2026-09-08:

| Role | Rows | Distinct projects |
|---|---|---|
| (blank) | 5,992 | 1,493 |
| (null) | 4,067 | 2,088 |
| `sysOwner` | 3,627 | 3,619 |
| **`Architect`** | **745** | **743** |
| `Billing` | 423 | 361 |
| Other · Electrical · Geotech · Mech · Contractor · Envelope · Landscape | 98 | — |

**743 of 10,017 top-level projects carry a named architect.** The firms, by project count:
Arcadis IBI 81 · DIALOG 68 · Chris Dikeakos 61 · GBL 48 · Pacific Coast 44 · Musson Cattell
Mackey 29 · Rositch Hemphill 28 · Yamamoto 26 · Ramsay Worden 19 · Keystone 17 · NSDA 15 ·
CEI 14 · Chandler 14 · Integra 13 · JWDA 11 · Christopher Bozyk 11 · VIA 11 · Perkins+Will 9.

That is the "reference jobs to mention when I call them" list Rory asked for, and it is nothing
like the client-side counts the dossiers have been quoting — **Arcadis shows 2 in the Brain and
81 here**, because Arcadis was the architect, not the payer.

**Searched and found nothing:** `ClendorProjectAssoc`, `PRClientAssoc`, `PRContactAssoc`,
`PRConsultant` across `*.cs`, `*.sql`, `*.ps1`, `*.py`. One hit — `DeltekClientContextService.cs`
— and that file reads the AR ledger, not the association table. The `"Architect"` matches in
`BriefGenerator.cs`, `BriefPdfGenerator.cs` and `OrgDossierViewModel.cs` are the Brain's
`CanonicalOrg.Kind`, not the Deltek project role.

⚠ Coverage is the catch: 743 of 10,017 is 7%. The field exists on every project and is filled in
on a fourteenth of them. Whether that is worth backfilling is a business call, not a data one.

## 2. The app holds TWO definitions of "won", and one of them is wrong

- `Kor.Operations.Data/Deltek/DeltekKorWonProjectAccessor.cs` → `WHERE pr.ChargeType = 'R'`.
  That admits **7,255 records that were never billed**, including live pursuits.
- `Kor.Operations.App/Crm/DeltekClientContextService.cs` → joins through **AR**, so it counts a
  project only once money has moved. This one is right.

`PR.Stage` is the field that settles it and neither of the two uses it as the primary test:
`InPursuit` 179 · `LOST` 86 · `DNP` 8 · `~WDEF~` 9,345 (of which 2,411 ever billed) · null+`P` 350.

**Gracewood at Fairwinds, Nanoose is `Stage = InPursuit`.** Deltek knew we had not won it; the
dossier said we had, because it read `ChargeType`.

## What a fix would look like

One accessor, one definition, used by both: a project counts as WON when it is `ChargeType <> 'P'`
**and** `Stage NOT IN ('InPursuit','LOST','DNP')` **and** (billed > 0 OR a contract fee is set).
Then surface the architect from `ClendorProjectAssoc` beside it, so a client card shows both who
paid and who we worked with.
