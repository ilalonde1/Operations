# Duplication Consolidation Response

Scope: applied source edits only. I did not run `dotnet build`, `dotnet test`, destructive git operations, file/folder/project deletion, or architecture regeneration.

## What Moved

Credential redaction moved into `Kor.Operations.Core`:

- Added `Kor.Operations.Core/Logging/CredentialPatterns.cs`.
- Added `Kor.Operations.Core/Logging/CredentialRedactingEnricher.cs`.
- Added `Kor.Operations.Core/Logging/CredentialRedactingPolicy.cs`.
- Added the existing `Serilog` package reference to `Kor.Operations.Core/Kor.Operations.Core.csproj`, because the shared types implement Serilog interfaces.
- Updated `Kor.Operations.FileSync.Service/Logging/SerilogBootstrap.cs` to import `Kor.Operations.Logging`.
- Left the old App and FileSync files on disk, per the no-delete instruction, but reduced them to moved-location marker comments so the duplicate types are no longer compiled there.

`MajorProjectRecord` moved within `Kor.Opportunities.Data`:

- Added `Kor.Opportunities.Data/Ingestion/Providers/MajorProjectRecord.cs`.
- Removed the four identical nested provider records from:
  - `AbMajorProjectsInventoryProvider.cs`
  - `BcMajorProjectsInventoryProvider.cs`
  - `CaSocrataMajorProjectsInventoryProvider.cs`
  - `CeqanetMajorProjectsInventoryProvider.cs`

Deltek link DTO records moved next to the existing canonical resolver home in `Kor.Opportunities.Data.Awards`:

- Added `Kor.Opportunities.Data/Awards/DeltekLinkModels.cs`.
- Removed local copies of `DeltekClientCandidate`, `CompanyMatch`, `CanonicalOrgTarget`, `LinkPlan`, `LinkPlanRow`, `ReviewRow`, and `DedupCandidateRow` from:
  - `Kor.Opportunities.Worker/Services/BdDeltekLinkDryRunJob.cs`
  - `tools/BdDeltekLink/Program.cs`
- Removed local copies of `DeltekClientCandidate` and `CompanyMatch` from `tools/BdSeedImport/Program.cs`.

## What I Left Alone

`tools/BdResearchImport/Program.cs` still has its own `MajorProjectRecord`. I did not merge it because it is not the same shape as the four provider copies: it uses `Source` instead of `Province` in the primary constructor and has many extra source-specific fields plus init-only canonical-party properties. Replacing it with the provider persistence record would be a behavior and call-site change, not a consolidation.

I did not touch `docs/map-audit/KorMapSyncRunner.cs` or retire any prototype tools.

I did not merge `DeltekFuzzyMatch`; only the duplicated record types around it were moved.

## Verification

Allowed verification only:

- `rg` confirmed credential redaction implementations now exist only under `Kor.Operations.Core/Logging`, with moved-location markers in the old App/FileSync files.
- `rg` confirmed the four provider `MajorProjectRecord` copies now resolve to one shared `Kor.Opportunities.Data/Ingestion/Providers/MajorProjectRecord.cs`; `BdResearchImport` remains separate.
- `rg` confirmed the duplicated Deltek DTO records now exist in `Kor.Opportunities.Data/Awards/DeltekLinkModels.cs`.
- `git diff` reviewed the changed source set.

## DeltekFuzzyMatch Difference Report

Changed no code in the matchers.

Worker and `tools/BdDeltekLink` are behaviorally aligned for company matching:

- Both keep only legal/entity boilerplate in `CompanySuffixTokens`: `inc`, `incorporated`, `ltd`, `limited`, `llc`, `llp`, `lp`, `corp`, `corporation`, `co`, `company`, `international`, `intl`.
- Both explicitly preserve distinctive business-line tokens such as `properties`, `construction`, `architecture`, `engineering`, `development`, `consulting`, `group`, and `holdings`.
- Both normalize punctuation to spaces, remove suffix tokens, then score by Levenshtein similarity.

`tools/BdSeedImport` differs:

- It strips all of the Worker/`BdDeltekLink` legal/entity tokens, plus `group`, `holdings`, `properties`, `property`, `construction`, `constructors`, `development`, `developments`, `developers`, `dev`, `consulting`, `consultants`, `architects`, `architecture`, `engineering`, and `engineers`.
- It also has `NormalizePersonName`, used for exact normalized contact-name matching. That method has no equivalent in the other two copies and is separate from company matching.

Inputs that produce different company results:

- Target `Fort Properties Ltd` with candidates `FORT Architecture` and `Fort Properties Ltd`.
  - Worker/`BdDeltekLink`: `fort properties` vs `fort architecture`; the exact `Fort Properties Ltd` candidate wins.
  - `BdSeedImport`: both candidates collapse to `fort`; both score `1.0`, and ordering by candidate name can put `FORT Architecture` first.
- Target `Bold Properties` with candidates `Bold Construction` and `Bold Properties`.
  - Worker/`BdDeltekLink`: preserved second token separates them.
  - `BdSeedImport`: both collapse to `bold`.
- Target `Apex Engineering` with candidates `Apex Consulting` and `Apex Engineering`.
  - Worker/`BdDeltekLink`: preserved business-line tokens separate them.
  - `BdSeedImport`: both collapse to `apex`.

Newest-looking copy:

- The Worker/`BdDeltekLink` company matcher looks newest and more deliberate. `git blame` shows the narrowed token set and R60/R62 comment came from `caa72fbd` on 2026-06-01, explicitly to prevent false positives like `Fort Properties Ltd` vs `FORT Architecture`.
- `BdSeedImport`'s broader company suffix list dates to `dbbd1a10` on 2026-05-04. The file was touched later on 2026-07-11, but the broad company suffix-token lines were not.
