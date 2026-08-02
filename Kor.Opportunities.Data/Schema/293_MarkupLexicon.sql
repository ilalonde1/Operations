/*
    markup.Lexicon — the engineer markup lexicon.

    WHY THIS IS A TABLE AND NOT A FILE
    Engineers reuse the same marks constantly: one writes `C4` 56 times in a
    single file, another prefixes every mark `KATE:`, another `KOR:`. If a mark
    means the same thing every time it should be resolved ONCE, verified, and
    reused — which makes this reference data that is read at run time, updated
    continuously, and must never fork. A markdown file in a repo would fragment
    the moment a second machine or a second person touched it.

    THE SAFETY PROPERTY, ENFORCED BY DATA NOT CONVENTION
    Confidence gates use. An 'unverified' entry is an observation and may only
    be used to REFER a mark to the engineer; it may never be drafted on. Only
    'replay-verified' (the interpretation matched what was actually issued in a
    scored historical replay) or 'engineer-confirmed' (the engineer said so)
    may drive drafting. CK_Lexicon_Confidence and the vw_LexiconDraftable view
    make that structural rather than a habit.

    THE COMPOUNDING PROPERTY
    When an engineer answers a referral, the answer is inserted here. The same
    question is therefore never asked twice — the referral list shrinks with
    use. That is the whole point of the design.

    Dialect varies by DOCUMENT TYPE as well as by person (the same engineer
    writes long directives on a stickfile back-check and bare member tags on an
    IFC markup), so DocumentType is part of the natural key.

    Idempotent and self-healing — re-runs cleanly, each block guards its own
    object.
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'markup')
BEGIN
    EXEC ('CREATE SCHEMA markup AUTHORIZATION dbo;');
END;
GO

IF OBJECT_ID('markup.Lexicon', 'U') IS NULL
BEGIN
    CREATE TABLE markup.Lexicon
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Lexicon_Id DEFAULT (NEWSEQUENTIALID()),
        Engineer        NVARCHAR(64)     NOT NULL,   -- AD sAMAccountName, e.g. kevinw
        DocumentType    NVARCHAR(64)     NOT NULL CONSTRAINT DF_Lexicon_DocType DEFAULT ('*'),
        Pattern         NVARCHAR(400)    NOT NULL,   -- regex or literal
        IsRegex         BIT              NOT NULL CONSTRAINT DF_Lexicon_IsRegex DEFAULT (1),
        ExampleMark     NVARCHAR(400)    NULL,
        Meaning         NVARCHAR(1000)   NOT NULL,
        ActionType      NVARCHAR(32)     NOT NULL,   -- ADD | CHANGE | VERIFY | REFER | NO-ACTION
        Confidence      NVARCHAR(32)     NOT NULL CONSTRAINT DF_Lexicon_Confidence DEFAULT ('unverified'),
        Caveat          NVARCHAR(1000)   NULL,
        OccurrenceCount INT              NOT NULL CONSTRAINT DF_Lexicon_Occ DEFAULT (0),
        DistinctFiles   INT              NOT NULL CONSTRAINT DF_Lexicon_Files DEFAULT (0),
        CreatedBy       NVARCHAR(150)    NOT NULL,
        CreatedAtUtc    DATETIMEOFFSET   NOT NULL CONSTRAINT DF_Lexicon_Created DEFAULT (sysdatetimeoffset()),
        UpdatedAtUtc    DATETIMEOFFSET   NOT NULL CONSTRAINT DF_Lexicon_Updated DEFAULT (sysdatetimeoffset()),
        RetiredAtUtc    DATETIMEOFFSET   NULL,       -- soft delete; entries are never hard-deleted
        RetiredReason   NVARCHAR(400)    NULL,
        CONSTRAINT PK_Lexicon PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_Lexicon_Confidence CHECK (Confidence IN
            ('unverified', 'replay-verified', 'engineer-confirmed', 'rejected')),
        CONSTRAINT CK_Lexicon_ActionType CHECK (ActionType IN
            ('ADD', 'CHANGE', 'VERIFY', 'REFER', 'NO-ACTION'))
    );
END;
GO

-- One live entry per engineer + document type + pattern. Retired rows are
-- excluded so a corrected entry can supersede a withdrawn one.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Lexicon_Natural' AND object_id = OBJECT_ID('markup.Lexicon'))
BEGIN
    CREATE UNIQUE INDEX UX_Lexicon_Natural
        ON markup.Lexicon (Engineer, DocumentType, Pattern)
        WHERE RetiredAtUtc IS NULL;
END;
GO

/*
    Evidence — every real occurrence backing an entry. An entry without
    evidence is a guess with better formatting, so this is where an auditor
    goes to check that a pattern was actually observed rather than imagined.
*/
IF OBJECT_ID('markup.LexiconEvidence', 'U') IS NULL
BEGIN
    CREATE TABLE markup.LexiconEvidence
    (
        Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_LexEv_Id DEFAULT (NEWSEQUENTIALID()),
        LexiconId     UNIQUEIDENTIFIER NOT NULL,
        SourcePath    NVARCHAR(500)    NOT NULL,   -- the markup PDF
        PageNumber    INT              NULL,
        MarkText      NVARCHAR(1000)   NULL,
        JobNumber     NVARCHAR(32)     NULL,
        ObservedAtUtc DATETIMEOFFSET   NOT NULL CONSTRAINT DF_LexEv_Observed DEFAULT (sysdatetimeoffset()),
        CONSTRAINT PK_LexiconEvidence PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_LexiconEvidence_Lexicon FOREIGN KEY (LexiconId)
            REFERENCES markup.Lexicon (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_LexiconEvidence_Lexicon ON markup.LexiconEvidence (LexiconId);
END;
GO

/*
    History — every confidence change, with who and why. Promotion from
    'unverified' to something draftable is the moment risk enters the system,
    so it is the moment that must be auditable.
*/
IF OBJECT_ID('markup.LexiconHistory', 'U') IS NULL
BEGIN
    CREATE TABLE markup.LexiconHistory
    (
        Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_LexHist_Id DEFAULT (NEWSEQUENTIALID()),
        LexiconId      UNIQUEIDENTIFIER NOT NULL,
        FromConfidence NVARCHAR(32)     NULL,
        ToConfidence   NVARCHAR(32)     NOT NULL,
        Basis          NVARCHAR(1000)   NOT NULL,   -- e.g. "Sherwood 31207-01 replay, item C19, HIT"
        ChangedBy      NVARCHAR(150)    NOT NULL,
        ChangedAtUtc   DATETIMEOFFSET   NOT NULL CONSTRAINT DF_LexHist_Changed DEFAULT (sysdatetimeoffset()),
        CONSTRAINT PK_LexiconHistory PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_LexiconHistory_Lexicon FOREIGN KEY (LexiconId)
            REFERENCES markup.Lexicon (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_LexiconHistory_Lexicon ON markup.LexiconHistory (LexiconId, ChangedAtUtc DESC);
END;
GO

/*
    THE GATE. Anything that drafts reads THIS view, never the base table.
    Unverified and rejected entries are structurally unreachable to a drafting
    consumer — they can still be read from Lexicon directly for the purpose of
    REFERRING a mark, which is what they are for.
*/
IF OBJECT_ID('markup.vw_LexiconDraftable', 'V') IS NOT NULL
    DROP VIEW markup.vw_LexiconDraftable;
GO
CREATE VIEW markup.vw_LexiconDraftable
AS
    SELECT Id, Engineer, DocumentType, Pattern, IsRegex, ExampleMark,
           Meaning, ActionType, Confidence, Caveat, OccurrenceCount, DistinctFiles
    FROM markup.Lexicon
    WHERE RetiredAtUtc IS NULL
      AND Confidence IN ('replay-verified', 'engineer-confirmed')
      AND ActionType <> 'REFER';
GO

/*
    Seed: the one pattern that must exist from day one and must NEVER become
    draftable. Jim's design-review questions are supposed to come back to a
    human; an automation that "improved" them into instructions would be a
    defect. Recorded as engineer-confirmed REFER so it is permanent and
    deliberate, not an accident of nobody having got to it.
*/
IF NOT EXISTS (SELECT 1 FROM markup.Lexicon WHERE Engineer = 'jdesroches' AND Pattern = '\?\?\s*$')
BEGIN
    INSERT INTO markup.Lexicon
        (Engineer, DocumentType, Pattern, IsRegex, ExampleMark, Meaning, ActionType, Confidence, Caveat, CreatedBy)
    VALUES
        ('jdesroches', '*', '\?\?\s*$', 1, 'should we have a detail??',
         'Design-review question. Jim surfaces gaps rather than dictating fixes; these are meant to return to a human.',
         'REFER', 'engineer-confirmed',
         'NEVER promote to draftable. A low automation rate here is correct behaviour, not a gap to close.',
         'KOR.Drafter dialect review 2026-08-01');
END;
GO
