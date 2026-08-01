IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'opportunities.CanonicalOrg')
                 AND name = N'LastKorProjectAtUtc')
BEGIN
    ALTER TABLE opportunities.CanonicalOrg
        ADD LastKorProjectAtUtc datetime2(3) NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'opportunities.CanonicalOrg')
                 AND name = N'KorProjectsCount')
BEGIN
    ALTER TABLE opportunities.CanonicalOrg
        ADD KorProjectsCount int NOT NULL CONSTRAINT DF_CanonicalOrg_KorProjectsCount DEFAULT 0;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_CanonicalOrg_LastKorProjectAtUtc')
BEGIN
    CREATE INDEX IX_CanonicalOrg_LastKorProjectAtUtc
        ON opportunities.CanonicalOrg (LastKorProjectAtUtc DESC)
        WHERE LastKorProjectAtUtc IS NOT NULL;
END;
GO

PRINT 'Migration 31  CanonicalOrg KOR-project signal columns added.';
GO
