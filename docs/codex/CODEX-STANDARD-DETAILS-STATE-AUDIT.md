# CODEX-STANDARD-DETAILS-STATE-AUDIT — adversarial verification of the 2026-09-07 state review

```
IMPORTANT: Do NOT run `dotnet build` or `dotnet test`. Do NOT run `dotnet publish`.
IMPORTANT: This audit is READ-ONLY. No writes to any database, share, workstation, or git state.
  - SQL: SELECT / COUNT only. Never INSERT/UPDATE/DELETE/EXEC/ALTER, even against "test" rows.
  - Files: read, list, hash, Get-Acl. Never delete, move, rename, touch, or create outside docs/codex/.
    The review calls several folders "cruft" (bridge outbox, _rendertest, .0002.rvt). Do NOT clean them.
  - Git: log / show / diff / status / ls-files only. No checkout, reset, clean, stash, add, commit, push.
Write your findings to docs/codex/CODEX-STANDARD-DETAILS-STATE-AUDIT-RESPONSE.md and stop.
```

## Mandate

On 2026-09-07 a state review of the Standard Details program was written from live reads of the repo,
two databases, the Drafting share, KOR-302N and KOR-204. It is at
`C:\Users\ilalonde\Desktop\KOR-StandardDetails-Review-2026-09-07.txt`. Read it first, in full.

Your job is to **prove it wrong.** Every claim in it must be independently re-derived by you, with your
own command and your own output, and marked one of:

- **CONFIRMED** — your evidence matches the claim (state the number you got, not "matches").
- **REFUTED** — your evidence contradicts it. Say what is actually true.
- **OVERSTATED** — directionally right but the wording claims more than the evidence supports.
- **UNVERIFIABLE** — you could not reach the evidence (say what you tried).

Then hunt for **what the review missed** — the second half of this brief. Assume the reviewer had
blind spots; the review itself admits one (it said "no tests" and was wrong, one test exists).

Do not congratulate. Do not fix anything. Report.

## Ground truth you may rely on

- Repos (all on this machine): `C:\VIsual Studio Projects\Operations` (branch develop),
  `C:\VIsual Studio Projects\KOR.Drafter` (branch standard-details-rendered-image-store),
  `C:\VIsual Studio Projects\KOR.RevitTools` (branch main).
- Databases on `KOR-APP01\SQLEXPRESS`: `KorStandards` (schema `detail`) and `KorTransmittals` (schema `dbo`).
  Connection strings with credentials are in `Kor.Operations.App\App.config` (`KorStandardsDb` =
  standards_reader, SELECT-only by design; `KorTransmittalsDb` = transmittals_app, which CAN write —
  you use it for SELECT only). `sqlcmd` is installed; `-C -W` flags work. `-i` needs a Windows path.
  standards_reader cannot SELECT `detail.DetailHistory` or `detail.Component` — that is expected, not a finding.
