/*
    Kor.OpportunitiesDb - Phase 5 migration.
    Adds the CRM tables that hang off opportunities.Opportunities:

      opportunities.CrmEngagements   (one-per-active-pursuit, FK Opportunity)
      opportunities.CrmActivities    (append-only log of calls/meetings/emails)
      opportunities.CrmContacts      (buyer-side people for an engagement)

    Idempotent - re-runs cleanly. Granted SELECT/INSERT/UPDATE/DELETE
    to opportunities_app at the end.
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'opportunities')
BEGIN
    EXEC ('CREATE SCHEMA opportunities AUTHORIZATION dbo;');
END;
GO

-- =========================================================================
-- CrmEngagements
-- =========================================================================
IF OBJECT_ID('opportunities.CrmEngagements', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.CrmEngagements
    (
        Id              BIGINT IDENTITY(1,1) NOT NULL,
        OpportunityId   BIGINT             NOT NULL,
        Stage           INT                NOT NULL CONSTRAINT DF_CrmEngagements_Stage DEFAULT (1),
        OwnerStaffId    NVARCHAR(20)       NULL,
        AssignedStaffIds NVARCHAR(500)     NULL,        -- comma-separated bag
        TargetMargin    DECIMAL(5,2)       NULL,
        ProposedFee     DECIMAL(18,2)      NULL,
        ProposedHours   DECIMAL(10,2)      NULL,
        Notes           NVARCHAR(MAX)      NULL,
        OpenedAtUtc     DATETIMEOFFSET     NOT NULL CONSTRAINT DF_CrmEngagements_OpenedAt DEFAULT (sysdatetimeoffset()),
        ClosedAtUtc     DATETIMEOFFSET     NULL,
        OutcomeNotes    NVARCHAR(MAX)      NULL,
        CreatedAtUtc    DATETIMEOFFSET     NOT NULL CONSTRAINT DF_CrmEngagements_CreatedAt DEFAULT (sysdatetimeoffset()),
        CreatedBy       NVARCHAR(150)      NOT NULL,
        UpdatedAtUtc    DATETIMEOFFSET     NOT NULL CONSTRAINT DF_CrmEngagements_UpdatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedBy       NVARCHAR(150)      NOT NULL,
        RowVersion      ROWVERSION         NOT NULL,
        CONSTRAINT PK_CrmEngagements PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CrmEngagements_Opportunity FOREIGN KEY (OpportunityId)
            REFERENCES opportunities.Opportunities(Id) ON DELETE CASCADE,
        CONSTRAINT CK_CrmEngagements_Stage CHECK (Stage BETWEEN 1 AND 9)
    );

    CREATE UNIQUE INDEX UX_CrmEngagements_Opportunity ON opportunities.CrmEngagements (OpportunityId);
    CREATE INDEX IX_CrmEngagements_Stage ON opportunities.CrmEngagements (Stage) INCLUDE (OpportunityId, OwnerStaffId);
END;
GO

-- =========================================================================
-- CrmActivities  (append-only log)
-- =========================================================================
IF OBJECT_ID('opportunities.CrmActivities', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.CrmActivities
    (
        Id              BIGINT IDENTITY(1,1) NOT NULL,
        EngagementId    BIGINT             NOT NULL,
        ActivityType    INT                NOT NULL,            -- 1=Note, 2=Call, 3=Meeting, 4=EmailOut, 5=EmailIn, 6=Deliverable, 99=Other
        OccurredAtUtc   DATETIMEOFFSET     NOT NULL CONSTRAINT DF_CrmActivities_OccurredAt DEFAULT (sysdatetimeoffset()),
        Subject         NVARCHAR(300)      NOT NULL,
        Body            NVARCHAR(MAX)      NULL,
        ContactId       BIGINT             NULL,
        CreatedAtUtc    DATETIMEOFFSET     NOT NULL CONSTRAINT DF_CrmActivities_CreatedAt DEFAULT (sysdatetimeoffset()),
        CreatedBy       NVARCHAR(150)      NOT NULL,
        CONSTRAINT PK_CrmActivities PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CrmActivities_Engagement FOREIGN KEY (EngagementId)
            REFERENCES opportunities.CrmEngagements(Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_CrmActivities_Engagement_Occurred
        ON opportunities.CrmActivities (EngagementId, OccurredAtUtc DESC);
END;
GO

-- =========================================================================
-- CrmContacts
-- =========================================================================
IF OBJECT_ID('opportunities.CrmContacts', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.CrmContacts
    (
        Id              BIGINT IDENTITY(1,1) NOT NULL,
        EngagementId    BIGINT             NOT NULL,
        DisplayName     NVARCHAR(200)      NOT NULL,
        Role            NVARCHAR(150)      NULL,
        Email           NVARCHAR(300)      NULL,
        Phone           NVARCHAR(60)       NULL,
        IsPrimary       BIT                NOT NULL CONSTRAINT DF_CrmContacts_IsPrimary DEFAULT (0),
        Notes           NVARCHAR(MAX)      NULL,
        CreatedAtUtc    DATETIMEOFFSET     NOT NULL CONSTRAINT DF_CrmContacts_CreatedAt DEFAULT (sysdatetimeoffset()),
        CreatedBy       NVARCHAR(150)      NOT NULL,
        UpdatedAtUtc    DATETIMEOFFSET     NOT NULL CONSTRAINT DF_CrmContacts_UpdatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedBy       NVARCHAR(150)      NOT NULL,
        RowVersion      ROWVERSION         NOT NULL,
        CONSTRAINT PK_CrmContacts PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CrmContacts_Engagement FOREIGN KEY (EngagementId)
            REFERENCES opportunities.CrmEngagements(Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_CrmContacts_Engagement ON opportunities.CrmContacts (EngagementId);
END;
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON opportunities.CrmEngagements TO opportunities_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON opportunities.CrmActivities TO opportunities_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON opportunities.CrmContacts TO opportunities_app;
GO
