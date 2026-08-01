/*
    Kor.OpportunitiesDb migration 21.
    Adds ExternalSource/Key columns to KorPursuits for idempotent imports
    from Deltek CustomProposal (and future external sources). Idempotent.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'ExternalSource'
                 AND Object_ID = Object_ID(N'opportunities.KorPursuits'))
BEGIN
    ALTER TABLE opportunities.KorPursuits
        ADD ExternalSource    nvarchar(40)  NULL,  -- 'Deltek.CustomProposal' | 'Deltek.PR' | future...
            ExternalSourceKey nvarchar(80)  NULL;  -- e.g. CustomPropID / WBS1
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_KorPursuits_ExternalSource_Key'
                 AND object_id = Object_ID(N'opportunities.KorPursuits'))
BEGIN
    CREATE UNIQUE INDEX UX_KorPursuits_ExternalSource_Key
        ON opportunities.KorPursuits (ExternalSource, ExternalSourceKey)
        WHERE ExternalSource IS NOT NULL AND ExternalSourceKey IS NOT NULL;
END;
GO

PRINT 'Migration 21 complete.';
GO
