IF COL_LENGTH(N'opportunities.OpportunitySources', N'ConfigJson') IS NULL
BEGIN
    ALTER TABLE opportunities.OpportunitySources
        ADD ConfigJson NVARCHAR(MAX) NULL;
END;
GO

PRINT 'Migration 62: opportunities.OpportunitySources.ConfigJson added.';
GO