- Shares reachable from here: `\\Kor-fs01\Drafting\KOR-Standards\`, `\\Kor-fs01\Drafting\KOR-Deploy\`,
  `\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\`, `\\KOR-302N\C$\KOR.Drafter\`,
  `\\KOR-302N\C$\ProgramData\KOR\kor-tools.json`, `\\KOR-204\C$\Newerforma\` (Jim DesRoches' workstation).
- Meeting transcript: `C:\Users\ilalonde\Downloads\Standard Detail discussion (1).docx` (Teams export;
  the text is in `word/document.xml`). The .mp4 beside it is out of scope for you too.
- Memory of the program's history is NOT available to you. Use git history and the briefs in
  `docs/codex/CODEX-STANDARD-DETAILS-*.md`, `CODEX-PUBLISH-TO-MASTER*.md`, `CODEX-DETAIL-ART-EXPORT.md`,
  and the blueprint `docs/KOR-StandardDetails-Governance-Review-2026-08-06.md`.

## Evidence discipline (this is a rule, not advice)

A truncated command cannot support a claim about a whole. If your command has `head`, `tail`,
`Select-Object -First`, `TOP N` or `grep -m`, you may not write "all", "every", "none", "only", or a
count from it. Count the population (`COUNT(*)`, `Measure-Object`, `wc -l`) and state **X of Y**.
Quote the command and the relevant output line for every verdict. No verdict without a command.

## Part 1 — the claims to attack

Work through the review section by section. The list below is the minimum; anything else stated as
fact in the review is also in scope.

### Section 2, "what is live"
1. KorStandards counts: 608 active / 4 retired; 603 content-verified = placeable; 5 unverified and
   they are exactly 00100, 00133, 00173, 00501, 00526 and all have VariantsDiverge=1. Discipline
   294/148/132/34. Kind 377/73/157/1 NULL. IsSheet=1 → 156. Check the review's arithmetic too.
2. `vw_PaletteCatalog`: 603 distinct placeable details across 1,049 view rows; 5 unplaceable across 26.
   Read the VIEW DEFINITION (`sp_helptext` or `OBJECT_DEFINITION`) and state in one sentence what
   `IsPlaceable` actually means. Does it exclude VariantsDiverge? Does it include human-confirmed?
3. `detail.RenderedImage`: 604 detail rows with both Png and Pdf, 287 component rows with Png only;
   0 placeable details lacking art; the 4 active details with no image are 00133/00173/00501/00526.
   Sizes 84 MB / 27 MB.
4. `detail.DetailOccurrence`: 1,079 rows, MAX(ObservedAtUtc) = 2026-08-06, DocumentName =
   `Kor_Structural_Standards_Template_R25.rvt` on every row (count rows per DocumentName).
5. `vw_QuickInsertCatalog`: 288 placeable, 0 not.
6. KorTransmittals: Documents / DocumentVariants / DocumentVersions / FileBlobs / ApprovalRecords /
   PublicationRecords / StandardDetailPromotionOutbox all 0; AuditEvents 43; DocumentGroups 4 named
   Concrete, Steel, General, Wood Frame.
7. Files: the two .rvt sizes and modified times on FS01 and 302N (four files). Also verify they are the
   SAME bytes across the two hosts (hash them — 120 MB each, it is fine) or say they differ.
8. `App.config` `StandardDetails.*` keys point where the review says; `kor-tools.json` on 302N has
   `detailsPalette.templatePath` = the 302N LOCAL authoring path and `showUnverified=false` on both sections.
9. Bridge on 302N: `app\artifacts\2026\KOR.Drafter.Bridge.dll` FileVersion 1.0.36.0, the other years
   1.0.31.0; newest log line is "Bridge down" 2026-09-04 14:34; inbox has 3 files, all verb `ping`;
   outbox 94,091 files / ~2,960 MB, oldest 2026-07-29; `inbox\done` 94,136 files.
   (Counting 94k files over SMB takes a minute or two. Do it once. Do not delete anything.)

### Section 3, "what has not shipped"
10. Newest zip on the Newerforma share is V17.zip dated 2026-08-24; `tools\workstation-install\Install-Newerforma.ps1`
    self-updates from that share on launch.
11. `\\KOR-204\C$\Newerforma\Kor.Operations.App.exe` modified 2026-08-24 00:49. Also check: is there any
    OTHER copy of the app on KOR-204 newer than that (search C$ for Kor.Operations.App.exe, bounded to
    Program Files, ProgramData, Users\jdesroches)?
12. `git log origin/develop..develop` in Operations = 14 commits; `git log main..develop` = 509.
13. Fleet `KOR-Deploy\current\{2025,2026}\version.txt` both say `1.0.0+2026-08-24T21:46:58 (git 72ffee6)`;
    the fleet `KOR.RevitTools.dll` does NOT contain the string `DetailsPaletteCommand`; the 302N pilot
    mirror `C:\KOR\KOR-Deploy-Pilot\current\2026\KOR.RevitTools.dll` DOES. 302N's `kor-deploy.path`
    points at the pilot mirror.
14. KOR.Drafter: `git log main..HEAD` = 5 commits (list them); `db/078_RenderedPdf.sql` is untracked
    (`git ls-files` returns nothing) yet the `Pdf` column exists live. Which other db/*.sql are untracked?
15. KOR.RevitTools main is at a6c8e38 and has nothing unpushed.

### Section 4, "the loop never ran"
16. App.config security groups exactly as quoted (five keys). Nobody but ilalonde in Approvers/Publishers.
17. There is no path other than migration 069/070 by which the 603 details became content-verified.
    You cannot read DetailHistory as standards_reader; instead find every writer of `Confidence` in
    KOR.Drafter\db\*.sql and in the app, and show that the app's writer (`detail.PromoteDetail`) writes
    `human-confirmed`, never `content-verified`, so a count of 0 human-confirmed proves no app approval
    ever landed.

### Section 5, "protection and hygiene"
18. ACLs: 0 explicit (non-inherited) ACEs on `KOR-Standards`, on `template\MASTER`, and on the master
    .rvt; `kor\BMZ_FS_Drafting_RW` has Modify by inheritance. Also report the ACL on `template\AUTHORING`
    and on the KOR-Standards PARENT (`\\Kor-fs01\Drafting`) so we know where inheritance starts.
19. `MasterPublisher.ReplaceMaster` uses `File.Replace(..., destinationBackupFileName: null, ...)`
    and nothing else in the class or its callers copies or archives the previous master first.
20. Scratch folder counts/sizes under KOR-Standards (`_detailrender`, `_rendertest`, `_verbtest`,
    `review-images`, `detail-previews`). Is `StandardDetails.PreviewCachePath` (`detail-previews`) still
    READ by any code path, or dead config?
21. Census: the only writers of `detail.DetailOccurrence` are migrations 005/005b/014/017/024 generated by
    `KOR.Drafter\db\tools\generate_*.py`; no tool, verb, or app action in any of the three repos inserts
    into it. Search the bridge source too (on 302N: `\\KOR-302N\C$\KOR.Drafter\app\src\`, read-only).
22. Tests: exactly one test file references any `StandardDetails` class:
    `Kor.Operations.App\EngineeringTools.Tests\StandardDetails\CustomPdfSheetComposerTests.cs` with one
    `[Fact]`. Search every *Tests* project in all three repos, including the folder named
    `Kor.Transmittals.App.Tests` (its csproj is `Kor.Operations.App.Tests.csproj`).
23. The three Kind/IsSheet contradictions are 00001, 00367 (general-note, IsSheet=0) and 00368
    (IsSheet=1, Kind NULL), and there are no others.

### Section 6, Jim's feedback vs what landed
24. For each row marked DONE, open the cited commit (`git show <hash> --stat` then the diff) and confirm
    the commit actually does what the row says. For "Sheet number still required": find the check in
    `SheetComposerWindow.xaml.cs` and confirm there is no auto-numbering path that pre-fills it.
25. "0 composed sheets": is that the right measure? A composed sheet lives as a ViewSheet in the AUTHORING
    model AND as a governance record. The governance side is 0 (claim 6). Could sheets exist in the .rvt
    without a record (e.g. from the Sep 2 live test)? You cannot open the .rvt; say what would prove it
    and whether any log or reply file under `\\KOR-302N\C$\KOR.Drafter\bridge\inbox\done` for verb
    `newsheet` dated Sep 2-4 shows a `savedoc` following it.
26. Sheet size: confirm `SheetWidthMm = 914.4` / `SheetHeightMm = 609.6` hard-coded and that no
    overflow-to-next-sheet logic exists in `SheetComposerWindow.xaml.cs` or `CustomPdfSheetComposer.cs`.

### Section 7, the meeting
27. Re-read the transcript yourself and check the "other subjects" list for omissions or misattributions.
    Confirm the transcript really ends at 51:52 and the recording header really says 1h 32m 51s.

## Part 2 — what the review missed (hunt here after Part 1)

The review was a STATE audit. It did not read the Sep 3-4 code for defects, and those 14 commits have
never had an adversarial pass (only the Sep 2 publisher and composer were audited). Go where it did not.

A. **The Sep 3-4 commits themselves** (`git log d5917259^..6a951b30 -- Kor.Operations.App/StandardDetails`).
   Read each diff as a hostile reviewer. Priorities: silent-wrong-result, data loss, a path that writes to
   KorStandards through the promoter that is not logged, UI state that lies (a "✓ Saved" that shows when
   the write failed), async re-entry after `await` without re-checking the selected row, a catch that
   swallows and then reports success.
B. **The static-PDF path.** `SheetComposer.OpenDetailPdfAsync`: what happens for a detail with no stored
   PDF when the bridge is down (the normal state now)? Clean error, or a 5-minute hang on a bridge timeout?
   What is the timeout, and does the busy-state (`Generating…`) always restore?
C. **Publish-to-Master now depends on `exportviews`.** That verb exists only in bridge 1.0.36 (302N, 2026
   only). If the gatekeeper's Revit is 2025, or any machine other than 302N, what does publish do — fail
   before or AFTER `File.Replace`? Trace `d16127ae`. A publish that replaces the master and then fails on
   capture is a half-state; say whether that is possible.
D. **Promoter surface area.** List every proc `standards_promoter` can EXECUTE (from migrations 066, 071,
   075, 076, 077, 078, and any others). For each: does it journal to a history table? Which are reachable
   from the app without an approval decision (Kind, IsSheet, SetRenderedImage, DeleteRenderedImage,
   SetRenderedPdf)? Is `DeleteRenderedImage` reachable from any UI, and gated how?
E. **Palette fleet-readiness.** `kor-tools.json` on 302N points `templatePath` at a 302N-local file. On any
   other fleet machine that path does not exist. Read `DetailsPaletteCommand.cs` / `KorConfig` in
   KOR.RevitTools: what does the palette do when `templatePath` is missing — clear message, or an
   unhandled exception inside Revit? Is there a fleet-wide config file, or is kor-tools.json per machine?
F. **Loader sync scope.** The KOR Tools loader copies TOP-LEVEL files only from `current\<year>\`. Confirm
   the real SqlClient impl and `sni.dll` are top-level in the 302N pilot mirror, and that
   `build\publish.ps1` (KOR.RevitTools) would place them top-level for the fleet.
G. **Census staleness vs ElementIds.** The app resolves views by `ViewElementId` from a census taken
   2026-08-06 against a file that has since been cleaned (172 elements + 26 group types deleted on Sep 2),
   saved-as twice, and renamed. ElementIds survive save-as, but do any of the 1,079 census ViewElementIds
   fall inside the id set deleted on Sep 2? The delete proposal is at
   `\\KOR-302N\C$\KOR.Drafter\tasks\template\delete-proposal.csv` (58 rows) and the bridge reply files
   under `inbox\done` from Sep 2 list cascaded ids. Cross-check.
H. **The register Jim was sent.** Ian told Jim on Sep 3 "609 details, 604 approved". Today: 608 active,
   603 content-verified. Find what changed between Sep 3 and Sep 7 (`RetiredAtUtc`, `UpdatedAtUtc` on
   detail.Detail) and whether any document sent out (Desktop or docs/) now states a number that is false.
I. **Credentials.** Both SQL logins' passwords sit in `App.config` in plaintext, and the same
   standards_reader password is in 302N's `kor-tools.json`. The blueprint deferred this. Confirm whether
   the promoter password is in any file that a non-gatekeeper receives (the app zip, the fleet KOR-Deploy,
   kor-tools.json) — that would make "approval" bypassable by anyone with the zip.
J. **Anything else.** Config keys that nothing reads, code that references the retired `detail-previews`
   folder, `PartImageRoot` pointing at `\\Kor-fs01\Drafting\2026\QuickPick\BMP` while the parts art now
   lives in the DB, dead windows (`GroupEditWindow`, `CreateStandardDocumentWindow`) still wired to
   buttons, the `_test.txt` file, `.0002.rvt` inside MASTER being picked up by anything that globs `*.rvt`.

## Output

`docs/codex/CODEX-STANDARD-DETAILS-STATE-AUDIT-RESPONSE.md`, three parts:

1. **Claim ledger** — one row per numbered claim above (and any other fact you checked): claim, verdict,
   the command you ran, the output line that decides it. Group by review section.
2. **Missed findings** — ranked **Critical / Material / Minor**. Each: file:line or path, the concrete
   scenario (inputs → wrong outcome), what would prove or disprove it. Then pick **exactly one
   ship-blocker** — the single thing that must be fixed before this reaches Jim.
3. **What you could not check** — and what access or tool would be needed.

Report only. Do not apply fixes; they will be briefed separately once the findings are verified.
Ping when the response file is written.
