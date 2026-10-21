/*
    KorOpportunitiesDb migration 377: Claude's reading of a pursuit's RFP, kept on the pursuit (21 October 2026).

    When a grabbed tender's documents arrive (the APC and Bids&Tenders download jobs), the app on a PC with Claude Code opens
    a review session on them -- the BD Director's local-Claude method: a prompt composed from live data, a one-time token,
    and a report back through tools/BdKnowledgeIngest. No model is called from the app or the server.

    1. opportunities.BdPromptRun gains Kind 'rfp' and EngagementId, so a review run belongs to its pursuit and the app can
       tell "Claude is reading it" from "not started".
    2. opportunities.PursuitReview keeps what the session reported, one row per run: the plain summary, the recommendation
       and why, and the questions for the buyer (JSON: question, why, ref). A later run adds a row; nothing is overwritten
       except by a resend of the same run. The checklist goes into opportunities.PursuitRequirement as before.

    Idempotent; GO-batched.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BdPromptRun_Kind' AND definition NOT LIKE N'%rfp%')
    ALTER TABLE opportunities.BdPromptRun DROP CONSTRAINT CK_BdPromptRun_Kind;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BdPromptRun_Kind')
    ALTER TABLE opportunities.BdPromptRun WITH CHECK ADD CONSTRAINT CK_BdPromptRun_Kind CHECK (Kind IN (N'ask', N'synthesis', N'rfp'));
GO

IF COL_LENGTH(N'opportunities.BdPromptRun', N'EngagementId') IS NULL
    ALTER TABLE opportunities.BdPromptRun ADD EngagementId BIGINT NULL
        CONSTRAINT FK_BdPromptRun_Engagement REFERENCES opportunities.CrmEngagements (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BdPromptRun_Engagement' AND object_id = OBJECT_ID(N'opportunities.BdPromptRun'))
    CREATE INDEX IX_BdPromptRun_Engagement ON opportunities.BdPromptRun (EngagementId, CreatedAtUtc DESC) WHERE EngagementId IS NOT NULL;
GO

IF OBJECT_ID(N'opportunities.PursuitReview', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PursuitReview (
        Id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_PursuitReview PRIMARY KEY CLUSTERED,
        EngagementId BIGINT NOT NULL CONSTRAINT FK_PursuitReview_Engagement REFERENCES opportunities.CrmEngagements (Id),
        PromptRunId BIGINT NULL CONSTRAINT FK_PursuitReview_Run REFERENCES opportunities.BdPromptRun (Id),
        Recommendation NVARCHAR(16) NOT NULL CONSTRAINT CK_PursuitReview_Recommendation CHECK (Recommendation IN (N'go', N'no-go', N'decide')),
        Summary NVARCHAR(MAX) NOT NULL,
        Reasons NVARCHAR(MAX) NOT NULL,
        QuestionsJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PursuitReview_Questions DEFAULT N'[]',
        QuestionsDeadline NVARCHAR(100) NULL,
        QuestionsTo NVARCHAR(320) NULL,
        FilesRead NVARCHAR(2000) NULL,
        ChecklistNote NVARCHAR(400) NULL,
        ReportedBy NVARCHAR(150) NOT NULL,
        ReportedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_PursuitReview_At DEFAULT SYSDATETIMEOFFSET()
    );
    CREATE UNIQUE INDEX UX_PursuitReview_Run ON opportunities.PursuitReview (PromptRunId) WHERE PromptRunId IS NOT NULL;
    CREATE INDEX IX_PursuitReview_Engagement ON opportunities.PursuitReview (EngagementId, ReportedAtUtc DESC);
END
GO
