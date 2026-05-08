---Idempotent script: safe to re-run during deploy. Operator runs this once
---against the SQL Server before first start.

IF SCHEMA_ID('Mcp') IS NULL
    EXEC('CREATE SCHEMA Mcp');
GO

IF OBJECT_ID('Mcp.AuditLog', 'U') IS NULL
BEGIN
    CREATE TABLE Mcp.AuditLog
    (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_McpAuditLog PRIMARY KEY,
        OccurredAt    DATETIME2(0)   NOT NULL CONSTRAINT DF_McpAuditLog_OccurredAt DEFAULT SYSUTCDATETIME(),
        UserUpn       NVARCHAR(254)  NULL,
        ClientApp     NVARCHAR(64)   NULL,
        ToolName      NVARCHAR(256)  NULL,
        InputJson     NVARCHAR(MAX)  NULL,
        ResultStatus  NVARCHAR(16)   NOT NULL,
        DurationMs    INT            NOT NULL,
        ErrorMessage  NVARCHAR(2000) NULL
    );

    CREATE INDEX IX_McpAuditLog_OccurredAt ON Mcp.AuditLog (OccurredAt DESC);
    CREATE INDEX IX_McpAuditLog_UserTool   ON Mcp.AuditLog (UserUpn, ToolName, OccurredAt DESC);
END
GO
