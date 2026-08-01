/*
    Kor.OpportunitiesDb migration 18.
    Adds VendorSiteCrawl table + extraction columns on OpportunityAwards. Idempotent.
    Stage 1 (7a) writes to VendorSiteCrawl. Stage 2 (7b) writes to the OpportunityAwards columns.
*/

-- Crawl table: one row per unique vendor website
IF OBJECT_ID(N'opportunities.VendorSiteCrawl', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.VendorSiteCrawl (
        Id                bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        VendorWebsite     nvarchar(500)    NOT NULL,
        Status            nvarchar(20)     NOT NULL,           -- pending | ok | failed | blocked | no_robots
        CrawledAtUtc      datetimeoffset   NULL,               -- last successful crawl
        LastAttemptAtUtc  datetimeoffset   NULL,
        Attempts          int              NOT NULL DEFAULT 0,
        ErrorMessage      nvarchar(2000)   NULL,
        RawCapture        nvarchar(max)    NULL,               -- JSON: {pages:[{url,title,text}], discovered:[urls]}
        ExtractedAtUtc    datetimeoffset   NULL,               -- when 7b extraction succeeded
        CreatedAtUtc      datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset()
    );

    CREATE UNIQUE INDEX UX_VendorSiteCrawl_VendorWebsite
        ON opportunities.VendorSiteCrawl (VendorWebsite);

    CREATE INDEX IX_VendorSiteCrawl_Status_LastAttempt
        ON opportunities.VendorSiteCrawl (Status, LastAttemptAtUtc);
END;
GO

-- Extraction columns on OpportunityAwards (filled by 7b; nullable for now)
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'AgentVendorPortfolio'
                 AND Object_ID = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AgentVendorPortfolio         nvarchar(max)   NULL, -- JSON array of {project_name,client,location,year,value,summary}
            AgentVendorSpecificServices  nvarchar(max)   NULL, -- JSON array of strings (narrow niches)
            AgentVendorSectorFocus       nvarchar(max)   NULL, -- JSON array of strings (healthcare/education/etc.)
            AgentVendorOpenPositions     nvarchar(max)   NULL, -- JSON array of {title,location,discipline}
            AgentVendorLeadershipDetail  nvarchar(max)   NULL, -- JSON array of {name,title,background,p_eng,joined_year}
            AgentVendorBondingCapacity   nvarchar(300)   NULL,
            AgentVendorTagline           nvarchar(300)   NULL,
            AgentSiteCrawledAtUtc        datetimeoffset  NULL;
END;
GO

PRINT 'Migration 18 complete.';
GO
