/*
    KorOpportunitiesDb migration 362: the BD consigliere — asking Claude from the Director, and doctrine that learns.

    Ian, 16 October 2026: "make a wise consigliere based on whichever role… make it a learning app by accepting and
    ingesting and resynthesizing", and "we already ask claude code from within the app and provide it context in the
    network ops app. Investigate it before building this." This is that method (NetworkOps 007/008/012) for BD:

      opportunities.BdPromptRun     one Claude run started from the Director: an ask (a question about today, a person or a
                                    pursuit) or a synthesis (turn the books into doctrine for a role). The prompt is composed
                                    from live data when opened and never stored; its SHA-256 is. The session reports back with
                                    a one-time token (only its SHA-256 is stored): outcome, the answer, proposed cards.
      opportunities.DoctrineCard    one principle and its play, cited to the books: title, plain-English line (what accepting
                                    it does, read first), where it applies (a closed vocabulary, BdDoctrine.AppliesTo),
                                    principle, how to recognise the situation, the play, the warning sign, citations.
                                    proposed -> accepted | rejected by Ian; only accepted cards reach later prompts. Nothing
                                    edits a card's text: a correction AMENDS it and, accepted, retires the one it amends.

    The session runs as Claude Code on Ian's PC under his subscription (CLAUDE.md rule 14: no API call anywhere). Nothing here
    sends anything to anyone. Creates are guarded; no data is changed.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'opportunities.BdPromptRun', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdPromptRun (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BdPromptRun PRIMARY KEY CLUSTERED,
        Kind NVARCHAR(16) NOT NULL CONSTRAINT CK_BdPromptRun_Kind CHECK (Kind IN (N'ask', N'synthesis')),
        Role NVARCHAR(20) NOT NULL CONSTRAINT DF_BdPromptRun_Role DEFAULT N'BD' CONSTRAINT CK_BdPromptRun_Role CHECK (Role IN (N'BD', N'Operations')),
        Subject NVARCHAR(200) NOT NULL,
        Question NVARCHAR(2000) NULL,
        CreatedBy NVARCHAR(150) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdPromptRun_Created DEFAULT sysdatetimeoffset(),
        PromptSha256 BINARY(32) NOT NULL,
        TokenSha256 BINARY(32) NOT NULL,
        OutcomeAtUtc DATETIMEOFFSET NULL,
        Outcome NVARCHAR(16) NULL CONSTRAINT CK_BdPromptRun_Outcome CHECK (Outcome IN (N'answered', N'partly', N'not-answered')),
        Answer NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_BdPromptRun_Created ON opportunities.BdPromptRun (CreatedAtUtc DESC);
END
GO

IF OBJECT_ID(N'opportunities.DoctrineCard', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.DoctrineCard (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DoctrineCard PRIMARY KEY CLUSTERED,
        Role NVARCHAR(20) NOT NULL CONSTRAINT CK_DoctrineCard_Role CHECK (Role IN (N'BD', N'Operations')),
        Title NVARCHAR(200) NOT NULL,
        Plain NVARCHAR(1000) NOT NULL,
        AppliesTo NVARCHAR(400) NOT NULL,
        Principle NVARCHAR(2000) NOT NULL,
        Recognise NVARCHAR(2000) NOT NULL,
        Play NVARCHAR(2000) NOT NULL,
        Warning NVARCHAR(2000) NULL,
        Citations NVARCHAR(2000) NOT NULL,
        Tags NVARCHAR(400) NULL,
        SourceRunId BIGINT NULL CONSTRAINT FK_DoctrineCard_Run REFERENCES opportunities.BdPromptRun (Id),
        AmendsCardId BIGINT NULL CONSTRAINT FK_DoctrineCard_Amends REFERENCES opportunities.DoctrineCard (Id),
        Status NVARCHAR(16) NOT NULL CONSTRAINT DF_DoctrineCard_Status DEFAULT N'proposed'
            CONSTRAINT CK_DoctrineCard_Status CHECK (Status IN (N'proposed', N'accepted', N'rejected', N'retired')),
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_DoctrineCard_Created DEFAULT sysdatetimeoffset(),
        DecidedAtUtc DATETIMEOFFSET NULL,
        DecidedBy NVARCHAR(150) NULL
    );
    CREATE INDEX IX_DoctrineCard_Status ON opportunities.DoctrineCard (Role, Status, CreatedAtUtc DESC);
    CREATE INDEX IX_DoctrineCard_Run ON opportunities.DoctrineCard (SourceRunId);
END
GO

SELECT (SELECT COUNT(*) FROM opportunities.BdPromptRun) AS prompt_runs, (SELECT COUNT(*) FROM opportunities.DoctrineCard) AS doctrine_cards;
GO
