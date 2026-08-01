-- Mcp.CooCardItem — weekly COO Card top-5 ranked action items.
-- Idempotent. Assumes the Mcp schema already exists (created by CreateAuditLog.sql).
IF NOT EXISTS (SELECT 1 FROM sys.tables t INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'Mcp' AND t.name = N'CooCardItem')
BEGIN
    CREATE TABLE Mcp.CooCardItem
    (
        Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CooCardItem PRIMARY KEY,
        GeneratedAt     DATETIME2(3) NOT NULL CONSTRAINT DF_CooCardItem_GeneratedAt DEFAULT (SYSUTCDATETIME()),
        WeekOf          DATE NOT NULL,
        Rank            INT NOT NULL,
        Severity        NVARCHAR(16) NOT NULL,
        Headline        NVARCHAR(400) NOT NULL,
        Body            NVARCHAR(MAX) NOT NULL,
        Recommendation  NVARCHAR(MAX) NOT NULL,
        SourceTags      NVARCHAR(400) NULL,
        AcknowledgedAt  DATETIME2(3) NULL,
        AcknowledgedBy  NVARCHAR(256) NULL
    );

    CREATE INDEX IX_CooCardItem_WeekOf_Rank ON Mcp.CooCardItem (WeekOf DESC, Rank ASC);
    CREATE INDEX IX_CooCardItem_Acknowledged ON Mcp.CooCardItem (WeekOf DESC) INCLUDE (AcknowledgedAt);
END
GO
