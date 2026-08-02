/*
    markup.Lexicon — load the first eight entries.

    Produced 2026-08-01 by mining 11,798 annotations from 56 markup PDFs across
    seven engineers, then replay-testing against the Sherwood 31207-01 pair
    (markup 10:11, model saved 10:43 — a 32-minute gap).

    ONE ENTRY IS DRAFTABLE. E1 is 'replay-verified': 11 of 11 member marks were
    present on the sheet they were marked on. Everything else is 'unverified'
    and can only REFER a mark to the engineer — that is enforced by
    vw_LexiconDraftable, not by anyone remembering.

    NOTE ON WHAT IS *NOT* HERE. Two candidate patterns were rejected during the
    build and must not be re-added without new evidence:
      - cmurtagh 'KATE:' — appears ZERO times across eight of his other files.
        It belonged to one job, not to him. Job vocabulary masquerading as
        engineer vocabulary.
      - kevinw literal values ('C4' = column 4) — a member mark resolves against
        THAT job's schedule and means nothing on the next one. The lexicon
        stores the SLOT, never the value.

    Idempotent — re-runs cleanly, each entry guards on its own natural key.
*/
SET NOCOUNT ON;


/* ---- E1 · kevinw · bare member mark · REPLAY-VERIFIED (the only draftable one) ---- */
IF NOT EXISTS (SELECT 1 FROM markup.Lexicon WHERE Engineer='kevinw' AND Pattern=N'^[A-Z]{1,3}\d{1,3}[A-Z]?( [A-Z.]{2,6})?$')
BEGIN
    INSERT INTO markup.Lexicon (Engineer, DocumentType, Pattern, IsRegex, ExampleMark, Meaning, ActionType, Confidence, Caveat, OccurrenceCount, DistinctFiles, CreatedBy)
    VALUES ('kevinw', '*', N'^[A-Z]{1,3}\d{1,3}[A-Z]?( [A-Z.]{2,6})?$', 1, N'B23 F.B.',
            N'Member mark. Designate the member at the leader target with this tag. The SLOT is vocabulary; the VALUE is job-scoped and resolves against that job''s schedule.',
            'VERIFY', 'replay-verified',
            N'ADD vs VERIFY cannot be distinguished by presence alone — the replay proved the tag is present on the marked sheet, not whether it was added or confirmed.',
            64, 13, N'KOR.Drafter lexicon build 2026-08-01');

    DECLARE @e1 UNIQUEIDENTIFIER = (SELECT Id FROM markup.Lexicon WHERE Engineer='kevinw' AND Pattern=N'^[A-Z]{1,3}\d{1,3}[A-Z]?( [A-Z.]{2,6})?$');
    INSERT INTO markup.LexiconHistory (LexiconId, FromConfidence, ToConfidence, Basis, ChangedBy)
    VALUES (@e1, 'unverified', 'replay-verified',
            N'Sherwood 31207-01, 32-minute markup-to-save gap. B23 F.B. on S2.13 (60 matches), SB1 on S2.08 (14), SB2 (6), SC1 (1) — 11 of 11 present on the sheet each was marked on, 0 absent.',
            N'KOR.Drafter lexicon build 2026-08-01');
END;
GO

/* ---- E3 · jdesroches · design-review question · REFER, permanently ---- */
IF NOT EXISTS (SELECT 1 FROM markup.Lexicon WHERE Engineer='jdesroches' AND Pattern=N'\?\?')
BEGIN
    INSERT INTO markup.Lexicon (Engineer, DocumentType, Pattern, IsRegex, ExampleMark, Meaning, ActionType, Confidence, Caveat, OccurrenceCount, DistinctFiles, CreatedBy)
    VALUES ('jdesroches', '*', N'\?\?', 1, N'should we have a detail??',
            N'Design-review question. Jim surfaces gaps rather than dictating fixes.',
            'REFER', 'engineer-confirmed',
            N'NEVER promote to draftable. A low automation rate here is correct behaviour, not a gap to close.',
            22, 4, N'KOR.Drafter lexicon build 2026-08-01');
END;
GO

/* ---- E2 · jstuart · TAG <mark> — the strongest cross-file vocabulary found ---- */
IF NOT EXISTS (SELECT 1 FROM markup.Lexicon WHERE Engineer='jstuart' AND Pattern=N'^TAG\s+\S+')
BEGIN
    INSERT INTO markup.Lexicon (Engineer, DocumentType, Pattern, IsRegex, ExampleMark, Meaning, ActionType, Confidence, Caveat, OccurrenceCount, DistinctFiles, CreatedBy)
    VALUES ('jstuart', '*', N'^TAG\s+\S+', 1, N'TAG B1',
            N'Place that member tag at the leader target.',
            'ADD', 'unverified',
            N'Untested against an issued model. The explicit verb makes it less ambiguous than E1 — this is the pattern worth quoting to others.',
            68, 5, N'KOR.Drafter lexicon build 2026-08-01');
