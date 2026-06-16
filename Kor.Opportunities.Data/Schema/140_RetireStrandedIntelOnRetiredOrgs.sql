USE [KorOpportunitiesDb];
GO

/* =====================================================================
   140 — Retire intel children stranded on retired CanonicalOrg rows.
   ---------------------------------------------------------------------
   Realized retirement-cascade bug (apparatus audit 2026-06-15): org-retire
   migrations (137 et al.) set RetiredAtUtc on CanonicalOrg only and never
   cascaded to the org's Intel* children, leaving active intel attached to
   retired parents. The runtime check found ~323+ such rows (signals 102,
   actions 87, narratives 74, affiliations 60, plus works/risks). The
   IntelReadService per-org dossier guard (batch 1) already STOPS surfacing
   them; this one-time cleanup retires them so the data is consistent and
   they drop out of every reader that filters child RetiredAtUtc.

   Going forward they cannot be re-created: BdIntelExtract now excludes
   retired-org enrichment at the source and IntelPersistenceService refuses
   to write intel to a non-live org (batch 2).

   Idempotent: each UPDATE filters RetiredAtUtc IS NULL.
   ===================================================================== */

UPDATE s SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'parent org retired (m140 cascade cleanup)'
FROM opportunities.IntelSignal s
JOIN opportunities.CanonicalOrg c ON c.Id = s.CanonicalOrgId
WHERE c.RetiredAtUtc IS NOT NULL AND s.RetiredAtUtc IS NULL;
GO

UPDATE a SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'parent org retired (m140 cascade cleanup)'
FROM opportunities.IntelAction a
JOIN opportunities.CanonicalOrg c ON c.Id = a.CanonicalOrgId
WHERE c.RetiredAtUtc IS NOT NULL AND a.RetiredAtUtc IS NULL;
GO

UPDATE w SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'parent org retired (m140 cascade cleanup)'
FROM opportunities.IntelWork w
JOIN opportunities.CanonicalOrg c ON c.Id = w.CanonicalOrgId
WHERE c.RetiredAtUtc IS NOT NULL AND w.RetiredAtUtc IS NULL;
GO

UPDATE r SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'parent org retired (m140 cascade cleanup)'
FROM opportunities.IntelRisk r
JOIN opportunities.CanonicalOrg c ON c.Id = r.CanonicalOrgId
WHERE c.RetiredAtUtc IS NOT NULL AND r.RetiredAtUtc IS NULL;
GO

UPDATE n SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'parent org retired (m140 cascade cleanup)'
FROM opportunities.IntelNarrative n
JOIN opportunities.CanonicalOrg c ON c.Id = n.CanonicalOrgId
WHERE c.RetiredAtUtc IS NOT NULL AND n.RetiredAtUtc IS NULL;
GO

UPDATE af SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'parent org retired (m140 cascade cleanup)'
FROM opportunities.IntelPersonAffiliation af
JOIN opportunities.CanonicalOrg c ON c.Id = af.CanonicalOrgId
WHERE c.RetiredAtUtc IS NOT NULL AND af.RetiredAtUtc IS NULL;
GO
