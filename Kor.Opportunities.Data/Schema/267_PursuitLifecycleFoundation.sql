/*
    Kor.OpportunitiesDb migration 267.

    Phase-1a BD pursuit lifecycle foundation. Additive schema only:
    outcome/action/source fields on CrmEngagements, stage history, and an
    assignment audit log that intentionally has no foreign keys so it survives
    source row deletion.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'WonLostOutcome') IS NULL
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD WonLostOutcome INT NULL;
END;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'OutcomeReason') IS NULL
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD OutcomeReason NVARCHAR(2000) NULL;
END;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'LostToCanonicalOrgId') IS NULL
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD LostToCanonicalOrgId BIGINT NULL;
END;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'WonProjectWbs1') IS NULL
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD WonProjectWbs1 NVARCHAR(50) NULL;
END;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'NextActionDueUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD NextActionDueUtc DATETIMEOFFSET NULL;
END;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'NextActionNote') IS NULL
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD NextActionNote NVARCHAR(500) NULL;
END;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'ExternalSource') IS NULL
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD ExternalSource NVARCHAR(50) NULL;
END;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'ExternalSourceKey') IS NULL
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD ExternalSourceKey NVARCHAR(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'opportunities.CrmEngagements')
      AND name = N'CK_CrmEngagements_WonLostOutcome')
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD CONSTRAINT CK_CrmEngagements_WonLostOutcome
        CHECK (WonLostOutcome IN (1,2,3,4) OR WonLostOutcome IS NULL);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'opportunities.CrmEngagements')
      AND name = N'FK_CrmEngagements_LostToCanonical')
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD CONSTRAINT FK_CrmEngagements_LostToCanonical
        FOREIGN KEY (LostToCanonicalOrgId) REFERENCES opportunities.CanonicalOrg (Id);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.CrmEngagements')
      AND name = N'IX_CrmEngagements_LostToCanonical')
BEGIN
    CREATE INDEX IX_CrmEngagements_LostToCanonical
        ON opportunities.CrmEngagements (LostToCanonicalOrgId)
        WHERE LostToCanonicalOrgId IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.CrmEngagements')
      AND name = N'UX_CrmEngagements_ExternalKey')
BEGIN
    CREATE UNIQUE INDEX UX_CrmEngagements_ExternalKey
        ON opportunities.CrmEngagements (ExternalSource, ExternalSourceKey)
        WHERE ExternalSource IS NOT NULL AND ExternalSourceKey IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.CrmEngagements')
      AND name = N'IX_CrmEngagements_NextActionDue')
BEGIN
    CREATE INDEX IX_CrmEngagements_NextActionDue
        ON opportunities.CrmEngagements (NextActionDueUtc)
        WHERE NextActionDueUtc IS NOT NULL;
END;
GO

IF OBJECT_ID(N'opportunities.CrmEngagementStageHistory', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.CrmEngagementStageHistory
    (
        Id             BIGINT IDENTITY(1,1) NOT NULL,
        EngagementId   BIGINT               NOT NULL,
        Stage          INT                  NOT NULL,
        EnteredAtUtc   DATETIMEOFFSET       NOT NULL CONSTRAINT DF_CrmEngStageHist_Entered DEFAULT sysdatetimeoffset(),
        ByStaffId      NVARCHAR(20)         NULL,
        CONSTRAINT PK_CrmEngagementStageHistory PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CrmEngStageHist_Engagement FOREIGN KEY (EngagementId)
            REFERENCES opportunities.CrmEngagements (Id) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.CrmEngagementStageHistory')
      AND name = N'IX_CrmEngStageHist_Engagement')
BEGIN
    CREATE INDEX IX_CrmEngStageHist_Engagement
        ON opportunities.CrmEngagementStageHistory (EngagementId, EnteredAtUtc);
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAssignmentLog', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.OpportunityAssignmentLog
    (
        Id             BIGINT IDENTITY(1,1) NOT NULL,
        OpportunityId  BIGINT               NULL,
        EngagementId   BIGINT               NULL,
        Action         NVARCHAR(20)         NOT NULL,
        FromStaffId    NVARCHAR(20)         NULL,
        ToStaffId      NVARCHAR(20)         NULL,
        ByStaffId      NVARCHAR(20)         NULL,
        AtUtc          DATETIMEOFFSET       NOT NULL CONSTRAINT DF_OppAssignLog_At DEFAULT sysdatetimeoffset(),
        Reason         NVARCHAR(500)        NULL,
        CONSTRAINT PK_OpportunityAssignmentLog PRIMARY KEY CLUSTERED (Id)
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAssignmentLog')
      AND name = N'IX_OppAssignLog_Opportunity')
BEGIN
    CREATE INDEX IX_OppAssignLog_Opportunity
        ON opportunities.OpportunityAssignmentLog (OpportunityId);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAssignmentLog')
      AND name = N'IX_OppAssignLog_Engagement')
BEGIN
    CREATE INDEX IX_OppAssignLog_Engagement
        ON opportunities.OpportunityAssignmentLog (EngagementId);
END;
GO

PRINT 'Migration 267 complete.';
GO
