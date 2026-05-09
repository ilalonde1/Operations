USE KorMcp;

IF OBJECT_ID('Mcp.CollectionsCase', 'U') IS NULL
BEGIN
    EXEC('
        CREATE TABLE Mcp.CollectionsCase
        (
            Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_McpCollectionsCase PRIMARY KEY,
            ClientID        NVARCHAR(32)   NOT NULL,
            Status          NVARCHAR(16)   NOT NULL
                CONSTRAINT CK_McpCollectionsCase_Status
                CHECK (Status IN (N''Open'', N''Lien'', N''Foreclosure'', N''WriteOff'', N''Resolved'')),
            OpenedAt        DATETIME2(0)   NOT NULL CONSTRAINT DF_McpCollectionsCase_OpenedAt    DEFAULT SYSUTCDATETIME(),
            OpenedBy        NVARCHAR(254)  NOT NULL,
            LastUpdatedAt   DATETIME2(0)   NOT NULL CONSTRAINT DF_McpCollectionsCase_LastUpdated DEFAULT SYSUTCDATETIME(),
            LastUpdatedBy   NVARCHAR(254)  NOT NULL,
            ResolvedAt      DATETIME2(0)   NULL,
            LegalAmount     DECIMAL(18,2)  NULL,
            Notes           NVARCHAR(MAX)  NULL
        );

        CREATE UNIQUE INDEX UX_McpCollectionsCase_ActiveClient
            ON Mcp.CollectionsCase (ClientID)
            WHERE Status <> N''Resolved'';

        CREATE INDEX IX_McpCollectionsCase_Status
            ON Mcp.CollectionsCase (Status, LastUpdatedAt DESC);

        CREATE INDEX IX_McpCollectionsCase_Client
            ON Mcp.CollectionsCase (ClientID, OpenedAt DESC);
    ');
END;
