/*
    Kor.OpportunitiesDb migration 24.
    Adds BC Registry / OrgBook snapshot columns to CanonicalOrg.
    Idempotent. Uses GO-separated batches so column references compile.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'BcRegistryTopicId'
                 AND Object_ID = Object_ID(N'opportunities.CanonicalOrg'))
BEGIN
    ALTER TABLE opportunities.CanonicalOrg
        ADD BcRegistryTopicId           nvarchar(50)   NULL, -- OrgBook topic_id
            BcRegistryLegalName         nvarchar(300)  NULL,
            BcRegistryEntityType        nvarchar(50)   NULL, -- e.g. 'BC' (corp), 'XPRO' (extra-provincial), 'SP' (sole prop), 'SOC' (society)
            BcRegistryStatus            nvarchar(40)   NULL, -- 'Active' | 'Historical' | ...
            BcRegistryIncorporationDate date           NULL,
            BcRegistryJurisdiction      nvarchar(50)   NULL,
            BcRegistryBusinessNumber    nvarchar(20)   NULL, -- CRA business number
            BcRegistryRegisteredOffice  nvarchar(500)  NULL,
            BcRegistryLastCheckedAtUtc  datetimeoffset NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_CanonicalOrg_BcRegistryTopicId'
                 AND object_id = Object_ID(N'opportunities.CanonicalOrg'))
BEGIN
    CREATE INDEX IX_CanonicalOrg_BcRegistryTopicId
        ON opportunities.CanonicalOrg (BcRegistryTopicId)
        WHERE BcRegistryTopicId IS NOT NULL;
END;
GO

PRINT 'Migration 24 complete.';
GO
