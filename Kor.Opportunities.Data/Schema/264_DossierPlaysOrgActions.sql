USE [KorOpportunitiesDb];
GO

/* =====================================================================
   264 — Dossier "plays" flag also counts ORG-level IntelAction.
   ---------------------------------------------------------------------
   vDossierCompleteness.HasPlays counted only opportunities.IntelProjectAction
   (project-level plays targeting an org). But org enrichment (FirmNarrative /
   FirmNarrativeHoning) writes opportunities.IntelAction (org-level plays:
   ContactStrategy / PursuitAngle / TeamingMove). So an org could have plenty
   of open org plays yet still read "missing plays" — the flag never flipped,
   permanently inflating every org's gap and Score. (Caught 2026-06-23 when a
   15-org enrichment landed 85 open IntelActions but HasPlays stayed 0.)

   This redefines the Plays CTE to UNION both action tables (Status='Open').
   Everything else (incl. the 263 freshness dimension) is unchanged.
   ===================================================================== */
CREATE OR ALTER VIEW opportunities.vDossierCompleteness AS
WITH MpiVerdict AS (
    SELECT e.MajorProjectsInventoryId AS MpiId,
           MAX(CASE COALESCE(NULLIF(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'), ''),
                             NULLIF(JSON_VALUE(e.ResultJson, '$.verdict'), ''))
               WHEN N'PURSUE_URGENT' THEN 5.0 WHEN N'PURSUE' THEN 3.0 WHEN N'MONITOR' THEN 1.0 ELSE 0.25 END) AS VerdictW
    FROM opportunities.MajorProjectEnrichment e
    WHERE e.ProviderName = N'ProjectBriefHoning' AND e.ResultJson IS NOT NULL
    GROUP BY e.MajorProjectsInventoryId
),
OrgMpi AS (
    SELECT x.OrgId, m.Id AS MpiId
    FROM opportunities.MajorProjectsInventory m
    CROSS APPLY (VALUES (m.ArchitectCanonicalOrgId), (m.GeneralContractorCanonicalOrgId),
                        (m.StructuralEngineerCanonicalOrgId), (m.ProponentCanonicalOrgId)) x(OrgId)
    WHERE m.RetiredAtUtc IS NULL AND x.OrgId IS NOT NULL
),
OrgPursuit AS (
    SELECT om.OrgId, COUNT(*) AS PursuitLinks,
           SUM(ISNULL(v.VerdictW, 0.25)) AS PursuitVerdictWeight
    FROM OrgMpi om
    LEFT JOIN MpiVerdict v ON v.MpiId = om.MpiId
    GROUP BY om.OrgId
),
Affil AS (
    SELECT a.CanonicalOrgId, COUNT(*) AS PeopleCount,
           SUM(CASE WHEN NULLIF(LTRIM(RTRIM(p.Email)), '') IS NOT NULL THEN 1 ELSE 0 END) AS PeopleWithEmail
    FROM opportunities.IntelPersonAffiliation a
    JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId AND p.RetiredAtUtc IS NULL
    WHERE a.RetiredAtUtc IS NULL
    GROUP BY a.CanonicalOrgId
),
KeyPpl AS (
    SELECT k.CanonicalOrgId, COUNT(DISTINCT k.NormalizedName) AS KeyPeopleCount
    FROM opportunities.IntelProjectKeyPerson k
    WHERE k.RetiredAtUtc IS NULL AND k.CanonicalOrgId IS NOT NULL
    GROUP BY k.CanonicalOrgId
),
Plays AS (
    SELECT u.OrgId, SUM(u.C) AS OpenActions
    FROM (
        SELECT pa.TargetCanonicalOrgId AS OrgId, COUNT(*) AS C
        FROM opportunities.IntelProjectAction pa
        WHERE pa.RetiredAtUtc IS NULL AND pa.Status = N'Open' AND pa.TargetCanonicalOrgId IS NOT NULL
        GROUP BY pa.TargetCanonicalOrgId
        UNION ALL
        SELECT ia.CanonicalOrgId AS OrgId, COUNT(*) AS C
        FROM opportunities.IntelAction ia
        WHERE ia.RetiredAtUtc IS NULL AND ia.Status = N'Open' AND ia.CanonicalOrgId IS NOT NULL
        GROUP BY ia.CanonicalOrgId
    ) u
    GROUP BY u.OrgId
),
Fresh AS (
    SELECT e.CanonicalOrgId, MAX(e.UpdatedAtUtc) AS LastEnrichedAtUtc
    FROM opportunities.CanonicalOrgEnrichment e
    WHERE e.ProviderName IN (N'FirmNarrative', N'FirmNarrativeHoning') AND e.Status = N'ok'
    GROUP BY e.CanonicalOrgId
)
SELECT
    o.Id                                            AS CanonicalOrgId,
    o.DisplayName,
    o.Kind,
    Briefed       = CAST(CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                                           WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrative' AND e.Status = N'ok')
                              THEN 1 ELSE 0 END AS bit),
    Honed         = CAST(CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                                           WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrativeHoning' AND e.Status = N'ok')
                              THEN 1 ELSE 0 END AS bit),
    HasPeople3    = CAST(CASE WHEN ISNULL(af.PeopleCount, 0) >= 3 OR ISNULL(kp.KeyPeopleCount, 0) >= 3 THEN 1 ELSE 0 END AS bit),
    AnyEmail      = CAST(CASE WHEN ISNULL(af.PeopleWithEmail, 0) > 0 THEN 1 ELSE 0 END AS bit),
    HasPlays      = CAST(CASE WHEN ISNULL(pl.OpenActions, 0) > 0 THEN 1 ELSE 0 END AS bit),
    PursuitLinked = CAST(CASE WHEN ISNULL(op.PursuitLinks, 0) > 0 THEN 1 ELSE 0 END AS bit),
    Fresh         = CAST(CASE WHEN fr.LastEnrichedAtUtc >= DATEADD(day, -90, SYSDATETIMEOFFSET()) THEN 1 ELSE 0 END AS bit),
    LastEnrichedAtUtc  = fr.LastEnrichedAtUtc,
    PeopleCount        = ISNULL(af.PeopleCount, 0),
    PeopleWithEmail    = ISNULL(af.PeopleWithEmail, 0),
    KeyPeopleCount     = ISNULL(kp.KeyPeopleCount, 0),
    OpenActions        = ISNULL(pl.OpenActions, 0),
    PursuitLinks       = ISNULL(op.PursuitLinks, 0),
    PursuitVerdictWeight = ISNULL(op.PursuitVerdictWeight, 0),
    KindWeight = CASE o.Kind WHEN N'KorClient' THEN 5.0 WHEN N'Architect' THEN 4.0 WHEN N'GC' THEN 3.0
                             WHEN N'Developer' THEN 2.0 WHEN N'Competitor' THEN 2.0 ELSE 1.0 END,
    Importance = CASE o.Kind WHEN N'KorClient' THEN 5.0 WHEN N'Architect' THEN 4.0 WHEN N'GC' THEN 3.0
                             WHEN N'Developer' THEN 2.0 WHEN N'Competitor' THEN 2.0 ELSE 1.0 END
                 + ISNULL(op.PursuitVerdictWeight, 0),
    MissingFraction =
        ( 7.0
        - CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                            WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrative' AND e.Status = N'ok') THEN 1 ELSE 0 END
        - CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                            WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrativeHoning' AND e.Status = N'ok') THEN 1 ELSE 0 END
        - CASE WHEN ISNULL(af.PeopleCount, 0) >= 3 OR ISNULL(kp.KeyPeopleCount, 0) >= 3 THEN 1 ELSE 0 END
        - CASE WHEN ISNULL(af.PeopleWithEmail, 0) > 0 THEN 1 ELSE 0 END
        - CASE WHEN ISNULL(pl.OpenActions, 0) > 0 THEN 1 ELSE 0 END
        - CASE WHEN ISNULL(op.PursuitLinks, 0) > 0 THEN 1 ELSE 0 END
        - CASE WHEN fr.LastEnrichedAtUtc >= DATEADD(day, -90, SYSDATETIMEOFFSET()) THEN 1 ELSE 0 END
        ) / 7.0,
    Score =
        ( CASE o.Kind WHEN N'KorClient' THEN 5.0 WHEN N'Architect' THEN 4.0 WHEN N'GC' THEN 3.0
                      WHEN N'Developer' THEN 2.0 WHEN N'Competitor' THEN 2.0 ELSE 1.0 END
          + ISNULL(op.PursuitVerdictWeight, 0) )
        *
        ( ( 7.0
          - CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                              WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrative' AND e.Status = N'ok') THEN 1 ELSE 0 END
          - CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                              WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrativeHoning' AND e.Status = N'ok') THEN 1 ELSE 0 END
          - CASE WHEN ISNULL(af.PeopleCount, 0) >= 3 OR ISNULL(kp.KeyPeopleCount, 0) >= 3 THEN 1 ELSE 0 END
          - CASE WHEN ISNULL(af.PeopleWithEmail, 0) > 0 THEN 1 ELSE 0 END
          - CASE WHEN ISNULL(pl.OpenActions, 0) > 0 THEN 1 ELSE 0 END
          - CASE WHEN ISNULL(op.PursuitLinks, 0) > 0 THEN 1 ELSE 0 END
          - CASE WHEN fr.LastEnrichedAtUtc >= DATEADD(day, -90, SYSDATETIMEOFFSET()) THEN 1 ELSE 0 END
          ) / 7.0 )
FROM opportunities.CanonicalOrg o
LEFT JOIN OrgPursuit op ON op.OrgId = o.Id
LEFT JOIN Affil af ON af.CanonicalOrgId = o.Id
LEFT JOIN KeyPpl kp ON kp.CanonicalOrgId = o.Id
LEFT JOIN Plays pl ON pl.OrgId = o.Id
LEFT JOIN Fresh fr ON fr.CanonicalOrgId = o.Id
WHERE o.RetiredAtUtc IS NULL
  AND o.EnrichmentSuppressedAtUtc IS NULL
  AND o.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor', N'KorClient');
GO
