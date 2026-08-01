-- Ensure natural-key unique index for (ExternalSource, ExternalSourceKey) upserts.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name IN (N'UX_KorPursuits_ExternalSourceKey', N'UX_KorPursuits_ExternalSource_Key')
                 AND object_id = OBJECT_ID(N'opportunities.KorPursuits'))
BEGIN
    CREATE UNIQUE INDEX UX_KorPursuits_ExternalSourceKey
        ON opportunities.KorPursuits (ExternalSource, ExternalSourceKey)
        WHERE ExternalSource IS NOT NULL AND ExternalSourceKey IS NOT NULL;
END;
GO

PRINT 'Migration 32  KorPursuits (ExternalSource, ExternalSourceKey) unique index ensured.';
GO
