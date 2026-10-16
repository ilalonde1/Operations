IF COL_LENGTH(N'opportunities.OpportunityFiles', N'Content') IS NULL
    ALTER TABLE opportunities.OpportunityFiles ADD Content VARBINARY(MAX) NULL;
GO

IF COLUMNPROPERTY(OBJECT_ID(N'opportunities.OpportunityFiles'), N'LocalPath', 'AllowsNull') = 0
    ALTER TABLE opportunities.OpportunityFiles ALTER COLUMN LocalPath NVARCHAR(4000) NULL;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'SubmittedFileId') IS NULL
    ALTER TABLE opportunities.CrmEngagements ADD SubmittedFileId BIGINT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE name = N'FK_CrmEngagements_SubmittedFile'
                 AND parent_object_id = OBJECT_ID(N'opportunities.CrmEngagements'))
    ALTER TABLE opportunities.CrmEngagements
        ADD CONSTRAINT FK_CrmEngagements_SubmittedFile FOREIGN KEY (SubmittedFileId)
        REFERENCES opportunities.OpportunityFiles(Id) ON DELETE NO ACTION;
GO