END;
GO

/* ---- E4 · omara · KOR: decision ---- */
IF NOT EXISTS (SELECT 1 FROM markup.Lexicon WHERE Engineer='omara' AND Pattern=N'^KOR:\s*(.+)')
BEGIN
    INSERT INTO markup.Lexicon (Engineer, DocumentType, Pattern, IsRegex, ExampleMark, Meaning, ActionType, Confidence, Caveat, OccurrenceCount, DistinctFiles, CreatedBy)
    VALUES ('omara', '*', N'^KOR:\s*(.+)', 1, N'KOR: OK follow detail 1/S3.0. If 8" sleeve does not fit, move to W1 wall',
            N'A decision, usually with a detail reference and often an explicit fallback condition.',
            'VERIFY', 'unverified',
            N'The fallback clause must be preserved, not discarded — it is the part that prevents a return query.',
            0, 3, N'KOR.Drafter lexicon build 2026-08-01');
END;
GO

/* ---- E8 · rbeirne · specification with no verb — REFER by design ---- */
IF NOT EXISTS (SELECT 1 FROM markup.Lexicon WHERE Engineer='rbeirne' AND Pattern=N'^(HSS|W\d|L\d|\d+\"\s*min)')
BEGIN
    INSERT INTO markup.Lexicon (Engineer, DocumentType, Pattern, IsRegex, ExampleMark, Meaning, ActionType, Confidence, Caveat, OccurrenceCount, DistinctFiles, CreatedBy)
    VALUES ('rbeirne', '*', N'^(HSS|W\d|L\d|\d+\"\s*min)', 1, N'HSS 4x4x0.188',
            N'A specification for the member at the leader target. The verb is absent.',
            'REFER', 'unverified',
            N'Add-a-note versus change-the-member cannot be determined from the mark. Refer.',
            0, 3, N'KOR.Drafter lexicon build 2026-08-01');
END;
GO

/* ---- E9 · rbeirne · detail reference ---- */
IF NOT EXISTS (SELECT 1 FROM markup.Lexicon WHERE Engineer='rbeirne' AND Pattern=N'\d+/S\d+\.\d+')
BEGIN
    INSERT INTO markup.Lexicon (Engineer, DocumentType, Pattern, IsRegex, ExampleMark, Meaning, ActionType, Confidence, Caveat, OccurrenceCount, DistinctFiles, CreatedBy)
    VALUES ('rbeirne', '*', N'\d+/S\d+\.\d+', 1, N'7/S3.02',
            N'Points at a standard detail.',
            'REFER', 'unverified',
            N'Whether to APPLY the detail or merely cite it is unstated.',
            0, 3, N'KOR.Drafter lexicon build 2026-08-01');
END;
GO

/* ---- E10 · jstuart · existing-framing survey note — NOT an instruction ---- */
IF NOT EXISTS (SELECT 1 FROM markup.Lexicon WHERE Engineer='jstuart' AND Pattern=N'^EXISTING\s+\d')
BEGIN
    INSERT INTO markup.Lexicon (Engineer, DocumentType, Pattern, IsRegex, ExampleMark, Meaning, ActionType, Confidence, Caveat, OccurrenceCount, DistinctFiles, CreatedBy)
    VALUES ('jstuart', '*', N'^EXISTING\s+\d', 1, N'EXISTING 2 x 10 @ 16 o.c.',
            N'Describes existing framing at the leader target — survey information, not a change.',
            'NO-ACTION', 'unverified',
            N'Recording this as NO-ACTION prevents it being mistaken for an instruction to add framing.',
            0, 3, N'KOR.Drafter lexicon build 2026-08-01');
END;
GO

/* ---- E11 · jstuart · hold-down / shear-wall designation ---- */
IF NOT EXISTS (SELECT 1 FROM markup.Lexicon WHERE Engineer='jstuart' AND Pattern=N'^(HD|SW\d+)$')
BEGIN
    INSERT INTO markup.Lexicon (Engineer, DocumentType, Pattern, IsRegex, ExampleMark, Meaning, ActionType, Confidence, Caveat, OccurrenceCount, DistinctFiles, CreatedBy)
    VALUES ('jstuart', '*', N'^(HD|SW\d+)$', 1, N'SW6',
            N'Hold-down or shear-wall designation at the leader target.',
            'VERIFY', 'unverified',
            N'Same ADD-vs-VERIFY ambiguity as E1.',
            0, 3, N'KOR.Drafter lexicon build 2026-08-01');
END;
GO

/* ---- What loaded, and what may actually be drafted on ---- */
SELECT Engineer, Pattern, ActionType, Confidence, OccurrenceCount, DistinctFiles
FROM markup.Lexicon WHERE RetiredAtUtc IS NULL ORDER BY Confidence DESC, Engineer;

SELECT 'DRAFTABLE (everything else is refer-only)' AS Gate, Engineer, Pattern, ActionType
FROM markup.vw_LexiconDraftable;
GO

