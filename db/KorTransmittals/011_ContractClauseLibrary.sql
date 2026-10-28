/*
011_ContractClauseLibrary.sql

The contract clause library (2026-10-28). Rory, 28 Oct: "compile all KOR's previously acceptable clauses
into 1 database, so that we can easily check new contracts against what we have already agreed to".
Lives here, in KorTransmittals, because this database holds dbo.ProposalBlockTemplates, the FeeProposal
builder's block library, and a terms block built from these clauses belongs beside it (it was first
created in KorOpportunitiesDb by mistake the same afternoon and dropped empty).

CREATES (only what is missing; re-runnable):
  dbo.ContractClauseSources  one row per document part read (a PDF, a Word file, an email attachment):
                             job and client, KOR's paper or the client's, role (executed / markup /
                             draft / proposal), place in the job's version order, path on KOR-FS01.
  dbo.ContractClauses        one row per clause-sized section carrying a contract topic, with full text.
  dbo.vw_ContractPositions   the clauses of signed documents and of the latest version on file, per job.
FILLED BY tools/ContractClauseLibrary/Import-ContractClauses.ps1 -Database KorTransmittals from the CSVs
in docs/contract-clause-library/ (truncate-and-reload). Nothing else touched.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ContractClauseSources', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContractClauseSources
    (
        SourceId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContractClauseSources PRIMARY KEY,
        DocId         CHAR(12)       NOT NULL,   -- first 12 hex of the file's SHA-1 (extract_clauses.py)
        Part          NVARCHAR(300)  NOT NULL CONSTRAINT DF_ContractClauseSources_Part DEFAULT (N''),  -- attachment name inside an email, else ''
        SourceKey     NVARCHAR(80)   NOT NULL,   -- job number, or OPP:<pursuit folder> for Project Opportunities
        Job           NVARCHAR(16)   NULL,
        ClientName    NVARCHAR(200)  NULL,       -- Deltek Clendor name joined on the job (WBS1)
        ProjectName   NVARCHAR(200)  NULL,
        Paper         NVARCHAR(8)    NOT NULL,   -- 'KOR' (fee proposal) or 'client'
        Role          NVARCHAR(12)   NOT NULL,   -- executed | markup | draft | proposal
        VersionIndex  NVARCHAR(12)   NULL,       -- e.g. '3/7' within the job, by file date
        IsLatest      BIT            NOT NULL CONSTRAINT DF_ContractClauseSources_IsLatest DEFAULT (0),
        NamesKor      BIT            NOT NULL CONSTRAINT DF_ContractClauseSources_NamesKor DEFAULT (0),
        FileDate      DATE           NULL,
        Area          NVARCHAR(16)   NULL,       -- projects | opportunities | templates
        Kind          NVARCHAR(20)   NOT NULL,   -- pdf | pdf-ocr | docx | doc-converted | unreadable
        Words         INT            NOT NULL,
        FilePath      NVARCHAR(700)  NOT NULL,   -- relative to E:\ on KOR-FS01
        ImportedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_ContractClauseSources_Imported DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_ContractClauseSources_Doc UNIQUE (DocId, Part)
    );
    CREATE INDEX IX_ContractClauseSources_Key ON dbo.ContractClauseSources (SourceKey, Role);
    CREATE INDEX IX_ContractClauseSources_Client ON dbo.ContractClauseSources (ClientName);
END;

IF OBJECT_ID(N'dbo.ContractClauses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContractClauses
    (
        ClauseId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContractClauses PRIMARY KEY,
        SourceId     INT            NOT NULL CONSTRAINT FK_ContractClauses_Source REFERENCES dbo.ContractClauseSources (SourceId),
        Section      NVARCHAR(24)   NOT NULL,   -- '8.4', or '0p3' for the third paragraph chunk of an unnumbered document
        Heading      NVARCHAR(200)  NULL,
        Topics       NVARCHAR(400)  NOT NULL,   -- '|'-separated: limitation_of_liability, indemnity, intellectual_property, ...
        IsDefinition BIT            NOT NULL CONSTRAINT DF_ContractClauses_IsDefinition DEFAULT (0),
        Words        INT            NOT NULL,
        ClauseText   NVARCHAR(MAX)  NOT NULL
    );
    CREATE INDEX IX_ContractClauses_Source ON dbo.ContractClauses (SourceId);
END;

IF OBJECT_ID(N'dbo.vw_ContractPositions', N'V') IS NULL
    EXEC(N'CREATE VIEW dbo.vw_ContractPositions AS SELECT 1 AS Placeholder;');
EXEC(N'
ALTER VIEW dbo.vw_ContractPositions AS
SELECT s.SourceKey, s.Job, s.ClientName, s.ProjectName, s.Paper, s.Role, s.IsLatest, s.FileDate, s.FilePath, s.Part,
       c.ClauseId, c.Section, c.Heading, c.Topics, c.Words, c.ClauseText
  FROM dbo.ContractClauses c
  JOIN dbo.ContractClauseSources s ON s.SourceId = c.SourceId
 WHERE s.Paper = N''client''
   AND (s.Role = N''executed'' OR s.IsLatest = 1)
   AND c.IsDefinition = 0;');

COMMIT TRANSACTION;
