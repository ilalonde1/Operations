-- 121: BD report generation audit log (BD-UI-Plan-2026-06-08, Phase B item 6).
-- Every report generate (HTML preview or DOCX export) is logged with the
-- generating user; RecipientEmail fills in when Outlook delivery lands.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'BdReportAuditLog' AND schema_id = SCHEMA_ID(N'opportunities'))
BEGIN
    CREATE TABLE opportunities.BdReportAuditLog (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Category        NVARCHAR(64)   NOT NULL,  -- sector key or 'call-sheet'
        Format          NVARCHAR(16)   NOT NULL,  -- 'docx' | 'html'
        GeneratedAtUtc  DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdReportAuditLog_GeneratedAt DEFAULT sysdatetimeoffset(),
        GeneratedByUser NVARCHAR(256)  NOT NULL,
        RecipientEmail  NVARCHAR(256)  NULL,
        RecordCount     INT            NULL,
        Notes           NVARCHAR(500)  NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BdReportAuditLog_GeneratedAtUtc' AND object_id = OBJECT_ID(N'opportunities.BdReportAuditLog'))
BEGIN
    CREATE INDEX IX_BdReportAuditLog_GeneratedAtUtc
        ON opportunities.BdReportAuditLog (GeneratedAtUtc DESC);
END;
GO
