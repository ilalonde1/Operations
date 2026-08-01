-- =============================================================================
-- KOR Opportunities — initial schema for the BD opportunity-pipeline module.
--
-- Database: KorOpportunitiesDb (KOR-APP01\SQLEXPRESS) — separate from KorTransmittals.
-- Idempotent: every CREATE / ALTER is guarded so repeat runs are safe.
--
-- Architecture: the Kor.Opportunities.Worker Windows Service writes ingestion
-- observations + heartbeat rows here; the Opportunities feature window inside
-- Kor.Operations.App reads from and writes to the same tables. No HTTP API;
-- this DB is the IPC channel. Mirrors the Kor.Operations.FileSync pattern.
--
-- Plan: ~/.claude/plans/kor-opportunities-architecture-plan.md
-- Source matrix: ~/.claude/plans/kor-opportunities-source-matrix.md
-- =============================================================================

-- -----------------------------------------------------------------------------
-- One-time setup the operator runs BEFORE this script:
--   1. CREATE DATABASE KorOpportunitiesDb;
--   2. CREATE LOGIN opportunities_app WITH PASSWORD = '<set in App.config>';
--   3. CREATE USER  opportunities_app FOR LOGIN opportunities_app;
--   4. ALTER ROLE   db_datareader  ADD MEMBER opportunities_app;
--   5. ALTER ROLE   db_datawriter  ADD MEMBER opportunities_app;
--   6. GRANT EXECUTE ON SCHEMA::opportunities TO opportunities_app;  (after this script)
-- -----------------------------------------------------------------------------

