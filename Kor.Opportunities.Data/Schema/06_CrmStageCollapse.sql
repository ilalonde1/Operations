/*
    Kor.OpportunitiesDb - migration 06.
    Collapses opportunities.CrmEngagements.Stage from 9 to 4 values:
        ProposalDraft (2)      -> Drafting (1)
        OnHold (9)             -> Drafting (1) + OutcomeNotes prefixed
        Presenting (4)         -> Submitted (3)
        Negotiating (5)        -> Submitted (3)
        Withdrawn (8)          -> Lost (7) + OutcomeNotes prefixed

    Pursuing (1) keeps value 1, renamed "Drafting" in code.
    ProposalSubmitted (3) keeps value 3, renamed "Submitted" in code.
    Won (6), Lost (7) values preserved.

    Idempotent: rows already at terminal values are skipped by the WHERE clauses.
    Safe to re-run.
*/

-- ProposalDraft (2) and OnHold (9) -> Drafting (1). OnHold gets a note prefix.
UPDATE opportunities.CrmEngagements
   SET Stage = 1
 WHERE Stage = 2;

UPDATE opportunities.CrmEngagements
   SET Stage = 1,
       OutcomeNotes = COALESCE(N'[OnHold (auto-migrated 2026-05-15)] ' + NULLIF(OutcomeNotes, N''), N'[OnHold (auto-migrated 2026-05-15)]')
 WHERE Stage = 9;

-- Presenting (4) and Negotiating (5) -> Submitted (3).
UPDATE opportunities.CrmEngagements
   SET Stage = 3
 WHERE Stage IN (4, 5);

-- Withdrawn (8) -> Lost (7). Note the original distinction in OutcomeNotes.
UPDATE opportunities.CrmEngagements
   SET Stage = 7,
       OutcomeNotes = COALESCE(N'[Withdrawn (auto-migrated 2026-05-15)] ' + NULLIF(OutcomeNotes, N''), N'[Withdrawn (auto-migrated 2026-05-15)]')
 WHERE Stage = 8;
