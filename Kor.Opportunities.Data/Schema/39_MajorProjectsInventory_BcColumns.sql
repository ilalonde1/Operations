USE [KorOpportunitiesDb];
GO

-- Round 23: BC MPI has much richer fields than AB. Add nullable columns;
-- AB rows simply leave them NULL.
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ExternalProjectId') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ExternalProjectId nvarchar(50) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ProjectDescription') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ProjectDescription nvarchar(max) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ConstructionType') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ConstructionType nvarchar(100) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ConstructionSubtype') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ConstructionSubtype nvarchar(100) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ProjectType') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ProjectType nvarchar(100) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ProjectCategoryName') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ProjectCategoryName nvarchar(200) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ProjectStatus') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ProjectStatus nvarchar(100) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ProjectStage') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ProjectStage nvarchar(100) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ArchitectName') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ArchitectName nvarchar(500) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ArchitectCanonicalOrgId') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ArchitectCanonicalOrgId bigint NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'PublicFundingInd') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD PublicFundingInd bit NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ProvincialFunding') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ProvincialFunding bit NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'FederalFunding') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD FederalFunding bit NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'MunicipalFunding') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD MunicipalFunding bit NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'OtherPublicFunding') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD OtherPublicFunding bit NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'GreenBuildingInd') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD GreenBuildingInd bit NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'IndigenousInd') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD IndigenousInd bit NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'IndigenousNames') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD IndigenousNames nvarchar(500) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ConstructionJobs') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ConstructionJobs int NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'OperatingJobs') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD OperatingJobs int NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'StandardizedStartDate') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD StandardizedStartDate nvarchar(20) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'StandardizedCompletionDate') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD StandardizedCompletionDate nvarchar(20) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'Latitude') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD Latitude decimal(9,6) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'Longitude') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD Longitude decimal(9,6) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'ProjectWebsite') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ProjectWebsite nvarchar(1000) NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'IssueYear') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD IssueYear smallint NULL;
GO
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'IssueQuarter') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD IssueQuarter tinyint NULL;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_MPI_ArchitectCanonicalOrg'
      AND parent_object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
BEGIN
    ALTER TABLE opportunities.MajorProjectsInventory
        ADD CONSTRAINT FK_MPI_ArchitectCanonicalOrg FOREIGN KEY (ArchitectCanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg (Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MPI_ArchitectCanonicalOrgId' AND object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
    CREATE INDEX IX_MPI_ArchitectCanonicalOrgId ON opportunities.MajorProjectsInventory (ArchitectCanonicalOrgId) WHERE ArchitectCanonicalOrgId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MPI_ExternalProjectId' AND object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
    CREATE INDEX IX_MPI_ExternalProjectId ON opportunities.MajorProjectsInventory (Province, ExternalProjectId) WHERE ExternalProjectId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MPI_IndigenousInd' AND object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
    CREATE INDEX IX_MPI_IndigenousInd ON opportunities.MajorProjectsInventory (IndigenousInd) WHERE IndigenousInd = 1;
GO

GRANT SELECT, INSERT, UPDATE ON opportunities.MajorProjectsInventory TO opportunities_app;
GO

PRINT 'Migration 39: BC columns added to MajorProjectsInventory.';
GO
