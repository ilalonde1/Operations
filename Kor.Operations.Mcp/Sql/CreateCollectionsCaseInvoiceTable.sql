USE KorMcp;

IF OBJECT_ID('Mcp.CollectionsCaseInvoice', 'U') IS NULL
BEGIN
    EXEC('
        CREATE TABLE Mcp.CollectionsCaseInvoice
        (
            Id              BIGINT IDENTITY(1,1) NOT NULL
                                CONSTRAINT PK_McpCollectionsCaseInvoice PRIMARY KEY,
            CaseId          BIGINT         NOT NULL,
            WBS1            NVARCHAR(32)   NOT NULL,
            InvoiceNumber   NVARCHAR(64)   NOT NULL,
            AddedAt         DATETIME2(0)   NOT NULL
                                CONSTRAINT DF_McpCollectionsCaseInvoice_AddedAt DEFAULT SYSUTCDATETIME(),
            AddedBy         NVARCHAR(254)  NOT NULL,
            -- An invoice can only ever live on one case (resolved or active).
            -- Per Ian: same (WBS1, Invoice) on two cases should never happen.
            CONSTRAINT FK_McpCollectionsCaseInvoice_Case
                FOREIGN KEY (CaseId) REFERENCES Mcp.CollectionsCase(Id)
                ON DELETE CASCADE,
            CONSTRAINT UQ_McpCollectionsCaseInvoice_Invoice UNIQUE (WBS1, InvoiceNumber)
        );

        CREATE INDEX IX_McpCollectionsCaseInvoice_Case
            ON Mcp.CollectionsCaseInvoice (CaseId);
    ');
END;