-- -----------------------------------------------------------------------------
-- Schema
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'opportunities')
BEGIN
    EXEC('CREATE SCHEMA opportunities AUTHORIZATION dbo;');
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.OpportunitySources -- one row per ingestion provider configuration.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'OpportunitySources')
BEGIN
    CREATE TABLE opportunities.OpportunitySources
    (
        Id                      uniqueidentifier    NOT NULL CONSTRAINT DF_Opp_Sources_Id DEFAULT (NEWID()),
        Name                    nvarchar(200)       NOT NULL,
        SourceType              int                 NOT NULL,
        BaseUrl                 nvarchar(2000)      NOT NULL,
        IsEnabled               bit                 NOT NULL CONSTRAINT DF_Opp_Sources_IsEnabled DEFAULT (1),
        CrawlDelaySeconds       int                 NOT NULL CONSTRAINT DF_Opp_Sources_CrawlDelay DEFAULT (1800),
        RequestTimeoutSeconds   int                 NOT NULL CONSTRAINT DF_Opp_Sources_ReqTimeout DEFAULT (30),
        CreatedAtUtc            datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Sources_CreatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedAtUtc            datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Sources_UpdatedAt DEFAULT (sysdatetimeoffset()),
        CONSTRAINT PK_Opp_Sources PRIMARY KEY (Id),
        CONSTRAINT UQ_Opp_Sources_Name UNIQUE (Name)
    );
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.OpportunitySourceMappings -- per-source key/value JSON config.
-- Replaces Contract Radar's lazy `AppSettings` raw-SQL pattern; this is a
-- real table created from day one, not bypassed by migrations.
-- Keys: 'JsonMapping', 'CsvMapping', 'Headers', 'CivicConfig', 'GraphEmailConfig'.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'OpportunitySourceMappings')
BEGIN
    CREATE TABLE opportunities.OpportunitySourceMappings
    (
        OpportunitySourceId     uniqueidentifier    NOT NULL,
        [Key]                   varchar(64)         NOT NULL,
        ValueJson               nvarchar(max)       NULL,
        UpdatedAtUtc            datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_SrcMap_UpdatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedBy               nvarchar(128)       NOT NULL CONSTRAINT DF_Opp_SrcMap_UpdatedBy DEFAULT (suser_sname()),
        CONSTRAINT PK_Opp_SrcMap PRIMARY KEY (OpportunitySourceId, [Key]),
        CONSTRAINT FK_Opp_SrcMap_Sources FOREIGN KEY (OpportunitySourceId)
            REFERENCES opportunities.OpportunitySources (Id) ON DELETE CASCADE
    );
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.Opportunities -- canonical record (one per real-world RFP).
-- Many OpportunityObservations may link to one Opportunity (auto-merged or manual).
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'Opportunities')
BEGIN
    CREATE TABLE opportunities.Opportunities
    (
        Id                          bigint              IDENTITY(1,1) NOT NULL,
        OpportunityKey              nvarchar(64)        NOT NULL,
        Name                        nvarchar(400)       NOT NULL,

        -- Buyer / issuer ----------------------------------------------------------------
        BuyerName                   nvarchar(300)       NOT NULL,
        BuyerType                   int                 NOT NULL CONSTRAINT DF_Opp_Opp_BuyerType DEFAULT (0), -- 0=Unknown
        DeltekClientId              nvarchar(50)        NULL,

        -- Project location --------------------------------------------------------------
        ProjectAddress              nvarchar(500)       NULL,
        ProjectCity                 nvarchar(150)       NULL,
        ProjectProvince             nvarchar(20)        NULL,
        ProjectPostalCode           nvarchar(20)        NULL,
        ProjectLatitude             decimal(9,6)        NULL,
        ProjectLongitude            decimal(9,6)        NULL,

        -- Discipline classification -----------------------------------------------------
        Discipline                  int                 NOT NULL CONSTRAINT DF_Opp_Opp_Discipline DEFAULT (0), -- 0=Unknown
        ConstructionType            nvarchar(100)       NULL,
        ProjectCategory             nvarchar(100)       NULL,

        -- Money + timeline --------------------------------------------------------------
        EstimatedValue              decimal(18,2)       NULL,
        EstimatedValueCurrency      nvarchar(3)         NOT NULL CONSTRAINT DF_Opp_Opp_Currency DEFAULT ('CAD'),
        RfpReleaseDate              date                NULL,
        SubmissionDeadlineUtc       datetimeoffset(3)   NULL,

        -- Pursuit lifecycle -------------------------------------------------------------
        Status                      int                 NOT NULL CONSTRAINT DF_Opp_Opp_Status DEFAULT (1), -- 1=Identified
        IdentifiedAtUtc             datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Opp_IdentifiedAt DEFAULT (sysdatetimeoffset()),
        ReviewingSinceUtc           datetimeoffset(3)   NULL,
        QualifiedAtUtc              datetimeoffset(3)   NULL,
        PursuingSinceUtc            datetimeoffset(3)   NULL,
        ProposalSubmittedAtUtc      datetimeoffset(3)   NULL,
        OutcomeAtUtc                datetimeoffset(3)   NULL,

        -- Assignment --------------------------------------------------------------------
        OwnerStaffId                nvarchar(20)        NULL,
        ReviewerStaffIds            nvarchar(500)       NULL,

        -- Scoring (refreshed on insert/update) ------------------------------------------
        RelevanceScore              decimal(10,4)       NULL,
        RelevanceTier               int                 NULL, -- 0=HardReject, 1=Low, 2=Medium, 3=High

        -- Outcome -----------------------------------------------------------------------
        WonLostOutcome              int                 NULL, -- 1=Won, 2=Lost, 3=NoBid, 4=Withdrawn
        OutcomeReason               nvarchar(2000)      NULL,
        WonProjectWbs1              nvarchar(50)        NULL,
        AwardedValue                decimal(18,2)       NULL,

        -- Audit -------------------------------------------------------------------------
        CreatedAtUtc                datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Opp_CreatedAt DEFAULT (sysdatetimeoffset()),
        CreatedBy                   nvarchar(150)       NOT NULL CONSTRAINT DF_Opp_Opp_CreatedBy DEFAULT (suser_sname()),
        UpdatedAtUtc                datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Opp_UpdatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedBy                   nvarchar(150)       NOT NULL CONSTRAINT DF_Opp_Opp_UpdatedBy DEFAULT (suser_sname()),

        -- Optimistic concurrency token --------------------------------------------------
        RowVersion                  rowversion          NOT NULL,

        CONSTRAINT PK_Opp_Opp PRIMARY KEY (Id),
        CONSTRAINT UQ_Opp_Opp_Key UNIQUE (OpportunityKey)
    );

    CREATE INDEX IX_Opp_Opp_Status                    ON opportunities.Opportunities (Status);
    CREATE INDEX IX_Opp_Opp_RelevanceTier_Deadline    ON opportunities.Opportunities (RelevanceTier, SubmissionDeadlineUtc);
    CREATE INDEX IX_Opp_Opp_DeltekClientId            ON opportunities.Opportunities (DeltekClientId) WHERE DeltekClientId IS NOT NULL;
    CREATE INDEX IX_Opp_Opp_OwnerStaffId              ON opportunities.Opportunities (OwnerStaffId)   WHERE OwnerStaffId   IS NOT NULL;
    CREATE INDEX IX_Opp_Opp_UpdatedAt                 ON opportunities.Opportunities (UpdatedAtUtc DESC);
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.OpportunityObservations -- one row per ingestion hit.
-- Many observations may link to one canonical Opportunity, or none yet.
-- Dedup via SHA-256 hash of (title|buyer|location|url) UPPERCASE pipe-separated.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'OpportunityObservations')
BEGIN
    CREATE TABLE opportunities.OpportunityObservations
    (
        Id                          bigint              IDENTITY(1,1) NOT NULL,
        OpportunityId               bigint              NULL,
        OpportunitySourceId         uniqueidentifier    NOT NULL,
        Title                       nvarchar(400)       NOT NULL,
        Buyer                       nvarchar(300)       NOT NULL,
        Location                    nvarchar(300)       NULL,
        Url                         nvarchar(2000)      NOT NULL,
        Description                 nvarchar(max)       NULL,
        RawJson                     nvarchar(max)       NULL,
        PostedDateUtc               datetimeoffset(3)   NULL,
        IngestedAtUtc               datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Obs_IngestedAt DEFAULT (sysdatetimeoffset()),
        HashSha256                  varbinary(32)       NOT NULL,
        IsActive                    bit                 NOT NULL CONSTRAINT DF_Opp_Obs_IsActive DEFAULT (1),

        CONSTRAINT PK_Opp_Obs PRIMARY KEY (Id),
        CONSTRAINT FK_Opp_Obs_Opportunity FOREIGN KEY (OpportunityId)
            REFERENCES opportunities.Opportunities (Id) ON DELETE SET NULL,
        CONSTRAINT FK_Opp_Obs_Source FOREIGN KEY (OpportunitySourceId)
            REFERENCES opportunities.OpportunitySources (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_Opp_Obs_HashSha256        ON opportunities.OpportunityObservations (HashSha256);
    CREATE INDEX IX_Opp_Obs_OpportunityId            ON opportunities.OpportunityObservations (OpportunityId) WHERE OpportunityId IS NOT NULL;
    CREATE INDEX IX_Opp_Obs_SourceId                 ON opportunities.OpportunityObservations (OpportunitySourceId);
    CREATE INDEX IX_Opp_Obs_PostedDate               ON opportunities.OpportunityObservations (PostedDateUtc DESC);
    CREATE INDEX IX_Opp_Obs_IsActive_Ingested        ON opportunities.OpportunityObservations (IsActive, IngestedAtUtc DESC);
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.OpportunityNotes -- free-text notes timeline per opportunity.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'OpportunityNotes')
BEGIN
    CREATE TABLE opportunities.OpportunityNotes
    (
        Id                  bigint              IDENTITY(1,1) NOT NULL,
        OpportunityId       bigint              NOT NULL,
        AuthorStaffId       nvarchar(20)        NULL,
        AuthorDisplay       nvarchar(150)       NOT NULL,
        CreatedAtUtc        datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Notes_CreatedAt DEFAULT (sysdatetimeoffset()),
        Body                nvarchar(max)       NOT NULL,

        CONSTRAINT PK_Opp_Notes PRIMARY KEY (Id),
        CONSTRAINT FK_Opp_Notes_Opportunity FOREIGN KEY (OpportunityId)
            REFERENCES opportunities.Opportunities (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_Opp_Notes_OpportunityId_CreatedAt
        ON opportunities.OpportunityNotes (OpportunityId, CreatedAtUtc DESC);
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.OpportunityFiles -- attachments (RFP PDF, response drafts, etc.).
-- Two storage paths: SharePoint (via IGraphFacade) or local working file.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'OpportunityFiles')
BEGIN
    CREATE TABLE opportunities.OpportunityFiles
    (
        Id                  bigint              IDENTITY(1,1) NOT NULL,
        OpportunityId       bigint              NOT NULL,
        FileName            nvarchar(500)       NOT NULL,
        SharePointItemId    nvarchar(200)       NULL,
        LocalPath           nvarchar(2000)      NULL,
        Sha256              varbinary(32)       NULL,
        SizeBytes           bigint              NULL,
        UploadedAtUtc       datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Files_UploadedAt DEFAULT (sysdatetimeoffset()),
        UploadedBy          nvarchar(150)       NOT NULL CONSTRAINT DF_Opp_Files_UploadedBy DEFAULT (suser_sname()),

        CONSTRAINT PK_Opp_Files PRIMARY KEY (Id),
        CONSTRAINT FK_Opp_Files_Opportunity FOREIGN KEY (OpportunityId)
            REFERENCES opportunities.Opportunities (Id) ON DELETE CASCADE,
        CONSTRAINT CK_Opp_Files_OneStorage CHECK (
            (SharePointItemId IS NOT NULL) OR (LocalPath IS NOT NULL)
        )
    );

    CREATE INDEX IX_Opp_Files_OpportunityId
        ON opportunities.OpportunityFiles (OpportunityId);
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.OpportunityFeeProposalLinks -- bridge to FeeProposal.
-- The FeeProposal table itself lives in KorTransmittalsDb (different DB);
-- we don't enforce a cross-DB FK, just store the GUID for navigation.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'OpportunityFeeProposalLinks')
BEGIN
    CREATE TABLE opportunities.OpportunityFeeProposalLinks
    (
        OpportunityId       bigint              NOT NULL,
        FeeProposalId       uniqueidentifier    NOT NULL,
        LinkedAtUtc         datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_FpLnk_LinkedAt DEFAULT (sysdatetimeoffset()),
        LinkedBy            nvarchar(150)       NOT NULL CONSTRAINT DF_Opp_FpLnk_LinkedBy DEFAULT (suser_sname()),

        CONSTRAINT PK_Opp_FpLnk PRIMARY KEY (OpportunityId, FeeProposalId),
        CONSTRAINT FK_Opp_FpLnk_Opportunity FOREIGN KEY (OpportunityId)
            REFERENCES opportunities.Opportunities (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_Opp_FpLnk_FeeProposalId
        ON opportunities.OpportunityFeeProposalLinks (FeeProposalId);
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.IngestionRuns -- one row per (provider, run-attempt). Observability.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'IngestionRuns')
BEGIN
    CREATE TABLE opportunities.IngestionRuns
    (
        Id                  uniqueidentifier    NOT NULL CONSTRAINT DF_Opp_Runs_Id DEFAULT (NEWID()),
        StartedAtUtc        datetimeoffset(3)   NOT NULL,
        EndedAtUtc          datetimeoffset(3)   NULL,
        ProviderName        nvarchar(200)       NOT NULL,
        Success             bit                 NOT NULL CONSTRAINT DF_Opp_Runs_Success DEFAULT (0),
        InsertedCount       int                 NOT NULL CONSTRAINT DF_Opp_Runs_Inserted DEFAULT (0),
        DuplicateCount      int                 NOT NULL CONSTRAINT DF_Opp_Runs_Duplicate DEFAULT (0),
        SkippedCount        int                 NOT NULL CONSTRAINT DF_Opp_Runs_Skipped DEFAULT (0),
        FailedCount         int                 NOT NULL CONSTRAINT DF_Opp_Runs_Failed DEFAULT (0),
        ErrorSummary        nvarchar(2000)      NULL,
        HostInstance        nvarchar(200)       NULL,
        CorrelationId       nvarchar(64)        NULL,

        CONSTRAINT PK_Opp_Runs PRIMARY KEY (Id)
    );

    CREATE INDEX IX_Opp_Runs_Provider_Started   ON opportunities.IngestionRuns (ProviderName, StartedAtUtc DESC);
    CREATE INDEX IX_Opp_Runs_Success_Started    ON opportunities.IngestionRuns (Success, StartedAtUtc DESC);
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.IngestionTriggers -- WPF-side "run now" requests.
-- Worker polls this table every 30s; on hit it dispatches the named provider
-- and writes a row to IngestionRuns. Mirrors FileSync.TriggerPoller pattern.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'IngestionTriggers')
BEGIN
    CREATE TABLE opportunities.IngestionTriggers
    (
        Id                      bigint              IDENTITY(1,1) NOT NULL,
        ProviderName            nvarchar(200)       NOT NULL,
        OpportunitySourceId     uniqueidentifier    NULL,
        RequestedAtUtc          datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Trg_RequestedAt DEFAULT (sysdatetimeoffset()),
        RequestedBy             nvarchar(150)       NOT NULL CONSTRAINT DF_Opp_Trg_RequestedBy DEFAULT (suser_sname()),
        ProcessedAtUtc          datetimeoffset(3)   NULL,
        ResultRunId             uniqueidentifier    NULL,
        Status                  varchar(16)         NOT NULL CONSTRAINT DF_Opp_Trg_Status DEFAULT ('Pending'),

        CONSTRAINT PK_Opp_Trg PRIMARY KEY (Id),
        CONSTRAINT CK_Opp_Trg_Status CHECK (Status IN ('Pending', 'Processing', 'Completed', 'Failed'))
    );

    CREATE INDEX IX_Opp_Trg_Pending ON opportunities.IngestionTriggers (Status, RequestedAtUtc) WHERE Status = 'Pending';
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.ScoringProfile -- singleton config row.
-- ProfileKey = 'Default' for the active profile; future per-discipline profiles
-- can use 'Structural' / 'Inspections' etc. See plan §6 for the JSON schema.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'ScoringProfile')
BEGIN
    CREATE TABLE opportunities.ScoringProfile
    (
        ProfileKey          varchar(64)         NOT NULL,
        ValueJson           nvarchar(max)       NOT NULL,
        UpdatedAtUtc        datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Score_UpdatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedBy           nvarchar(150)       NOT NULL CONSTRAINT DF_Opp_Score_UpdatedBy DEFAULT (suser_sname()),

        CONSTRAINT PK_Opp_Score PRIMARY KEY (ProfileKey)
    );
END;
GO

-- -----------------------------------------------------------------------------
-- opportunities.ServiceHeartbeat -- Worker liveness signal.
-- Written every HeartbeatSeconds (default 60s). The WPF admin tab reads this
-- to display "Worker last seen X minutes ago, version Y".
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'opportunities' AND t.name = 'ServiceHeartbeat')
BEGIN
    CREATE TABLE opportunities.ServiceHeartbeat
    (
        ServiceName             nvarchar(64)        NOT NULL,
        MachineName             nvarchar(128)       NOT NULL,
        Version                 nvarchar(32)        NULL,
        LastBeatUtc             datetimeoffset(3)   NOT NULL CONSTRAINT DF_Opp_Hb_LastBeat DEFAULT (sysdatetimeoffset()),
        LastIngestionEndedUtc   datetimeoffset(3)   NULL,

        CONSTRAINT PK_Opp_Hb PRIMARY KEY (ServiceName, MachineName)
    );
END;
GO

PRINT 'Schema script complete.';
