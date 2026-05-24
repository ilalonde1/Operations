/*
    Kor.OpportunitiesDb migration 29.
    Defensive schema verifier for migrations 12-18 and 25-26.
    Repairs missing sibling columns, indexes, and foreign keys after partial deployments.
*/

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorProfile')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorProfile nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentContractContext')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentContractContext nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentCompetesWithKor')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentCompetesWithKor bit NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentCompetitionNotes')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentCompetitionNotes nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentSourceUrls')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentSourceUrls nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentEnrichedAtUtc')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentEnrichedAtUtc datetimeoffset(3) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentEnrichmentAttempts')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AgentEnrichmentAttempts int NOT NULL CONSTRAINT DF_OppAwards_AgentAttempts DEFAULT (0);
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentLastError')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentLastError nvarchar(2000) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentLastAttemptAtUtc')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentLastAttemptAtUtc datetimeoffset(3) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OppAwards_PendingAgent' AND object_id = OBJECT_ID(N'opportunities.OpportunityAwards'))
BEGIN
    CREATE INDEX IX_OppAwards_PendingAgent
        ON opportunities.OpportunityAwards (ContractValue DESC, Id)
        INCLUDE (AgentEnrichmentAttempts)
        WHERE AgentEnrichedAtUtc IS NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OppAwards_CompetesWithKor' AND object_id = OBJECT_ID(N'opportunities.OpportunityAwards'))
BEGIN
    CREATE INDEX IX_OppAwards_CompetesWithKor
        ON opportunities.OpportunityAwards (AgentCompetesWithKor)
        WHERE AgentCompetesWithKor = 1;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorWebsite')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorWebsite nvarchar(500) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorHqLocation')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorHqLocation nvarchar(200) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorSizeBand')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorSizeBand nvarchar(20) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorFoundedYear')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorFoundedYear int NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorSpecialties')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorSpecialties nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorLeadership')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorLeadership nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorOwnershipStatus')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorOwnershipStatus nvarchar(50) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorParentCompany')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorParentCompany nvarchar(300) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorLocations')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorLocations nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorCertifications')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorCertifications nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorRecentNews')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorRecentNews nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorLinkedInUrl')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorLinkedInUrl nvarchar(500) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentKorOverlapScore')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentKorOverlapScore tinyint NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentContractProjectType')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentContractProjectType nvarchar(80) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.VendorSiteCrawl', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_VendorSiteCrawl_VendorWebsite' AND object_id = OBJECT_ID(N'opportunities.VendorSiteCrawl'))
BEGIN
    CREATE UNIQUE INDEX UX_VendorSiteCrawl_VendorWebsite
        ON opportunities.VendorSiteCrawl (VendorWebsite);
END;
GO

IF OBJECT_ID(N'opportunities.VendorSiteCrawl', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VendorSiteCrawl_Status_LastAttempt' AND object_id = OBJECT_ID(N'opportunities.VendorSiteCrawl'))
BEGIN
    CREATE INDEX IX_VendorSiteCrawl_Status_LastAttempt
        ON opportunities.VendorSiteCrawl (Status, LastAttemptAtUtc);
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorPortfolio')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorPortfolio nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorSpecificServices')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorSpecificServices nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorSectorFocus')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorSectorFocus nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorOpenPositions')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorOpenPositions nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorLeadershipDetail')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorLeadershipDetail nvarchar(max) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorBondingCapacity')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorBondingCapacity nvarchar(300) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentVendorTagline')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentVendorTagline nvarchar(300) NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityAwards', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.OpportunityAwards') AND name = N'AgentSiteCrawledAtUtc')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards ADD AgentSiteCrawledAtUtc datetimeoffset NULL;
END;
GO

