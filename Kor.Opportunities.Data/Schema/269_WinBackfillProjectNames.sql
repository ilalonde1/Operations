/*
    Kor.OpportunitiesDb migration 269.

    Follow-up to 268: the win backfill stored each project title in Notes. Copy it
    into PotentialProjects so the CRM list's existing ProjectName display logic
    (Opportunity?.Name ?? PotentialProjects ?? "(no project named yet)") surfaces a
    real name instead of a wall of placeholders — no UI/display hack needed.

    Safe: the cross-link matcher (BdTrackingCrossLink) only reads engagements with
    Region IS NOT NULL; these backfilled wins are Region-null, so setting
    PotentialProjects does not feed them into project matching.

    Idempotent (only fills NULLs on the tagged rows). Reversible:
        UPDATE opportunities.CrmEngagements SET PotentialProjects = NULL
        WHERE ExternalSource = N'Deltek.CustomProposal';
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

UPDATE e
   SET e.PotentialProjects = kp.Title,
       e.UpdatedAtUtc      = SYSDATETIMEOFFSET()
FROM opportunities.CrmEngagements e
JOIN opportunities.KorPursuits   kp
     ON kp.ExternalSource    = N'Deltek.CustomProposal'
    AND kp.ExternalSourceKey = e.ExternalSourceKey
WHERE e.ExternalSource     = N'Deltek.CustomProposal'
  AND e.PotentialProjects IS NULL
  AND NULLIF(LTRIM(RTRIM(kp.Title)), N'') IS NOT NULL;
GO

PRINT 'Migration 269 complete.';
GO
