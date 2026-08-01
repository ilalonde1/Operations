/*
    Kor.OpportunitiesDb migration 49.

    Adds opportunities.CrmEngagementProjectLink — a many-to-many junction
    between CrmEngagement (BD touchpoint with a firm) and
    MajorProjectsInventory (forward-pipeline project). Populated by the
    BdResearchImport `bd-tracking-crosslink` handler which fuzzy-matches
    the engagement's freeform PotentialProjects text against MPI project
    names within the engagement's region.

    Why a junction (not a single FK on CrmEngagement):
    - One engagement's PotentialProjects text often lists multiple projects
      ("Riley St. San Diego, Northgate Sacramento Custom Homes") so a
      single FK can't represent the truth.
    - One MPI project may attract touchpoints from multiple firms (multiple
      engagements link to the same project — a developer + an architect
      both pursuing the same site).

    Idempotent — gated on sys.objects / sys.indexes existence.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'opportunities.CrmEngagementProjectLink', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.CrmEngagementProjectLink
    (
        Id                          BIGINT IDENTITY(1,1) NOT NULL,
        EngagementId                BIGINT             NOT NULL,
        MajorProjectsInventoryId    BIGINT             NOT NULL,
        Confidence                  DECIMAL(5,2)       NOT NULL,         -- 0-100
        MatchedText                 NVARCHAR(500)      NULL,             -- the candidate phrase that triggered
        MatchedBy                   NVARCHAR(150)      NOT NULL CONSTRAINT DF_CrmEngagementProjectLink_MatchedBy DEFAULT (N'BdTrackingCrossLink'),
        MatchedAtUtc                DATETIMEOFFSET     NOT NULL CONSTRAINT DF_CrmEngagementProjectLink_MatchedAt DEFAULT (sysdatetimeoffset()),
        UpdatedAtUtc                DATETIMEOFFSET     NOT NULL CONSTRAINT DF_CrmEngagementProjectLink_UpdatedAt DEFAULT (sysdatetimeoffset()),
        CONSTRAINT PK_CrmEngagementProjectLink PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CrmEngagementProjectLink_Engagement FOREIGN KEY (EngagementId)
            REFERENCES opportunities.CrmEngagements (Id) ON DELETE CASCADE,
        CONSTRAINT FK_CrmEngagementProjectLink_Project FOREIGN KEY (MajorProjectsInventoryId)
            REFERENCES opportunities.MajorProjectsInventory (Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_CrmEngagementProjectLink_Pair
        ON opportunities.CrmEngagementProjectLink (EngagementId, MajorProjectsInventoryId);

    CREATE INDEX IX_CrmEngagementProjectLink_Project
        ON opportunities.CrmEngagementProjectLink (MajorProjectsInventoryId)
        INCLUDE (EngagementId, Confidence);
END;
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON opportunities.CrmEngagementProjectLink TO opportunities_app;
GO
