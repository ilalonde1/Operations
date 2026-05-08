USE KorMcp;

IF SCHEMA_ID('Mcp') IS NULL EXEC('CREATE SCHEMA Mcp');

IF OBJECT_ID('Mcp.Alert', 'U') IS NULL
BEGIN
    EXEC('
        CREATE TABLE Mcp.Alert
        (
            Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_McpAlert PRIMARY KEY,
            GeneratedAt     DATETIME2(0)   NOT NULL CONSTRAINT DF_McpAlert_GeneratedAt DEFAULT SYSUTCDATETIME(),
            RuleId          NVARCHAR(64)   NOT NULL,
            Section         NVARCHAR(32)   NOT NULL,
            Severity        NVARCHAR(16)   NOT NULL,
            Subject         NVARCHAR(256)  NULL,
            Title           NVARCHAR(512)  NOT NULL,
            Body            NVARCHAR(MAX)  NOT NULL,
            AcknowledgedAt  DATETIME2(0)   NULL,
            AcknowledgedBy  NVARCHAR(254)  NULL
        );
        CREATE INDEX IX_McpAlert_Unacked  ON Mcp.Alert (AcknowledgedAt, GeneratedAt DESC);
        CREATE INDEX IX_McpAlert_RuleSubj ON Mcp.Alert (RuleId, Subject, GeneratedAt DESC);
    ');
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'mcp_app')
    CREATE USER mcp_app FOR LOGIN mcp_app;
-- mcp_app already has db_datareader+writer from Phase 1A; this is just defensive.
