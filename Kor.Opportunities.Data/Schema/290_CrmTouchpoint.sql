/*
  290_CrmTouchpoint.sql  (2026-07-17)

  CRM Neural Gap Register G5: the interaction log. An engagement was one row
  with one Notes blob — no sequence of contacts, so "when did we last touch
  Ledcor Kelowna?" was unanswerable. CrmTouchpoint records each real contact
  (meeting / email / call / event / note); vw_OrgWarmth derives warmth from it
  as ONE predicate (doctrine D11 style: warmth is a view over touchpoints,
  never a hand-maintained column that drifts).

  Anchoring: CanonicalOrgId is required (warmth is org-first); EngagementId
  and IntelPersonId are optional refinements. OccurredAtUtc is when the touch
  HAPPENED (past only by convention — future intents live on
  CrmEngagements.NextActionDueUtc, surfaced by the morning report since G2).

  Backfill: the touchpoints evidenced in the primary-source threads this week.
*/

IF OBJECT_ID(N'opportunities.CrmTouchpoint', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.CrmTouchpoint (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NaturalKey      CHAR(40)        NOT NULL,
        CanonicalOrgId  BIGINT          NOT NULL
            CONSTRAINT FK_CrmTouchpoint_Org REFERENCES opportunities.CanonicalOrg (Id),
        EngagementId    BIGINT          NULL
            CONSTRAINT FK_CrmTouchpoint_Engagement REFERENCES opportunities.CrmEngagements (Id),
        IntelPersonId   BIGINT          NULL
            CONSTRAINT FK_CrmTouchpoint_Person REFERENCES opportunities.IntelPerson (Id),
        Kind            NVARCHAR(12)    NOT NULL
            CONSTRAINT CK_CrmTouchpoint_Kind CHECK (Kind IN (N'Meeting', N'Email', N'Call', N'Event', N'Note')),
        OccurredAtUtc   DATETIMEOFFSET  NOT NULL,
        Summary         NVARCHAR(MAX)   NOT NULL,
        KorStaff        NVARCHAR(100)   NULL,   -- which KOR person(s) made the touch
        CreatedBy       NVARCHAR(100)   NOT NULL,
        CreatedAtUtc    DATETIMEOFFSET  NOT NULL CONSTRAINT DF_CrmTouchpoint_Created DEFAULT sysdatetimeoffset(),
        RetiredAtUtc    DATETIMEOFFSET  NULL,
        CONSTRAINT UQ_CrmTouchpoint_NaturalKey UNIQUE (NaturalKey)
    );
    CREATE INDEX IX_CrmTouchpoint_OrgWhen ON opportunities.CrmTouchpoint (CanonicalOrgId, OccurredAtUtc DESC) WHERE RetiredAtUtc IS NULL;
END
GO

/* ---- Warmth: ONE predicate over touchpoints ----------------------------- */
CREATE OR ALTER VIEW opportunities.vw_OrgWarmth
AS
/* Org-level relationship warmth derived purely from logged touchpoints.
   Warm = touched in 30d; Cooling = 90d; Cold = older. Orgs with no
   touchpoints simply don't appear — absence of evidence is not warmth. */
SELECT t.CanonicalOrgId,
       MAX(t.OccurredAtUtc)                                       AS LastTouchUtc,
       COUNT(*)                                                   AS Touches90d,
       CASE WHEN MAX(t.OccurredAtUtc) >= DATEADD(DAY, -30, sysdatetimeoffset()) THEN N'Warm'
            WHEN MAX(t.OccurredAtUtc) >= DATEADD(DAY, -90, sysdatetimeoffset()) THEN N'Cooling'
            ELSE N'Cold' END                                      AS Warmth
FROM opportunities.CrmTouchpoint t
WHERE t.RetiredAtUtc IS NULL
  AND t.OccurredAtUtc >= DATEADD(DAY, -90, sysdatetimeoffset())
  AND t.OccurredAtUtc <= sysdatetimeoffset()
GROUP BY t.CanonicalOrgId;
GO

/* ---- Backfill: this week's evidenced touches ---------------------------- */
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @by nvarchar(100) = N'BrainDecompose-2026-07-17';
DECLARE @barry bigint = (SELECT Id FROM opportunities.IntelPerson WHERE Email = N'barry.murphy@ledcor.com' AND RetiredAtUtc IS NULL);
DECLARE @ell   bigint = (SELECT Id FROM opportunities.IntelPerson WHERE Email = N'elliot.wood@ledcor.com'  AND RetiredAtUtc IS NULL);
DECLARE @terry bigint = (SELECT Id FROM opportunities.IntelPerson WHERE Email = N'terry.gray@arcadis.com'  AND RetiredAtUtc IS NULL);

DECLARE @t TABLE (OrgId bigint, EngId bigint NULL, PersonId bigint NULL, Kind nvarchar(12), Occ datetimeoffset, Summary nvarchar(max), Staff nvarchar(100));
INSERT @t VALUES
 (69671, NULL, @barry, N'Event',   '2026-06-24', N'Ledcor client event, Vancouver — Omar met Barry Murphy (Dir of BD); also the origin of the Arcadis/Terry Gray channel.', N'Omar Alcazar'),
 (153,   NULL, @terry, N'Event',   '2026-06-24', N'Ledcor client event, Vancouver — Omar met Terry Gray (Arcadis Gov&Civic Canada).', N'Omar Alcazar'),
 (153,   NULL, @terry, N'Email',   '2026-07-15', N'Active thread Omar<->Terry: Alberta joint BD + Arcadis Mexico intros; Terry "digging up the names".', N'Omar Alcazar'),
 (69671, 375,  @barry, N'Email',   '2026-07-15', N'Omar asked Barry for Kelowna intro (thread: Introduction to Ledcor Kelowna).', N'Omar Alcazar'),
 (69671, 375,  @ell,   N'Email',   '2026-07-16', N'Barry forwarded to Elliot Wood ("you''re the man"); Elliot replied same morning proposing to meet — office overview: Okanagan+Interior, institutional/industrial/commercial, UBC/IH/munis/BC Housing.', N'Omar Alcazar'),
 (69671, 375,  @ell,   N'Email',   '2026-07-17', N'Meeting locked: Mon 2026-07-20 10:00 @ Ledcor Kelowna (Landmark 4). Jim confirmed.', N'Jim DesRoches');

MERGE opportunities.CrmTouchpoint WITH (HOLDLOCK) AS T
USING (SELECT OrgId, EngId, PersonId, Kind, Occ, Summary, Staff,
              CONVERT(char(40), HASHBYTES('SHA1', CAST(
                CAST(OrgId AS varchar(20)) + '|' + Kind + '|' + CONVERT(varchar(10), Occ, 120) + '|' +
                LOWER(REPLACE(LEFT(Summary, 80), ' ', '')) AS varchar(8000))), 2) AS NK
       FROM @t) AS S ON T.NaturalKey = S.NK
WHEN MATCHED THEN UPDATE SET Summary = S.Summary
WHEN NOT MATCHED THEN INSERT
   (NaturalKey, CanonicalOrgId, EngagementId, IntelPersonId, Kind, OccurredAtUtc, Summary, KorStaff, CreatedBy)
   VALUES (S.NK, S.OrgId, S.EngId, S.PersonId, S.Kind, S.Occ, S.Summary, S.Staff, @by);

SELECT TouchpointsBanked = COUNT(*) FROM opportunities.CrmTouchpoint WHERE RetiredAtUtc IS NULL;
GO