IF OBJECT_ID(N'opportunities.NewsFeed', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_NewsFeed_FeedUrl' AND object_id = OBJECT_ID(N'opportunities.NewsFeed'))
BEGIN
    CREATE UNIQUE INDEX UX_NewsFeed_FeedUrl ON opportunities.NewsFeed (FeedUrl);
END;
GO

IF OBJECT_ID(N'opportunities.NewsArticle', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_NewsArticle_NewsFeed' AND parent_object_id = OBJECT_ID(N'opportunities.NewsArticle'))
BEGIN
    ALTER TABLE opportunities.NewsArticle
        ADD CONSTRAINT FK_NewsArticle_NewsFeed FOREIGN KEY (FeedId) REFERENCES opportunities.NewsFeed (Id);
END;
GO

IF OBJECT_ID(N'opportunities.NewsArticle', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_NewsArticle_FeedId_ExternalId' AND object_id = OBJECT_ID(N'opportunities.NewsArticle'))
BEGIN
    CREATE UNIQUE INDEX UX_NewsArticle_FeedId_ExternalId
        ON opportunities.NewsArticle (FeedId, ExternalId);
END;
GO

IF OBJECT_ID(N'opportunities.NewsArticle', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NewsArticle_PublishedAt' AND object_id = OBJECT_ID(N'opportunities.NewsArticle'))
BEGIN
    CREATE INDEX IX_NewsArticle_PublishedAt
        ON opportunities.NewsArticle (PublishedAtUtc DESC);
END;
GO

IF OBJECT_ID(N'opportunities.NewsArticle', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NewsArticle_PendingClassification' AND object_id = OBJECT_ID(N'opportunities.NewsArticle'))
BEGIN
    CREATE INDEX IX_NewsArticle_PendingClassification
        ON opportunities.NewsArticle (FeedId, IngestedAtUtc)
        WHERE ClassificationStatus = 'pending';
END;
GO

IF OBJECT_ID(N'opportunities.NewsArticleOrgMention', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_NewsMention_Article' AND parent_object_id = OBJECT_ID(N'opportunities.NewsArticleOrgMention'))
BEGIN
    ALTER TABLE opportunities.NewsArticleOrgMention
        ADD CONSTRAINT FK_NewsMention_Article FOREIGN KEY (NewsArticleId) REFERENCES opportunities.NewsArticle (Id);
END;
GO

IF OBJECT_ID(N'opportunities.NewsArticleOrgMention', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_NewsMention_CanonicalOrg' AND parent_object_id = OBJECT_ID(N'opportunities.NewsArticleOrgMention'))
BEGIN
    ALTER TABLE opportunities.NewsArticleOrgMention
        ADD CONSTRAINT FK_NewsMention_CanonicalOrg FOREIGN KEY (CanonicalOrgId) REFERENCES opportunities.CanonicalOrg (Id);
END;
GO

IF OBJECT_ID(N'opportunities.NewsArticleOrgMention', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_NewsMention_ArticleOrg' AND object_id = OBJECT_ID(N'opportunities.NewsArticleOrgMention'))
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_NewsMention_ArticleOrg_Type' AND object_id = OBJECT_ID(N'opportunities.NewsArticleOrgMention'))
BEGIN
    CREATE UNIQUE INDEX UX_NewsMention_ArticleOrg
        ON opportunities.NewsArticleOrgMention (NewsArticleId, CanonicalOrgId);
END;
GO

IF OBJECT_ID(N'opportunities.NewsArticleOrgMention', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NewsMention_CanonicalOrg' AND object_id = OBJECT_ID(N'opportunities.NewsArticleOrgMention'))
BEGIN
    CREATE INDEX IX_NewsMention_CanonicalOrg
        ON opportunities.NewsArticleOrgMention (CanonicalOrgId, CreatedAtUtc DESC);
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BuildingPermit_PermitSource' AND parent_object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    ALTER TABLE opportunities.BuildingPermit
        ADD CONSTRAINT FK_BuildingPermit_PermitSource FOREIGN KEY (PermitSourceId) REFERENCES opportunities.PermitSource(Id);
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BuildingPermit_OwnerCanonical' AND parent_object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    ALTER TABLE opportunities.BuildingPermit
        ADD CONSTRAINT FK_BuildingPermit_OwnerCanonical FOREIGN KEY (OwnerCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id);
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BuildingPermit_ApplicantCanonical' AND parent_object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    ALTER TABLE opportunities.BuildingPermit
        ADD CONSTRAINT FK_BuildingPermit_ApplicantCanonical FOREIGN KEY (ApplicantCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id);
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BuildingPermit_ContractorCanonical' AND parent_object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    ALTER TABLE opportunities.BuildingPermit
        ADD CONSTRAINT FK_BuildingPermit_ContractorCanonical FOREIGN KEY (ContractorCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id);
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_BuildingPermit_Source_External' AND object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    CREATE UNIQUE INDEX UX_BuildingPermit_Source_External
        ON opportunities.BuildingPermit (PermitSourceId, ExternalId);
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingPermit_IssuedDate' AND object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    CREATE INDEX IX_BuildingPermit_IssuedDate ON opportunities.BuildingPermit (IssuedDate DESC);
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingPermit_OwnerCanonical' AND object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    CREATE INDEX IX_BuildingPermit_OwnerCanonical
        ON opportunities.BuildingPermit (OwnerCanonicalOrgId)
        WHERE OwnerCanonicalOrgId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingPermit_ApplicantCanonical' AND object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    CREATE INDEX IX_BuildingPermit_ApplicantCanonical
        ON opportunities.BuildingPermit (ApplicantCanonicalOrgId)
        WHERE ApplicantCanonicalOrgId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingPermit_ContractorCanonical' AND object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    CREATE INDEX IX_BuildingPermit_ContractorCanonical
        ON opportunities.BuildingPermit (ContractorCanonicalOrgId)
        WHERE ContractorCanonicalOrgId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'opportunities.BuildingPermit', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BuildingPermit_City' AND object_id = OBJECT_ID(N'opportunities.BuildingPermit'))
BEGIN
    CREATE INDEX IX_BuildingPermit_City ON opportunities.BuildingPermit (City, IssuedDate DESC);
END;
GO

PRINT 'Migration 29 schema verifier complete.';
GO
