/* Adds KOR seat-read fields populated by BdResearchImport --only pipeline-seats. */
IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'SeatStatus') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD SeatStatus nvarchar(20) NULL;
GO

IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'KorSeatOpening') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD KorSeatOpening nvarchar(500) NULL;
GO

IF COL_LENGTH(N'opportunities.MajorProjectsInventory', N'SeatConfidence') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD SeatConfidence nvarchar(20) NULL;
GO

PRINT 'Migration 52: MPI seat status fields added.';
GO
