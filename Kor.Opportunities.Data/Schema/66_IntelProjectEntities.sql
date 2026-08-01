/*
    Kor.OpportunitiesDb migration 66.

    Project-level enrichment + relational intel tables keyed to
    opportunities.MajorProjectsInventory.Id. Mirrors the org-side
    CanonicalOrgEnrichment + Intel* pattern for project brief research.
*/
IF OBJECT_ID(N'opportunities.MajorProjectEnrichment', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.MajorProjectEnrichment (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        MajorProjectsInventoryId BIGINT           NOT NULL,
        ProviderName             NVARCHAR(60)     NOT NULL,
        Status                   NVARCHAR(20)     NOT NULL,
        LastRefreshAtUtc         DATETIMEOFFSET   NULL,
        LastAttemptAtUtc         DATETIMEOFFSET   NULL,
        NextRefreshAtUtc         DATETIMEOFFSET   NULL,
        Attempts                 INT              NOT NULL DEFAULT 0,
        ErrorMessage             NVARCHAR(2000)   NULL,
        ResultJson               NVARCHAR(MAX)    NULL,
        Notes                    NVARCHAR(MAX)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),

        CONSTRAINT FK_MajorProjectEnrichment_MajorProjectsInventory
            FOREIGN KEY (MajorProjectsInventoryId)
            REFERENCES opportunities.MajorProjectsInventory(Id)
    );

    CREATE UNIQUE INDEX UQ_MajorProjectEnrichment_ProjectProvider
        ON opportunities.MajorProjectEnrichment (MajorProjectsInventoryId, ProviderName);

    CREATE INDEX IX_MajorProjectEnrichment_ProviderNextRefresh
        ON opportunities.MajorProjectEnrichment (ProviderName, NextRefreshAtUtc)
        WHERE Status IN ('pending', 'ok', 'failed');
END;
GO

