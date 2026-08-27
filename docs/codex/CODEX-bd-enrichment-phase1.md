# Codex Brief — Phase 1: carry & derive Discipline + Buyer Contact, map the feed sources

## Goal
Live opportunities currently ingest listing-only: `Discipline` is hardcoded `Unknown` and buyer contact is never set, even when the source carries them. Make ingestion **carry** buyer contact + commodity codes on the candidate, **derive** the KOR structural-relevance `Discipline` deterministically, and **map** the fields CanadaBuys and SAM.gov already return. The persistence layer already writes these columns — this brief only fills them.

## Verified context (already true in the code — do not re-plumb)
- `SqlOpportunityStore` INSERT + UPDATE already persist `Discipline` (param `@disc`, `SqlOpportunityStore.cs:315,370,535`) and `BuyerContactName/Email/Phone` (params `@buyerContactName/@buyerContactEmail/@buyerContactPhone`, `:313,367,524-526`; column widths NVarChar 120 / 255 / 40). No schema change, no store change needed.
- `IngestionService.BuildOpportunity` sets `Discipline = OpportunityDiscipline.Unknown` (`IngestionService.cs:594`) and never assigns the contact fields (`:578-595`).
- `OpportunityDiscipline` enum is coarse and KOR-relevance-oriented: `Unknown=0, Structural=1, Inspections=2, Mixed=3, OutOfScope=99` (`OpportunityEnums.cs:38-45`).
- `OpportunityCandidate` has no contact or commodity slots (`OpportunityCandidate.cs:14-72`).

## Changes

### 1. `OpportunityCandidate` (Kor.Opportunities.Core\Ingestion\OpportunityCandidate.cs)
Add optional (nullable) init-only fields, documented like the existing "structured fields a provider can supply when known" block:
- `string? BuyerContactName`
- `string? BuyerContactEmail`
- `string? BuyerContactPhone`
- `IReadOnlyList<string>? CommodityCodes` — raw commodity/category signals a source exposes (UNSPSC/GSIN/NAICS codes or their text, e.g. `"81101505 - Structural engineering"`).
All default null → zero behavior change for providers that don't set them.

### 2. New `DisciplineClassifier` (Kor.Opportunities.Core\Ingestion\DisciplineClassifier.cs)
Static, deterministic (no AI, no I/O). `public static OpportunityDiscipline Classify(OpportunityCandidate c)` using `CommodityCodes` + `Title` + `Description`:
- Build a lowercased blob of commodity codes + title + description.
- **Structural signal** = contains UNSPSC `81101505` OR the phrases `structural engineer`/`structural engineering`.
- **Other-discipline signal** = contains `architectural`/`mechanical engineer`/`electrical engineer`/`civil engineer` or their UNSPSC codes (`81101508`, `81101600`, `81101701`, `81101500`).
- Rules (in order): structural + other → `Mixed`; structural only → `Structural`; contains `inspection`/`building envelope`/`condition assessment` with no design signal → `Inspections`; a confident non-AEC signal (e.g. janitorial, IT/software, medical supplies, food services) with **no** structural signal → `OutOfScope`; else `Unknown`.
- Be conservative: only return `OutOfScope` on a confident non-AEC match; default `Unknown`. Mirror the keyword style already used by the relevance gate (see `SqlRelevanceGateRejectStore` / the relevance scorer for tone).
- Unit-test-friendly pure function.

### 3. `IngestionService.BuildOpportunity` (Kor.Opportunities.Data\Ingestion\IngestionService.cs:573-596)
- Replace `Discipline = OpportunityDiscipline.Unknown` with `Discipline = DisciplineClassifier.Classify(candidate)`.
- Add, with truncation to the store's column widths:
  - `BuyerContactName = Truncate(candidate.BuyerContactName, 120)` (null-safe; keep null when blank)
  - `BuyerContactEmail = Truncate(candidate.BuyerContactEmail, 255)`
  - `BuyerContactPhone = Truncate(candidate.BuyerContactPhone, 40)`
  Use the existing `Truncate`/blank-guard helper pattern already used for `ProjectCity` (`:584`).

### 4. Map the feed sources that already carry the data
- **CanadaBuys** — `GenericCsvOpportunityProvider` (`Kor.Opportunities.Data\Ingestion\Providers\GenericCsvOpportunityProvider.cs:130-217`): the openTenderNotice CSV has `contactInfoEmail`, `contactInfoName`, `contactInfoPhone`, `contactInfoCity`, `gsin`, `gsinDescription`, `unspsc`. Read them from the row (extend the hardcoded alias lists) and set `BuyerContactEmail/Name/Phone`, `ProjectCity` (replace the hardcoded null at `:211`), and `CommodityCodes` (gsin + unspsc + gsinDescription). Leave EstimatedValue as-is (CSV lacks it per `:213` comment — confirm and keep).
- **SAM.gov** — `SamGovOpportunityProvider` (`Kor.Opportunities.Data\Ingestion\Providers\SamGovOpportunityProvider.cs:246-262,338-384`): model the `pointOfContact` array (fullName/email/phone) and `naicsCode` on the row DTO, and map → `BuyerContactName/Email/Phone` and `CommodityCodes` (naicsCode). Keep the existing description-URL null behavior (do NOT add a fetch here — that's Phase 2).

## Constraints
- Additive and null-safe: new candidate fields optional; existing providers untouched behave identically.
- No DB schema migration (columns exist). No change to `SqlOpportunityStore` (it already writes these columns).
- Classifier is deterministic and conservative — never guess `OutOfScope`; default `Unknown`. It must not regress the existing relevance gate (this sets `Discipline`, a separate column from RelevanceTier/PrimeDisciplineType — do not touch those).
- Do not fetch any detail pages or documents in this phase (that is Phase 2). CanadaBuys/SAM changes are pure payload-field mapping.
- No destructive git operations.

## Pattern references
- Field mapping + truncation: existing `BuildOpportunity` (`IngestionService.cs:573-596`) and `GenericCsvOpportunityProvider` alias-list mapping.
- Keyword classification tone: the relevance gate / `SqlRelevanceGateRejectStore`.
- Candidate-field documentation style: the existing "structured fields a provider can supply when known" comment block in `OpportunityCandidate.cs:34-40`.
