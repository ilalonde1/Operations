# BD-PM-AUDIT-20260530-R6.md

## Round 41  code + data verification
- A.1 ProvinceFromSeismicRegion behavior parity: pass. The only non-BC branches in `ProvinceFromSeismicRegion()` are `WA*` and `OR*` at `tools/BdResearchImport/Program.cs:4654`; live `IslandOkanaganPairing` rows are all BC regions (`CentralOkanagan`, `GreaterVictoria`, `Kamloops`, etc.) and all have `Province='BC'`. The constant `"BC"` wrapper at `Program.cs:3766` is behavior-equivalent for the observed Island/Okanagan data.
- A.2 sourceLabel substitutions: pass. The Island/Okanagan wrapper keeps `sourceLabel: "IslandOkanaganPairing"` at `Program.cs:3768`, so existing `SourceKey = Sha1("IslandOkanaganPairing|...")` stays stable. Lower Mainland and Edmonton intentionally get new `ProjectStage` / resolver labels through `sourceLabel` at `Program.cs:3847` and `Program.cs:3892`.
- A.3 Edmonton/LM ingest counts: partial. Live DB has `LowerMainlandPairing=401` and `EdmontonPairing=73`; the final 474-row invariant holds. I could not identify the claimed one-row LM collision from source because the `pairings.jsonl` inputs are not present in the repo; live DB has 401 distinct surviving LM rows and no duplicate `ProjectName + RegionName` survivors.
- A.4 Edmonton province=AB: pass. Live DB shows all 73 `EdmontonPairing` rows have `Province='AB'`; spot-check regions include Edmonton, Fort Saskatchewan, Lamont County, Parkland, Red Deer, and Spruce Grove.
- B.1 nested dedupe-plan.csv: pass with T3 finding below. This was not introduced by Round 41; the tool has always used cwd-relative `DefaultOutputDirectory = @"tools\BdCanonicalDedup\output"` at `tools/BdCanonicalDedup/Program.cs:13`. The nested plan and root plan are byte-identical by SHA-256 and both contain 97 merge rows, so the reviewed dry-run file matches the committed plan.
- B.2 Air Studio / Strand post-commit data: pass. Air Studio id 90 has 1 `KorPursuit` buyer reference and 1 MPI architect reference; Strand id 43 has 5 `KorPursuit` buyer refs, 3 MPI proponent refs, and 1 MPI GC ref. The Strand GC row is a real same-project team role (`Arlo`) and not an orphaned loser reference; no Air/Strand references to deleted loser ids remain.
- B.3 KOR-self-merge FK landscape: pass. The Round-41 KOR loser id 70937 is gone. Post-Round-42, id 38918 has 99 MPI structural-engineer references and zero checked references as MPI proponent, MPI architect, MPI GC, OpportunityAward awarded-to, or OpportunityAward awarding org.

## Round 42  code + data verification
- C.1 CSV parser robustness: pass. `LoadDedupAllowlist()` splits on comma and only adds rows where both first fields parse as `long` at `tools/BdCanonicalDedup/Program.cs:198`; the header and comment lines are ignored cleanly, and the five data rows parse.
- C.2 Content include actually copies: pass. `tools/BdCanonicalDedup/BdCanonicalDedup.csproj:26` includes `dedup-non-similar-allowlist.csv` as `Content` with `CopyToOutputDirectory=PreserveNewest`, matching the assembly-directory lookup at `Program.cs:193`.
- C.3 First-use risk: pass. No `rejected-pairs.csv` exists under `tools/BdCanonicalDedup`, and the source-controlled allowlist first appears in commit `0a91388`; I found no evidence that an earlier committed `--pairs` honing run was unintentionally skipped due to missing allowlist entries.
- D.1 5 pairs landed: pass. Live DB contains id 38918 `KOR Structural Ltd.` with `Kind='KorStructural'`; loser ids 69930, 69934, 70675, 70948, and 71123 are gone.
- D.2 Kind ranking: pass. `KindRank` gives `KorStructural=0` and `Competitor=1` at `tools/BdCanonicalDedup/Program.cs:37`; pair merge chooses the lowest-rank member as `bestKind` at `Program.cs:279`, so BMZ/KOR-variant competitors merge into survivor kind `KorStructural`.
- D.3 Enrichment collision resolution: pass. `CommitGroupAsync()` calls `DeleteEnrichmentCollisionsAsync()` before repointing FKs at `Program.cs:640`; the collision delete preserves the survivor's existing `ProviderName` row and deletes colliding loser enrichment rows at `Program.cs:741`. Live id 38918 has survivor enrichment rows for `BcRegistry`, `DataHoning`, and `KorCapability`.
- D.4 99-project KOR count: pass. Live DB has exactly 99 `MajorProjectsInventory` rows with `StructuralEngineerCanonicalOrgId=38918` and zero remaining structural refs to the five Round-42 loser ids.
- D.5 Alias preservation: pass. Live id 38918 has the five Round-42 loser display names as `Source='DedupeMerge'` aliases, plus the Round-41 `KOR Structural` alias: `Bryson Markulin Zickmantel`, `Bryson Markulin Zickmantel (BMZ) Structural Engineers`, `Bryson Markulin Zickmantel Structural Engineers`, `KOR Structural Engineers`, and `KOR Structural (One Burrard Place — per KOR's own portfolio)`.
- D.6 FK repoint counter reality check: pass with T3 finding below. Hard DB check across the dedup `FkTargets` found zero remaining references to loser ids 69930, 69934, 70675, 70948, and 71123, so FKs were repointed. The pair-merge summary's zero FK counters are a reporting bug, not an orphaned-FK data bug.

