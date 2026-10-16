IF COL_LENGTH(N'opportunities.CrmEngagements', N'SubmittedFileName') IS NULL
    ALTER TABLE opportunities.CrmEngagements ADD SubmittedFileName NVARCHAR(260) NULL;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'FolderLink') IS NULL
    ALTER TABLE opportunities.CrmEngagements ADD FolderLink NVARCHAR(1000) NULL;
GO
