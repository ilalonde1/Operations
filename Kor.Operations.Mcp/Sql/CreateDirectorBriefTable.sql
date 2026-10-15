USE KorMcp;

IF OBJECT_ID('Mcp.DirectorBrief', 'U') IS NULL
BEGIN
    EXEC('
        CREATE TABLE Mcp.DirectorBrief
        (
            Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DirectorBrief PRIMARY KEY,
            GeneratedAt     DATETIME2(0)   NOT NULL CONSTRAINT DF_DirectorBrief_GeneratedAt DEFAULT SYSUTCDATETIME(),
            WeekOf          DATE           NOT NULL,
            Section         NVARCHAR(32)   NOT NULL,
            Headline        NVARCHAR(512)  NOT NULL,
            Body            NVARCHAR(MAX)  NOT NULL,
            Recommendation  NVARCHAR(MAX)  NULL,
            InputTokens     INT            NOT NULL DEFAULT 0,
            OutputTokens    INT            NOT NULL DEFAULT 0,
            ToolCalls       INT            NOT NULL DEFAULT 0,
            AcknowledgedAt  DATETIME2(0)   NULL,
            AcknowledgedBy  NVARCHAR(254)  NULL,
            Citations       NVARCHAR(MAX)  NOT NULL DEFAULT N''[]''
        );
        CREATE INDEX IX_DirectorBrief_WeekSection ON Mcp.DirectorBrief (WeekOf DESC, Section);
    ');
END;
