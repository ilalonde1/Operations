/*
    Kor.OpportunitiesDb migration 64.

    Relational intel tables for normalized query access over the JSON blobs in
    opportunities.CanonicalOrgEnrichment. Raw enrichment stays archived in the
    source enrichment row; these entities are the searchable, deduplicated view.
*/
IF OBJECT_ID(N'opportunities.IntelPerson', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelPerson (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL, -- 'High'/'Medium'/'Low'
        NaturalKey               NVARCHAR(64)     NOT NULL, -- SHA1 hex, 40 chars
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        DisplayName              NVARCHAR(200)    NOT NULL,
        NormalizedName           NVARCHAR(200)    NOT NULL,
        Email                    NVARCHAR(200)    NULL,
        Phone                    NVARCHAR(50)     NULL,
        LinkedinUrl              NVARCHAR(500)    NULL,
        Notes                    NVARCHAR(MAX)    NULL,
        Corroborations           INT              NOT NULL DEFAULT 1,

        CONSTRAINT UQ_IntelPerson_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelPerson_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.CanonicalOrgEnrichment(Id)
    );

    CREATE INDEX IX_IntelPerson_NormalizedName
        ON opportunities.IntelPerson (NormalizedName);

    CREATE INDEX IX_IntelPerson_LastSeen
        ON opportunities.IntelPerson (LastSeenAtUtc DESC)
        WHERE RetiredAtUtc IS NULL;
END;
GO

IF OBJECT_ID(N'opportunities.IntelPersonAffiliation', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelPersonAffiliation (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL, -- 'High'/'Medium'/'Low'
        NaturalKey               NVARCHAR(64)     NOT NULL, -- SHA1 hex, 40 chars
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        IntelPersonId            BIGINT           NOT NULL,
        CanonicalOrgId           BIGINT           NOT NULL,
        Title                    NVARCHAR(200)    NULL,
        Department               NVARCHAR(200)    NULL,
        IsCurrent                BIT              NOT NULL DEFAULT 1,
        StartDateApprox          NVARCHAR(50)     NULL,
        EndDateApprox            NVARCHAR(50)     NULL,
        Notes                    NVARCHAR(MAX)    NULL,

        CONSTRAINT UQ_IntelPersonAffiliation_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelPersonAffiliation_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.CanonicalOrgEnrichment(Id),

        CONSTRAINT FK_IntelPersonAffiliation_IntelPerson
            FOREIGN KEY (IntelPersonId)
            REFERENCES opportunities.IntelPerson(Id)
            ON DELETE CASCADE,

        CONSTRAINT FK_IntelPersonAffiliation_CanonicalOrg
            FOREIGN KEY (CanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
    );

    CREATE INDEX IX_IntelPersonAffiliation_CanonicalOrgId
        ON opportunities.IntelPersonAffiliation (CanonicalOrgId, IsCurrent);

    CREATE INDEX IX_IntelPersonAffiliation_Person
        ON opportunities.IntelPersonAffiliation (IntelPersonId);
END;
GO

IF OBJECT_ID(N'opportunities.IntelSignal', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelSignal (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL, -- 'High'/'Medium'/'Low'
        NaturalKey               NVARCHAR(64)     NOT NULL, -- SHA1 hex, 40 chars
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        CanonicalOrgId           BIGINT           NOT NULL,
        SignalType               NVARCHAR(50)     NOT NULL, -- 'LeadershipChange'/'HiringSurge'/'OfficeMove'/'OwnershipMnA'/'CapacityStrain'/'RecentWin'/'Other'
        Subject                  NVARCHAR(500)    NOT NULL,
        Detail                   NVARCHAR(MAX)    NULL,
        OccurredAtApprox         NVARCHAR(50)     NULL,
        SourceUrl                NVARCHAR(1000)   NULL,
        Corroborations           INT              NOT NULL DEFAULT 1,

        CONSTRAINT UQ_IntelSignal_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelSignal_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.CanonicalOrgEnrichment(Id),

        CONSTRAINT FK_IntelSignal_CanonicalOrg
            FOREIGN KEY (CanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
    );

    CREATE INDEX IX_IntelSignal_CanonicalOrgId
        ON opportunities.IntelSignal (CanonicalOrgId, LastSeenAtUtc DESC)
        WHERE RetiredAtUtc IS NULL;

    CREATE INDEX IX_IntelSignal_Type
        ON opportunities.IntelSignal (SignalType, LastSeenAtUtc DESC)
        WHERE RetiredAtUtc IS NULL;
END;
GO

IF OBJECT_ID(N'opportunities.IntelAction', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelAction (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL, -- 'High'/'Medium'/'Low'
        NaturalKey               NVARCHAR(64)     NOT NULL, -- SHA1 hex, 40 chars
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        CanonicalOrgId           BIGINT           NOT NULL,
        ActionType               NVARCHAR(50)     NOT NULL, -- 'ContactStrategy'/'PursuitAngle'/'TimingWindow'/'HowToGetOnRoster'/'KorDisplacementRead'/'Other'
        Recommendation           NVARCHAR(MAX)    NOT NULL,
        TargetPersonName         NVARCHAR(200)    NULL,
        TimingNotes              NVARCHAR(500)    NULL,
        Status                   NVARCHAR(20)     NOT NULL DEFAULT N'Open', -- 'Open'/'Done'/'Dismissed'
        StatusChangedByUser      NVARCHAR(100)    NULL,
        StatusChangedAtUtc       DATETIMEOFFSET   NULL,

        CONSTRAINT UQ_IntelAction_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelAction_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.CanonicalOrgEnrichment(Id),

        CONSTRAINT FK_IntelAction_CanonicalOrg
            FOREIGN KEY (CanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
    );

    CREATE INDEX IX_IntelAction_CanonicalOrgId
        ON opportunities.IntelAction (CanonicalOrgId, Status)
        WHERE RetiredAtUtc IS NULL;
END;
GO

IF OBJECT_ID(N'opportunities.IntelWork', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelWork (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL, -- 'High'/'Medium'/'Low'
        NaturalKey               NVARCHAR(64)     NOT NULL, -- SHA1 hex, 40 chars
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        CanonicalOrgId           BIGINT           NOT NULL,
        ProjectName              NVARCHAR(500)    NOT NULL,
        NormalizedProjectName    NVARCHAR(500)    NOT NULL,
        Role                     NVARCHAR(100)    NULL,
        YearApprox               NVARCHAR(20)     NULL,
        EstimatedValueCad        DECIMAL(18,2)    NULL,
        EstimatedValueText       NVARCHAR(100)    NULL,
        Notes                    NVARCHAR(MAX)    NULL,
        MajorProjectsInventoryId BIGINT           NULL,

        CONSTRAINT UQ_IntelWork_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelWork_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.CanonicalOrgEnrichment(Id),

        CONSTRAINT FK_IntelWork_CanonicalOrg
            FOREIGN KEY (CanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id),

        CONSTRAINT FK_IntelWork_MajorProjectsInventory
            FOREIGN KEY (MajorProjectsInventoryId)
            REFERENCES opportunities.MajorProjectsInventory(Id)
    );

    CREATE INDEX IX_IntelWork_CanonicalOrgId
        ON opportunities.IntelWork (CanonicalOrgId);

    CREATE INDEX IX_IntelWork_NormalizedName
        ON opportunities.IntelWork (NormalizedProjectName);

    CREATE INDEX IX_IntelWork_Mpi
        ON opportunities.IntelWork (MajorProjectsInventoryId)
        WHERE MajorProjectsInventoryId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'opportunities.IntelRisk', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelRisk (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL, -- 'High'/'Medium'/'Low'
        NaturalKey               NVARCHAR(64)     NOT NULL, -- SHA1 hex, 40 chars
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        CanonicalOrgId           BIGINT           NOT NULL,
        RiskType                 NVARCHAR(50)     NOT NULL, -- 'CapacityStrain'/'KeyPersonDependency'/'OwnershipUncertainty'/'ExploitableWeakness'/'DataIssue'/'Other'
        Description              NVARCHAR(MAX)    NOT NULL,
        MitigationNotes          NVARCHAR(MAX)    NULL,

        CONSTRAINT UQ_IntelRisk_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelRisk_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.CanonicalOrgEnrichment(Id),

        CONSTRAINT FK_IntelRisk_CanonicalOrg
            FOREIGN KEY (CanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
    );

    CREATE INDEX IX_IntelRisk_CanonicalOrgId
        ON opportunities.IntelRisk (CanonicalOrgId, RiskType)
        WHERE RetiredAtUtc IS NULL;
END;
GO

IF OBJECT_ID(N'opportunities.IntelNarrative', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelNarrative (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SourceProviderName       NVARCHAR(100)    NOT NULL,
        SourceEnrichmentId       BIGINT           NOT NULL,
        SourceConfidence         NVARCHAR(20)     NOT NULL, -- 'High'/'Medium'/'Low'
        NaturalKey               NVARCHAR(64)     NOT NULL, -- SHA1 hex, 40 chars
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL,
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL,
        RetiredAtUtc             DATETIMEOFFSET   NULL,
        RetiredReason            NVARCHAR(200)    NULL,
        CreatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        CanonicalOrgId           BIGINT           NOT NULL,
        NarrativeType            NVARCHAR(50)     NOT NULL, -- 'Current'/'History'/'Action'/'Summary'
        ParagraphText            NVARCHAR(MAX)    NOT NULL,

        CONSTRAINT UQ_IntelNarrative_NaturalKey
            UNIQUE (NaturalKey),

        CONSTRAINT FK_IntelNarrative_SourceEnrichment
            FOREIGN KEY (SourceEnrichmentId)
            REFERENCES opportunities.CanonicalOrgEnrichment(Id),

        CONSTRAINT FK_IntelNarrative_CanonicalOrg
            FOREIGN KEY (CanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
    );

    CREATE INDEX IX_IntelNarrative_CanonicalOrgId
        ON opportunities.IntelNarrative (CanonicalOrgId, NarrativeType);
END;
GO

PRINT 'Migration 64 complete.';
GO
