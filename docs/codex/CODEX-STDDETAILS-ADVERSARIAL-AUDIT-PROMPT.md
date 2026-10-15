DEEP ADVERSARIAL AUDIT - THE STANDARD DETAILS GOVERNANCE BRIDGE (Tasks A-D + WP1)

You are a hostile reviewer. Your job is to BREAK this, find what is wrong, unsafe, or a lie - not
to praise it. Assume the author was optimistic and the "verified" claims are incomplete. Read the
real code and the live-run behaviour; do not take the summaries below as truth, use them only as a
map of what to attack. READ-ONLY: do not build, run, execute SQL, or modify anything. Report
findings ranked by severity with file:line evidence, then a one-line verdict:
SHIP / SHIP WITH FIXES (list) / DO NOT SHIP (why).

WHAT THIS IS (the map - verify all of it against the code)
A bridge added to the Operations app's Standard Details module so an engineer's APPROVAL of a
document promotes a linked KorStandards detail to Confidence='human-confirmed' (placeable). Two
databases: KorTransmittals (the app's documents/workflow) and KorStandards (the 612 KOR-D details).

THE SURFACE TO AUDIT
App module (C#, WPF): Kor.Operations.App/StandardDetails/
  - KorStandardsReadRepository.cs        (Task A: read the 612 via detail.vw_PaletteCatalog)
  - LinkDetailWindow.xaml(.cs)           (Task B: pick a detail, write Documents.DetailNumber)
  - StandardDetailsRepository.cs         (SetDocumentDetailNumberAsync; DecideAsync outbox enqueue;
                                          LoadOutbox / pending / MarkDone / MarkFailed)
  - KorStandardsPromoterRepository.cs    (Task D: EXEC detail.PromoteDetail)
  - PromotionQueueWindow.xaml(.cs)       (Task D: queue view + Process Pending)
  - StandardDetailsWindow.xaml / .xaml.cs / .Logic.cs  (buttons, wiring, approve handler, processor)
Config chain: App.config <connectionStrings> (KorStandardsDb, KorStandardsPromoterDb);
  AppConfigKeys.cs; Options/AppOptions.cs (DatabaseOptions); CompositionModules/CompositionHelpers.cs.
SQL: db/KorTransmittals/001-005*.sql (baseline, variants+DetailNumber, purge propose/apply, outbox);
  and in the KOR.Drafter repo db/066_PromoteDetailProcAndPromoter.sql (proc + standards_promoter).

ATTACK THESE SPECIFICALLY (do not stop here - add anything else you find)

1. TRANSACTION / ATOMICITY. DecideAsync inserts the outbox row inside the approval transaction.
   Prove or DISPROVE that the outbox row and the approval are truly atomic. Any path where the
   version updates but the outbox insert is skipped or vice-versa? What if the DetailNumber SELECT
   or the outbox INSERT throws mid-transaction - is it rolled back, and does the UI mislead the user?

2. THE PROMOTION CANNOT CORRUPT AN APPROVAL. The processor runs AFTER the approval commits. Confirm
   a promotion exception cannot roll back, re-run, or double-apply the approval. Check the approve
   handler ordering (Logic.cs) and error handling. Does a thrown processor error leave the UI or the
   outbox in a lying state (e.g. row marked done when the proc failed, or failed when it succeeded)?

3. THE SECURITY BOUNDARY IS REAL. standards_promoter is meant to have EXECUTE on detail.PromoteDetail
   and NOTHING else - the app must never write a KorStandards table directly. Audit: can the promoter
   connection do anything else? Does any app code open the promoter or reader connection and run
   ad-hoc SQL? Is the promoter password in cleartext, and is that consistent with the existing
   posture (it is - note it, don't inflate it)? Does ownership chaining actually hold (proc and
   tables same owner/schema)?

4. THE PROC. detail.PromoteDetail: idempotency (re-drain same row), unknown DetailNumber, the
   @ToConfidence guard, XACT_ABORT + TRY/CATCH + THROW behaviour, and whether a caught RAISERROR
   actually rolls back and rethrows so the outbox records a FAILURE (not a false success). Can it
   ever promote the WRONG detail, or write a DetailHistory row that disagrees with the Detail row?

5. THE OUTBOX PROCESSOR. Re-processing safety, AttemptCount growth, a poison row (permanent failure)
   blocking others, Status transitions (0->1, 0->2), and whether a row can be processed twice
   concurrently (two app instances) causing a double history row - or is the proc's idempotency the
   only guard, and is that enough?

6. THE LINK. SetDocumentDetailNumberAsync + the unique index UX_Documents_DetailNumber. Can two
   records end up linked to the same detail? Can a link silently overwrite another? Is the collision
   error (2601/2627) actually caught and shown, or can it crash? Does QUOTED_IDENTIFIER matter for
   the app driver on this filtered-index table (it is ON by default for Microsoft.Data.SqlClient -
   verify that assumption rather than trusting it)?

7. GRACEFUL DEGRADATION. If KorStandardsDb or KorStandardsPromoterDb is absent/wrong, does the module
   still open and does approve still work, with promotion simply unavailable? Any NullReference or
   startup throw? Does an unlinked approval correctly enqueue nothing without error?

8. THE WP1 MIGRATIONS. 002 keeps DocumentVariantId nullable with FILTERED unique indexes for the
   pre-Task-G window - is that actually collision-safe for the live app's current insert path? Does
   004's purge guard truly refuse any world other than the 12 known drafts? Any migration that is not
   re-runnable, or that an interrupted run leaves half-applied?

9. LIES IN THE DOSSIER. Read Desktop/KOR-StandardDetails-Governance-Module-2026-09-02-web.pdf claims
   against the code and call out anything overstated: "atomic", "cannot undo", "locked out",
   "verified", "zero footprint". If a claim is not actually true in the code, that is a finding.

RULES
- Read-only. Cite file:line for every finding. Rank by severity. Distinguish CONFIRMED (you traced
  it) from PLAUSIBLE (you suspect it). Do not invent findings to look thorough - if a thing is
  genuinely sound, say so.
- End with the one-line verdict.
