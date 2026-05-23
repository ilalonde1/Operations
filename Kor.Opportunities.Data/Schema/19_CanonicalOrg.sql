/*
    Kor.OpportunitiesDb migration 19.
    Canonical Org master + Alias mapping tables. Foundation for cross-linking
    Opportunities (buyers/vendors/awards) and Deltek (Clendor.ClientID).
    Idempotent.
*/

-- Canonical org master: one row per distinct organization (KOR client, vendor, buyer, etc.)
IF OBJECT_ID(N'opportunities.CanonicalOrg', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.CanonicalOrg (
        Id              bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Kind            nvarchar(40)     NOT NULL, -- 'KorClient' | 'Vendor' | 'Buyer' | 'Architect' | 'GC' | 'Subcontractor' | 'Unknown'
        DisplayName     nvarchar(300)    NOT NULL,
        ClendorClientId varchar(32)      NULL,     -- Deltek Clendor.ClientID when this org also exists in Deltek
        Website         nvarchar(500)    NULL,
        Notes           nvarchar(max)    NULL,
        CreatedAtUtc    datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc    datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset()
    );

    CREATE INDEX IX_CanonicalOrg_Clendor
        ON opportunities.CanonicalOrg (ClendorClientId)
        WHERE ClendorClientId IS NOT NULL;

    CREATE INDEX IX_CanonicalOrg_Kind_DisplayName
        ON opportunities.CanonicalOrg (Kind, DisplayName);
END;
GO

-- Alias: every raw name string we've seen, mapped to a CanonicalOrg
IF OBJECT_ID(N'opportunities.OrgAlias', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.OrgAlias (
        Id               bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CanonicalOrgId   bigint           NULL,     -- NULL until classified
        RawName          nvarchar(300)    NOT NULL,
        Source           nvarchar(80)     NOT NULL, -- 'OpportunityAwards.AwardedTo' | 'OpportunityAwards.Awarding' | 'Opportunities.Buyer' | 'VendorSiteCrawl' | 'Clendor.Name' | 'Manual'
        Confidence       int              NOT NULL DEFAULT 0, -- 0-100; 100 = human-verified
        ClassifiedBy     nvarchar(50)     NULL,     -- 'auto-fuzzy' | 'auto-claude' | 'manual' | NULL
        ClassifiedAtUtc  datetimeoffset   NULL,
        Notes            nvarchar(max)    NULL,
        CreatedAtUtc     datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        CONSTRAINT FK_OrgAlias_CanonicalOrg
            FOREIGN KEY (CanonicalOrgId) REFERENCES opportunities.CanonicalOrg (Id)
    );

    CREATE UNIQUE INDEX UX_OrgAlias_RawName_Source
        ON opportunities.OrgAlias (RawName, Source);

    CREATE INDEX IX_OrgAlias_CanonicalOrgId
        ON opportunities.OrgAlias (CanonicalOrgId)
        WHERE CanonicalOrgId IS NOT NULL;

    CREATE INDEX IX_OrgAlias_Unclassified
        ON opportunities.OrgAlias (Source, CreatedAtUtc)
        WHERE CanonicalOrgId IS NULL;
END;
GO

PRINT 'Migration 19 complete.';
GO
