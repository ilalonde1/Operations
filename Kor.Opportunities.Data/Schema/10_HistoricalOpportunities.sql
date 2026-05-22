/*
    Kor.OpportunitiesDb migration 10.

    Adds a separate datamining pipeline for historical (closed/awarded) RFPs.
    The BC Bid Historical scraper and any future archive-style sources land
    here instead of polluting the active Opportunities pipeline.

    - opportunities.HistoricalOpportunities      -- canonical archived record
    - opportunities.HistoricalOpportunityObservations -- raw ingestion hits
    - opportunities.OpportunitySources.IsHistorical   -- routing flag

    Idempotent. Safe to re-run.
*/

-- 1. Add IsHistorical flag to sources --------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'IsHistorical' AND Object_ID = Object_ID(N'opportunities.OpportunitySources'))
BEGIN
    ALTER TABLE opportunities.OpportunitySources
        ADD IsHistorical bit NOT NULL
            CONSTRAINT DF_Opp_Sources_IsHistorical DEFAULT (0);
END;
GO

-- 2. HistoricalOpportunities table -----------------------------------------------------
IF OBJECT_ID('opportunities.HistoricalOpportunities', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.HistoricalOpportunities
    (
        Id                          bigint              IDENTITY(1,1) NOT NULL,
        OpportunityKey              nvarchar(64)        NOT NULL,
        Name                        nvarchar(400)       NOT NULL,

        BuyerName                   nvarchar(300)       NOT NULL,
        BuyerType                   int                 NOT NULL CONSTRAINT DF_HistOpp_BuyerType DEFAULT (0),

        ProjectAddress              nvarchar(500)       NULL,
        ProjectCity                 nvarchar(150)       NULL,
        ProjectProvince             nvarchar(20)        NULL,
        ProjectPostalCode           nvarchar(20)        NULL,
        ProjectLatitude             decimal(9,6)        NULL,
        ProjectLongitude            decimal(9,6)        NULL,

        Discipline                  int                 NOT NULL CONSTRAINT DF_HistOpp_Discipline DEFAULT (0),
        ConstructionType            nvarchar(100)       NULL,
        ProjectCategory             nvarchar(100)       NULL,

        EstimatedValue              decimal(18,2)       NULL,
        EstimatedValueCurrency      nvarchar(3)         NOT NULL CONSTRAINT DF_HistOpp_Currency DEFAULT ('CAD'),
        RfpReleaseDate              date                NULL,
        SubmissionDeadlineUtc       datetimeoffset(3)   NULL,

        -- Archive-specific
        HistoricalStatus            nvarchar(32)        NULL,  -- 'Closed', 'Cancelled', 'Awarded', etc. (source-supplied)
        IngestedAtUtc               datetimeoffset(3)   NOT NULL CONSTRAINT DF_HistOpp_IngestedAt DEFAULT (sysdatetimeoffset()),

        -- Retrospective scoring (what would our scorer have done?)
        RelevanceScore              decimal(10,4)       NULL,
        RelevanceTier               int                 NULL,

        CreatedAtUtc                datetimeoffset(3)   NOT NULL CONSTRAINT DF_HistOpp_CreatedAt DEFAULT (sysdatetimeoffset()),
        CreatedBy                   nvarchar(150)       NOT NULL CONSTRAINT DF_HistOpp_CreatedBy DEFAULT (suser_sname()),
        UpdatedAtUtc                datetimeoffset(3)   NOT NULL CONSTRAINT DF_HistOpp_UpdatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedBy                   nvarchar(150)       NOT NULL CONSTRAINT DF_HistOpp_UpdatedBy DEFAULT (suser_sname()),
        RowVersion                  rowversion          NOT NULL,

        CONSTRAINT PK_HistOpp PRIMARY KEY (Id),
        CONSTRAINT UQ_HistOpp_Key UNIQUE (OpportunityKey)
    );

    CREATE INDEX IX_HistOpp_Buyer            ON opportunities.HistoricalOpportunities (BuyerName);
    CREATE INDEX IX_HistOpp_Province         ON opportunities.HistoricalOpportunities (ProjectProvince) WHERE ProjectProvince IS NOT NULL;
    CREATE INDEX IX_HistOpp_RfpReleaseDate   ON opportunities.HistoricalOpportunities (RfpReleaseDate DESC) WHERE RfpReleaseDate IS NOT NULL;
    CREATE INDEX IX_HistOpp_HistoricalStatus ON opportunities.HistoricalOpportunities (HistoricalStatus) WHERE HistoricalStatus IS NOT NULL;
END;
GO

-- 3. HistoricalOpportunityObservations table ------------------------------------------
IF OBJECT_ID('opportunities.HistoricalOpportunityObservations', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.HistoricalOpportunityObservations
    (
        Id                          bigint              IDENTITY(1,1) NOT NULL,
        HistoricalOpportunityId     bigint              NULL,
        OpportunitySourceId         uniqueidentifier    NOT NULL,
        Title                       nvarchar(400)       NOT NULL,
        Buyer                       nvarchar(300)       NOT NULL,
        Location                    nvarchar(300)       NULL,
        Url                         nvarchar(2000)      NOT NULL,
        Description                 nvarchar(max)       NULL,
        RawJson                     nvarchar(max)       NULL,
        PostedDateUtc               datetimeoffset(3)   NULL,
        IngestedAtUtc               datetimeoffset(3)   NOT NULL CONSTRAINT DF_HistObs_IngestedAt DEFAULT (sysdatetimeoffset()),
        HashSha256                  varbinary(32)       NOT NULL,
        IsActive                    bit                 NOT NULL CONSTRAINT DF_HistObs_IsActive DEFAULT (1),

        CONSTRAINT PK_HistObs PRIMARY KEY (Id),
        CONSTRAINT FK_HistObs_HistOpp FOREIGN KEY (HistoricalOpportunityId)
            REFERENCES opportunities.HistoricalOpportunities (Id) ON DELETE SET NULL,
        CONSTRAINT FK_HistObs_Source FOREIGN KEY (OpportunitySourceId)
            REFERENCES opportunities.OpportunitySources (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_HistObs_HashSha256       ON opportunities.HistoricalOpportunityObservations (HashSha256);
    CREATE INDEX IX_HistObs_HistOppId               ON opportunities.HistoricalOpportunityObservations (HistoricalOpportunityId) WHERE HistoricalOpportunityId IS NOT NULL;
    CREATE INDEX IX_HistObs_SourceId                ON opportunities.HistoricalOpportunityObservations (OpportunitySourceId);
    CREATE INDEX IX_HistObs_PostedDate              ON opportunities.HistoricalOpportunityObservations (PostedDateUtc DESC);
END;
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON opportunities.HistoricalOpportunities             TO opportunities_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON opportunities.HistoricalOpportunityObservations  TO opportunities_app;
GO

-- 4. Flag BcBidHistorical (and any other archive-style sources) as historical --------
UPDATE opportunities.OpportunitySources
SET    IsHistorical = 1
WHERE  Name = 'BcBidHistorical' AND IsHistorical = 0;
GO

-- 5. Move BCBIDHIS-* rows from Opportunities → HistoricalOpportunities ---------------
--    Idempotent: skips keys already in HistoricalOpportunities.
;WITH source AS (
    SELECT o.*
    FROM opportunities.Opportunities o
    WHERE o.OpportunityKey LIKE 'BCBIDHIS%'
      AND NOT EXISTS (
          SELECT 1 FROM opportunities.HistoricalOpportunities h
          WHERE h.OpportunityKey = o.OpportunityKey
      )
)
INSERT INTO opportunities.HistoricalOpportunities
    (OpportunityKey, Name, BuyerName, BuyerType,
     ProjectAddress, ProjectCity, ProjectProvince, ProjectPostalCode, ProjectLatitude, ProjectLongitude,
     Discipline, ConstructionType, ProjectCategory,
     EstimatedValue, EstimatedValueCurrency, RfpReleaseDate, SubmissionDeadlineUtc,
     HistoricalStatus, IngestedAtUtc,
     RelevanceScore, RelevanceTier,
     CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy)
SELECT
    s.OpportunityKey, s.Name, s.BuyerName, s.BuyerType,
    s.ProjectAddress, s.ProjectCity, s.ProjectProvince, s.ProjectPostalCode, s.ProjectLatitude, s.ProjectLongitude,
    s.Discipline, s.ConstructionType, s.ProjectCategory,
    s.EstimatedValue, s.EstimatedValueCurrency, s.RfpReleaseDate, s.SubmissionDeadlineUtc,
    NULL,                          -- HistoricalStatus: backfilled from RawJson below
    s.IdentifiedAtUtc,             -- map IdentifiedAt → IngestedAt
    s.RelevanceScore, s.RelevanceTier,
    s.CreatedAtUtc, s.CreatedBy, s.UpdatedAtUtc, s.UpdatedBy
FROM source s;
GO

-- 6. Backfill HistoricalStatus from RawJson on the moved rows -------------------------
--    RawJson is prefixed "status=<value>|..." by BcBidHistoricalScraper.
;WITH ranked_obs AS (
    SELECT obs.RawJson, o.Id AS HistId,
           ROW_NUMBER() OVER (PARTITION BY o.Id ORDER BY obs.IngestedAtUtc ASC) AS rn
    FROM opportunities.OpportunityObservations obs
    JOIN opportunities.Opportunities oOld     ON oOld.Id = obs.OpportunityId
    JOIN opportunities.HistoricalOpportunities o ON o.OpportunityKey = oOld.OpportunityKey
    WHERE oOld.OpportunityKey LIKE 'BCBIDHIS%' AND obs.RawJson LIKE 'status=%'
)
UPDATE h
SET    HistoricalStatus = LEFT(SUBSTRING(r.RawJson, 8, 32),
                              CASE WHEN CHARINDEX('|', SUBSTRING(r.RawJson, 8, 32)) > 0
                                   THEN CHARINDEX('|', SUBSTRING(r.RawJson, 8, 32)) - 1
                                   ELSE 32 END)
FROM   opportunities.HistoricalOpportunities h
JOIN   ranked_obs r ON r.HistId = h.Id AND r.rn = 1
WHERE  h.HistoricalStatus IS NULL;
GO

-- 7. Move observations: copy to HistoricalOpportunityObservations, then delete --------
;WITH src AS (
    SELECT obs.*, h.Id AS NewOpportunityId
    FROM opportunities.OpportunityObservations obs
    JOIN opportunities.Opportunities oOld       ON oOld.Id = obs.OpportunityId
    JOIN opportunities.HistoricalOpportunities h ON h.OpportunityKey = oOld.OpportunityKey
    WHERE oOld.OpportunityKey LIKE 'BCBIDHIS%'
      AND NOT EXISTS (
          SELECT 1 FROM opportunities.HistoricalOpportunityObservations hobs
          WHERE hobs.HashSha256 = obs.HashSha256
      )
)
INSERT INTO opportunities.HistoricalOpportunityObservations
    (HistoricalOpportunityId, OpportunitySourceId,
     Title, Buyer, Location, Url, Description, RawJson, PostedDateUtc,
     IngestedAtUtc, HashSha256, IsActive)
SELECT
    src.NewOpportunityId, src.OpportunitySourceId,
    src.Title, src.Buyer, src.Location, src.Url, src.Description, src.RawJson, src.PostedDateUtc,
    src.IngestedAtUtc, src.HashSha256, src.IsActive
FROM src;
GO

-- 8. Delete the moved observations from the active table ------------------------------
DELETE obs
FROM   opportunities.OpportunityObservations obs
JOIN   opportunities.Opportunities oOld ON oOld.Id = obs.OpportunityId
WHERE  oOld.OpportunityKey LIKE 'BCBIDHIS%';
GO

-- 9. Delete the moved opportunities from the active table -----------------------------
DELETE FROM opportunities.Opportunities WHERE OpportunityKey LIKE 'BCBIDHIS%';
GO

PRINT 'Migration 10 complete.';
GO
