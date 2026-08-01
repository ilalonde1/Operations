/*
    Kor.OpportunitiesDb migration 26.
    Building-permits ingestion: leading-indicator construction signal.
    Source #1 = City of Vancouver Open Data (OGL-BC). Idempotent.
*/

-- Permit source registry (parallels NewsFeed)
IF OBJECT_ID(N'opportunities.PermitSource', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PermitSource (
        Id                bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name              nvarchar(120)    NOT NULL,
        Adapter           nvarchar(60)     NOT NULL,         -- 'VancouverOpenData' for now; future: 'BurnabyOpenData', etc.
        Endpoint          nvarchar(800)    NOT NULL,
        Region            nvarchar(40)     NULL,             -- 'CA-BC'
        Municipality      nvarchar(80)     NULL,
        IsActive          bit              NOT NULL DEFAULT 1,
        LastPolledAtUtc   datetimeoffset   NULL,
        LastErrorMessage  nvarchar(1000)   NULL,
        CreatedAtUtc      datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset()
    );
END;
GO

-- Building permits master table
IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BuildingPermit (
        Id                       bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PermitSourceId           bigint           NOT NULL,
        ExternalId               nvarchar(120)    NOT NULL,       -- permit number / system id
        PermitNumber             nvarchar(80)     NULL,           -- display number
        PermitCategory           nvarchar(120)    NULL,           -- 'Building' / 'Demolition' / etc.
        WorkType                 nvarchar(120)    NULL,           -- 'New Building' / 'Alteration' / etc.
        ProjectDescription       nvarchar(max)    NULL,
        EstimatedValue           decimal(19,2)    NULL,
        NumberOfDwellingUnits    int              NULL,
        Address                  nvarchar(300)    NULL,
        City                     nvarchar(100)    NULL,
        PostalCode               nvarchar(20)     NULL,
        GeoLocalArea             nvarchar(120)    NULL,
        Latitude                 decimal(9,6)     NULL,
        Longitude                decimal(9,6)     NULL,
        AppliedDate              date             NULL,
        IssuedDate               date             NULL,
        OwnerName                nvarchar(300)    NULL,
        OwnerCanonicalOrgId      bigint           NULL,
        ApplicantName            nvarchar(300)    NULL,
        ApplicantCanonicalOrgId  bigint           NULL,
        ContractorName           nvarchar(300)    NULL,
        ContractorCanonicalOrgId bigint           NULL,
        SpecificUseCategory      nvarchar(200)    NULL,
        PropertyUse              nvarchar(200)    NULL,
        RawJson                  nvarchar(max)    NULL,
        IngestedAtUtc            datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        CONSTRAINT FK_BuildingPermit_PermitSource FOREIGN KEY (PermitSourceId) REFERENCES opportunities.PermitSource(Id),
        CONSTRAINT FK_BuildingPermit_OwnerCanonical FOREIGN KEY (OwnerCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id),
        CONSTRAINT FK_BuildingPermit_ApplicantCanonical FOREIGN KEY (ApplicantCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id),
        CONSTRAINT FK_BuildingPermit_ContractorCanonical FOREIGN KEY (ContractorCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id)
    );

    CREATE UNIQUE INDEX UX_BuildingPermit_Source_External
        ON opportunities.BuildingPermit (PermitSourceId, ExternalId);

    CREATE INDEX IX_BuildingPermit_IssuedDate ON opportunities.BuildingPermit (IssuedDate DESC);

    CREATE INDEX IX_BuildingPermit_OwnerCanonical
        ON opportunities.BuildingPermit (OwnerCanonicalOrgId)
        WHERE OwnerCanonicalOrgId IS NOT NULL;

    CREATE INDEX IX_BuildingPermit_ApplicantCanonical
        ON opportunities.BuildingPermit (ApplicantCanonicalOrgId)
        WHERE ApplicantCanonicalOrgId IS NOT NULL;

    CREATE INDEX IX_BuildingPermit_ContractorCanonical
        ON opportunities.BuildingPermit (ContractorCanonicalOrgId)
        WHERE ContractorCanonicalOrgId IS NOT NULL;

    CREATE INDEX IX_BuildingPermit_City ON opportunities.BuildingPermit (City, IssuedDate DESC);
END;
GO

-- Seed: City of Vancouver
IF NOT EXISTS (SELECT 1 FROM opportunities.PermitSource WHERE Name = N'City of Vancouver — issued-building-permits')
BEGIN
    INSERT INTO opportunities.PermitSource (Name, Adapter, Endpoint, Region, Municipality)
    VALUES (
        N'City of Vancouver — issued-building-permits',
        'VancouverOpenData',
        'https://opendata.vancouver.ca/api/explore/v2.1/catalog/datasets/issued-building-permits/exports/json?lang=en&timezone=America%2FVancouver',
        'CA-BC',
        'Vancouver'
    );
END;
GO

PRINT 'Migration 26 complete.';
GO
