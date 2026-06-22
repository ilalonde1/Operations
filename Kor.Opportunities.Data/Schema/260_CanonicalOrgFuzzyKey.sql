/* Kor.OpportunitiesDb migration 260: persist the application's safe fuzzy
   canonical-org name key for resolver lookups. The value is maintained by
   application code because NormalizeForFuzzyMatch uses regex transformations
   that are not suitable for a SQL computed column. */

IF COL_LENGTH(N'opportunities.CanonicalOrg', N'FuzzyNormalizedName') IS NULL
BEGIN
    ALTER TABLE opportunities.CanonicalOrg
        ADD FuzzyNormalizedName nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CanonicalOrg_FuzzyNormalizedName'
      AND object_id = OBJECT_ID(N'opportunities.CanonicalOrg'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CanonicalOrg_FuzzyNormalizedName
        ON opportunities.CanonicalOrg (FuzzyNormalizedName)
        WHERE FuzzyNormalizedName IS NOT NULL AND RetiredAtUtc IS NULL;
END;
GO
