USE [KorOpportunitiesDb];
GO

IF COL_LENGTH('opportunities.Opportunities', 'IsPrimeConsultantRfp') IS NULL
BEGIN
    ALTER TABLE opportunities.Opportunities ADD IsPrimeConsultantRfp bit NULL;
END;
GO

IF COL_LENGTH('opportunities.Opportunities', 'PrimeConfidence') IS NULL
BEGIN
    ALTER TABLE opportunities.Opportunities ADD PrimeConfidence decimal(4,3) NULL;
END;
GO

IF COL_LENGTH('opportunities.Opportunities', 'PrimeDisciplineType') IS NULL
BEGIN
    ALTER TABLE opportunities.Opportunities ADD PrimeDisciplineType nvarchar(40) NULL;
END;
GO

IF COL_LENGTH('opportunities.Opportunities', 'PrimeProjectSector') IS NULL
BEGIN
    ALTER TABLE opportunities.Opportunities ADD PrimeProjectSector nvarchar(40) NULL;
END;
GO

IF COL_LENGTH('opportunities.Opportunities', 'PrimeLikelyType') IS NULL
BEGIN
    ALTER TABLE opportunities.Opportunities ADD PrimeLikelyType nvarchar(20) NULL;
END;
GO

IF COL_LENGTH('opportunities.Opportunities', 'PrimeKorSectorMatch') IS NULL
BEGIN
    ALTER TABLE opportunities.Opportunities ADD PrimeKorSectorMatch bit NULL;
END;
GO

IF COL_LENGTH('opportunities.Opportunities', 'PrimeKorLocationMatch') IS NULL
BEGIN
    ALTER TABLE opportunities.Opportunities ADD PrimeKorLocationMatch bit NULL;
END;
GO

IF COL_LENGTH('opportunities.Opportunities', 'PrimeClassifiedAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.Opportunities ADD PrimeClassifiedAtUtc datetimeoffset NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.Opportunities')
      AND name = N'IX_Opportunities_PrimeRfp'
)
BEGIN
    CREATE INDEX IX_Opportunities_PrimeRfp
        ON opportunities.Opportunities (IsPrimeConsultantRfp)
        WHERE IsPrimeConsultantRfp = 1;
END;
GO