IF OBJECT_ID(N'opportunities.IntelProject', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelProject (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL,
        NaturalKey               NVARCHAR(64)     NOT NULL,
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        MajorProjectsInventoryId BIGINT           NOT NULL,
        Description              NVARCHAR(MAX)    NULL,
        Schedule                 NVARCHAR(MAX)    NULL,
        Status                   NVARCHAR(MAX)    NULL,
        KorAngle                 NVARCHAR(MAX)    NULL,
        Corroborations           INT              NOT NULL DEFAULT 1,

        CONSTRAINT UQ_IntelProject_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelProject_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.MajorProjectEnrichment(Id),

        CONSTRAINT FK_IntelProject_MajorProjectsInventory
            FOREIGN KEY (MajorProjectsInventoryId)
            REFERENCES opportunities.MajorProjectsInventory(Id)
    );

    CREATE UNIQUE INDEX UQ_IntelProject_ProjectProvider_Live
        ON opportunities.IntelProject (MajorProjectsInventoryId, SourceProviderName)
        WHERE RetiredAtUtc IS NULL;
END;
GO

IF OBJECT_ID(N'opportunities.IntelProjectSignal', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelProjectSignal (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL,
        NaturalKey               NVARCHAR(64)     NOT NULL,
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        MajorProjectsInventoryId BIGINT           NOT NULL,
        SignalType               NVARCHAR(50)     NOT NULL,
        Subject                  NVARCHAR(500)    NOT NULL,
        Detail                   NVARCHAR(MAX)    NULL,
        OccurredAtApprox         NVARCHAR(50)     NULL,
        SourceUrl                NVARCHAR(1000)   NULL,
        Corroborations           INT              NOT NULL DEFAULT 1,

        CONSTRAINT UQ_IntelProjectSignal_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelProjectSignal_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.MajorProjectEnrichment(Id),

        CONSTRAINT FK_IntelProjectSignal_MajorProjectsInventory
            FOREIGN KEY (MajorProjectsInventoryId)
            REFERENCES opportunities.MajorProjectsInventory(Id)
    );

    CREATE INDEX IX_IntelProjectSignal_ProjectLastSeen
        ON opportunities.IntelProjectSignal (MajorProjectsInventoryId, LastSeenAtUtc DESC)
        WHERE RetiredAtUtc IS NULL;
END;
GO

IF OBJECT_ID(N'opportunities.IntelProjectAction', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelProjectAction (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL,
        NaturalKey               NVARCHAR(64)     NOT NULL,
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        MajorProjectsInventoryId BIGINT           NOT NULL,
        ActionType               NVARCHAR(50)     NOT NULL,
        Recommendation           NVARCHAR(MAX)    NOT NULL,
        TargetPersonName         NVARCHAR(200)    NULL,
        TargetCanonicalOrgId     BIGINT           NULL,
        TimingNotes              NVARCHAR(500)    NULL,
        Status                   NVARCHAR(20)     NOT NULL DEFAULT N'Open',
        StatusChangedByUser      NVARCHAR(100)    NULL,
        StatusChangedAtUtc       DATETIMEOFFSET   NULL,
        Corroborations           INT              NOT NULL DEFAULT 1,

        CONSTRAINT UQ_IntelProjectAction_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelProjectAction_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.MajorProjectEnrichment(Id),

        CONSTRAINT FK_IntelProjectAction_MajorProjectsInventory
            FOREIGN KEY (MajorProjectsInventoryId)
            REFERENCES opportunities.MajorProjectsInventory(Id),

        CONSTRAINT FK_IntelProjectAction_TargetCanonicalOrg
            FOREIGN KEY (TargetCanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
    );

    CREATE INDEX IX_IntelProjectAction_ProjectStatus
        ON opportunities.IntelProjectAction (MajorProjectsInventoryId, Status)
        WHERE RetiredAtUtc IS NULL;
END;
GO

IF OBJECT_ID(N'opportunities.IntelProjectRisk', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelProjectRisk (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL,
        NaturalKey               NVARCHAR(64)     NOT NULL,
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        MajorProjectsInventoryId BIGINT           NOT NULL,
        RiskType                 NVARCHAR(50)     NOT NULL,
        Description              NVARCHAR(MAX)    NOT NULL,
        MitigationNotes          NVARCHAR(MAX)    NULL,
        Corroborations           INT              NOT NULL DEFAULT 1,

        CONSTRAINT UQ_IntelProjectRisk_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelProjectRisk_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.MajorProjectEnrichment(Id),

        CONSTRAINT FK_IntelProjectRisk_MajorProjectsInventory
            FOREIGN KEY (MajorProjectsInventoryId)
            REFERENCES opportunities.MajorProjectsInventory(Id)
    );
END;
GO

IF OBJECT_ID(N'opportunities.IntelProjectKeyPerson', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelProjectKeyPerson (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL,
        NaturalKey               NVARCHAR(64)     NOT NULL,
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        MajorProjectsInventoryId BIGINT           NOT NULL,
        DisplayName              NVARCHAR(200)    NOT NULL,
        NormalizedName           NVARCHAR(200)    NOT NULL,
        Title                    NVARCHAR(200)    NULL,
        Side                     NVARCHAR(20)     NOT NULL,
        CanonicalOrgId           BIGINT           NULL,
        Corroborations           INT              NOT NULL DEFAULT 1,

        CONSTRAINT UQ_IntelProjectKeyPerson_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelProjectKeyPerson_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.MajorProjectEnrichment(Id),

        CONSTRAINT FK_IntelProjectKeyPerson_MajorProjectsInventory
            FOREIGN KEY (MajorProjectsInventoryId)
            REFERENCES opportunities.MajorProjectsInventory(Id),

        CONSTRAINT FK_IntelProjectKeyPerson_CanonicalOrg
            FOREIGN KEY (CanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
    );

    CREATE INDEX IX_IntelProjectKeyPerson_ProjectSide
        ON opportunities.IntelProjectKeyPerson (MajorProjectsInventoryId, Side)
        WHERE RetiredAtUtc IS NULL;

    CREATE INDEX IX_IntelProjectKeyPerson_NormalizedName
        ON opportunities.IntelProjectKeyPerson (NormalizedName);
END;
GO

PRINT 'Migration 66 complete.';
GO
