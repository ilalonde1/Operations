/* KorOpportunitiesDb migration 356: link imported recommendations to the decision ledger. */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'opportunities.BdDecision') AND name = N'CK_BdDecision_Kind'
      AND definition NOT LIKE N'%N''Brief''%')
    ALTER TABLE opportunities.BdDecision DROP CONSTRAINT CK_BdDecision_Kind;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'opportunities.BdDecision') AND name = N'CK_BdDecision_Kind')
    ALTER TABLE opportunities.BdDecision ADD CONSTRAINT CK_BdDecision_Kind
        CHECK (Kind IN (N'Go', N'NoGo', N'Drop', N'Reassign', N'Price', N'Target', N'Other', N'Brief'));
GO

IF COL_LENGTH(N'opportunities.BdDecision', N'BriefRowId') IS NULL
    ALTER TABLE opportunities.BdDecision ADD BriefRowId BIGINT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'opportunities.BdDecision') AND name = N'FK_BdDecision_BriefRow')
    ALTER TABLE opportunities.BdDecision ADD CONSTRAINT FK_BdDecision_BriefRow
        FOREIGN KEY (BriefRowId) REFERENCES opportunities.DirectorBrief(Id) ON DELETE NO ACTION;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.BdDecision') AND name = N'IX_BdDecision_BriefRowId')
    CREATE INDEX IX_BdDecision_BriefRowId ON opportunities.BdDecision (BriefRowId);
GO