## New findings
### T1 (Critical / High)
- No new T1 findings.

### T2 (Medium)
- No new T2 findings.

### T3 (Low)
- [T3.001] `tools/BdCanonicalDedup/Program.cs:13`  Default output path is cwd-relative, producing nested audit artifacts. Why it bites: running the tool from `tools/BdCanonicalDedup` writes the default output to `tools/BdCanonicalDedup/tools/BdCanonicalDedup/output`, while running from repo root writes to `tools/BdCanonicalDedup/output`. That already produced two `dedupe-plan.csv` locations for Round 41 and can make reviewers inspect the wrong artifact. Repro: launch from the tool directory without `--out`; `ImportOptions.Parse()` keeps `output = DefaultOutputDirectory` at `Program.cs:938`. Fix: resolve the default output from repo root or from the project/assembly directory, and print the absolute output path before writing.

- [T3.002] `tools/BdCanonicalDedup/Program.cs:299`  Pair-merge commits do not aggregate FK repoint counts into the summary. Why it bites: `CommitGroupAsync()` returns a `GroupCommitResult` with `FkRepointsByTable`, but `RunPairsMergeAsync()` discards it and only increments `GroupsCommitted`. The final "FK repoints by table" report can show zero for every table even when FKs were actually repointed, which masks whether a pair merge touched graph edges. Repro: inspect `RunPairsMergeAsync()` at `Program.cs:299`; no `AddTableCount` loop mirrors the canonical-name merge path. Fix: capture the returned `GroupCommitResult` and add its `FkRepointsByTable` values into `summary.FkRepointsByTable`.

## Prior-round regression check
- Round 37 anchors: clean. `SqlCrmEngagementStore.AllColumns` still includes the five BD-tracking columns; `BdCanonicalDedup.FkTargets` still includes `CrmEngagements.BuyerCanonicalOrgId`; `DeleteBdTrackingChildrenAsync` is still called; BD tracking linked projects still filter `m.RetiredAtUtc IS NULL`; `CompetitionInfoView.View_Loaded` is still wrapped; `OpportunitiesViewModel` still logs the outer detail-load catch; `extract.py` still derives paths from `__file__`.
- Round 38 anchors: clean. PM Tools still routes through the chooser; `AppModule` still registers the two PM Tools VMs as singletons and the chooser/workload/capacity windows as transients; `PmCapacityWindow.xaml` still has the five-row capacity grid with `ScrollViewer Grid.Row="4"`.
- Round 39 anchors: clean. `PmCapacityWindow` still stores/unsubscribes singleton-VM handlers; `OperationsApp.OnExit` still disposes the DI provider; `PmToolsChooserWindow.OpenOrActivate<T>()` still scans `Application.Current.Windows`; capacity XAML remains free of the removed meeting-only resources.
- Round 40 anchors: clean. `RefreshPriorityProjects()` still preserves existing enrichment when no enricher is supplied, and the priority-save failure path still skips stale reverts when `row.MeetingPriority != attemptedPriority`.

## Summary
- Round 41 code verified: pass
- Round 41 data integrity: pass, with one unverifiable LM input-collision detail because the source JSONL is not present
- Round 42 code verified: pass
- Round 42 data integrity: pass
- New findings: T1=0, T2=0, T3=2
- Prior-round regression: clean
