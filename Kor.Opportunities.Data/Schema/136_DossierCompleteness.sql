USE [KorOpportunitiesDb];
GO

/* =====================================================================
   136 — Dossier Completeness Engine: scoring views.
   ---------------------------------------------------------------------
   The "smart backfiller" substrate. Three views rank where the BD graph
   is thin, so gap-fill research batches can burn the holes down nightly
   instead of Ian discovering them in demos.

   1) opportunities.vDossierCompleteness — per active, non-suppressed org
      of the six BD kinds (Architect/GC/Developer/Buyer/Competitor/
      KorClient): six dossier flags (briefed, honed, people>=3, anyEmail,
      hasPlays, pursuitLinked), an importance weight (kind priority +
      pursuit links weighted by honing verdict), and
      Score = Importance x MissingFraction.

   2) opportunities.vDossierCompletenessPeople — selection authorities
      (people currently affiliated with an org that carries PURSUE /
      PURSUE_URGENT work) missing email / LinkedIn.

   3) opportunities.vDossierCompletenessPursuits — PURSUE/PURSUE_URGENT
      MPIs missing team edges (architect / structural engineer — the
      m-graph-completion targets from Generate-Batch -PursuitGaps).

   Verdicts read the same JSON path Generate-Batch.ps1 uses:
   $.honingPass.verdict falling back to $.verdict on the
   ProjectBriefHoning enrichment row.
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
    SELECT pa.TargetCanonicalOrgId, COUNT(*) AS OpenActions
    FROM opportunities.IntelProjectAction pa
    WHERE pa.RetiredAtUtc IS NULL AND pa.Status = N'Open' AND pa.TargetCanonicalOrgId IS NOT NULL
    GROUP BY pa.TargetCanonicalOrgId
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
        ( 6.0
        - CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                            WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrative' AND e.Status = N'ok') THEN 1 ELSE 0 END
        - CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                            WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrativeHoning' AND e.Status = N'ok') THEN 1 ELSE 0 END
        - CASE WHEN ISNULL(af.PeopleCount, 0) >= 3 OR ISNULL(kp.KeyPeopleCount, 0) >= 3 THEN 1 ELSE 0 END
        - CASE WHEN ISNULL(af.PeopleWithEmail, 0) > 0 THEN 1 ELSE 0 END
        - CASE WHEN ISNULL(pl.OpenActions, 0) > 0 THEN 1 ELSE 0 END
        - CASE WHEN ISNULL(op.PursuitLinks, 0) > 0 THEN 1 ELSE 0 END
        ) / 6.0,
    Score =
        ( CASE o.Kind WHEN N'KorClient' THEN 5.0 WHEN N'Architect' THEN 4.0 WHEN N'GC' THEN 3.0
                      WHEN N'Developer' THEN 2.0 WHEN N'Competitor' THEN 2.0 ELSE 1.0 END
          + ISNULL(op.PursuitVerdictWeight, 0) )
        *
        ( ( 6.0
          - CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                              WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrative' AND e.Status = N'ok') THEN 1 ELSE 0 END
          - CASE WHEN EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                              WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = N'FirmNarrativeHoning' AND e.Status = N'ok') THEN 1 ELSE 0 END
          - CASE WHEN ISNULL(af.PeopleCount, 0) >= 3 OR ISNULL(kp.KeyPeopleCount, 0) >= 3 THEN 1 ELSE 0 END
          - CASE WHEN ISNULL(af.PeopleWithEmail, 0) > 0 THEN 1 ELSE 0 END
          - CASE WHEN ISNULL(pl.OpenActions, 0) > 0 THEN 1 ELSE 0 END
          - CASE WHEN ISNULL(op.PursuitLinks, 0) > 0 THEN 1 ELSE 0 END
          ) / 6.0 )
FROM opportunities.CanonicalOrg o
LEFT JOIN OrgPursuit op ON op.OrgId = o.Id
LEFT JOIN Affil af ON af.CanonicalOrgId = o.Id
LEFT JOIN KeyPpl kp ON kp.CanonicalOrgId = o.Id
LEFT JOIN Plays pl ON pl.TargetCanonicalOrgId = o.Id
WHERE o.RetiredAtUtc IS NULL
  AND o.EnrichmentSuppressedAtUtc IS NULL
  AND o.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor', N'KorClient');
GO

CREATE OR ALTER VIEW opportunities.vDossierCompletenessPeople AS
WITH MpiVerdict AS (
    SELECT e.MajorProjectsInventoryId AS MpiId,
           MAX(CASE COALESCE(NULLIF(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'), ''),
                             NULLIF(JSON_VALUE(e.ResultJson, '$.verdict'), ''))
               WHEN N'PURSUE_URGENT' THEN 5.0 WHEN N'PURSUE' THEN 3.0 ELSE 0 END) AS VerdictW
    FROM opportunities.MajorProjectEnrichment e
    WHERE e.ProviderName = N'ProjectBriefHoning' AND e.ResultJson IS NOT NULL
    GROUP BY e.MajorProjectsInventoryId
),
OrgPursuit AS (
    SELECT x.OrgId, COUNT(*) AS PursuitCount, SUM(v.VerdictW) AS PursuitWeight
    FROM opportunities.MajorProjectsInventory m
    JOIN MpiVerdict v ON v.MpiId = m.Id AND v.VerdictW > 0
    CROSS APPLY (VALUES (m.ArchitectCanonicalOrgId), (m.GeneralContractorCanonicalOrgId),
                        (m.StructuralEngineerCanonicalOrgId), (m.ProponentCanonicalOrgId)) x(OrgId)
    WHERE m.RetiredAtUtc IS NULL AND x.OrgId IS NOT NULL
    GROUP BY x.OrgId
)
SELECT
    p.Id            AS IntelPersonId,
    p.DisplayName,
    a.Title,
    a.CanonicalOrgId,
    OrgName         = o.DisplayName,
    OrgKind         = o.Kind,
    HasEmail        = CAST(CASE WHEN NULLIF(LTRIM(RTRIM(p.Email)), '') IS NOT NULL THEN 1 ELSE 0 END AS bit),
    HasLinkedin     = CAST(CASE WHEN NULLIF(LTRIM(RTRIM(p.LinkedinUrl)), '') IS NOT NULL THEN 1 ELSE 0 END AS bit),
    HasPhone        = CAST(CASE WHEN NULLIF(LTRIM(RTRIM(p.Phone)), '') IS NOT NULL THEN 1 ELSE 0 END AS bit),
    PursuitCount    = op.PursuitCount,
    PursuitWeight   = op.PursuitWeight,
    MissingFraction =
        ( 2.0
        - CASE WHEN NULLIF(LTRIM(RTRIM(p.Email)), '') IS NOT NULL THEN 1 ELSE 0 END
        - CASE WHEN NULLIF(LTRIM(RTRIM(p.LinkedinUrl)), '') IS NOT NULL THEN 1 ELSE 0 END
        ) / 2.0,
    Score = op.PursuitWeight *
        ( ( 2.0
          - CASE WHEN NULLIF(LTRIM(RTRIM(p.Email)), '') IS NOT NULL THEN 1 ELSE 0 END
          - CASE WHEN NULLIF(LTRIM(RTRIM(p.LinkedinUrl)), '') IS NOT NULL THEN 1 ELSE 0 END
          ) / 2.0 )
FROM opportunities.IntelPerson p
JOIN opportunities.IntelPersonAffiliation a ON a.IntelPersonId = p.Id AND a.IsCurrent = 1 AND a.RetiredAtUtc IS NULL
JOIN opportunities.CanonicalOrg o ON o.Id = a.CanonicalOrgId AND o.RetiredAtUtc IS NULL
JOIN OrgPursuit op ON op.OrgId = a.CanonicalOrgId
WHERE p.RetiredAtUtc IS NULL;
GO

CREATE OR ALTER VIEW opportunities.vDossierCompletenessPursuits AS
WITH MpiVerdict AS (
    SELECT e.MajorProjectsInventoryId AS MpiId,
           MAX(CASE COALESCE(NULLIF(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'), ''),
                             NULLIF(JSON_VALUE(e.ResultJson, '$.verdict'), ''))
               WHEN N'PURSUE_URGENT' THEN 5.0 WHEN N'PURSUE' THEN 3.0 ELSE 0 END) AS VerdictW
    FROM opportunities.MajorProjectEnrichment e
    WHERE e.ProviderName = N'ProjectBriefHoning' AND e.ResultJson IS NOT NULL
    GROUP BY e.MajorProjectsInventoryId
)
SELECT
    m.Id            AS MajorProjectsInventoryId,
    m.ProjectName,
    m.Province,
    m.Stage,
    m.EstimatedCostCad,
    VerdictWeight   = v.VerdictW,
    HasArchitectEdge = CAST(CASE WHEN m.ArchitectCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END AS bit),
    HasSeEdge        = CAST(CASE WHEN m.StructuralEngineerCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END AS bit),
    HasGcEdge        = CAST(CASE WHEN m.GeneralContractorCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END AS bit),
    HasProponentEdge = CAST(CASE WHEN m.ProponentCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END AS bit),
    KeyPeopleCount   = (SELECT COUNT(*) FROM opportunities.IntelProjectKeyPerson k
                        WHERE k.MajorProjectsInventoryId = m.Id AND k.RetiredAtUtc IS NULL),
    OpenActions      = (SELECT COUNT(*) FROM opportunities.IntelProjectAction pa
                        WHERE pa.MajorProjectsInventoryId = m.Id AND pa.RetiredAtUtc IS NULL AND pa.Status = N'Open'),
    MissingFraction =
        ( 2.0
        - CASE WHEN m.ArchitectCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END
        - CASE WHEN m.StructuralEngineerCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END
        ) / 2.0,
    Score = v.VerdictW *
        ( ( 2.0
          - CASE WHEN m.ArchitectCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END
          - CASE WHEN m.StructuralEngineerCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END
          ) / 2.0 )
FROM opportunities.MajorProjectsInventory m
JOIN MpiVerdict v ON v.MpiId = m.Id AND v.VerdictW > 0
WHERE m.RetiredAtUtc IS NULL;
GO
