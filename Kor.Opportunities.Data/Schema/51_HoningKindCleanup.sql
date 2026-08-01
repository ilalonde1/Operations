USE [KorOpportunitiesDb];
GO

/* =====================================================================
   Round 57 — Data-honing kind cleanup.
   Fixes 5 mis-kinded canonical orgs surfaced by the KOR-Data-Honing
   passes (verified against the live graph 2026-05-27). Guarded by the
   current kind so re-running is a no-op. Opp prime-RFP reclassifications
   from the same honing run needed NO change — the live classifier state
   already matches (718/468/561/640/10803 already False, 690 already True).
   ===================================================================== */

-- PGH Consulting Services: CM/GC firm in Courtenay BC, not an architect.
UPDATE opportunities.CanonicalOrg SET Kind='GC', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=69276 AND Kind='Architect';
PRINT 'PGH Consulting Services -> GC: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Greyback Construction / Developments: primarily a GC (Penticton), not a developer.
UPDATE opportunities.CanonicalOrg SET Kind='GC', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=55053 AND Kind='Developer';
PRINT 'Greyback -> GC: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Kontur: geotechnical engineering / materials testing — a sub-consultant, not a structural competitor.
UPDATE opportunities.CanonicalOrg SET Kind='Subcontractor', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=69471 AND Kind='Competitor';
PRINT 'Kontur -> Subcontractor: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Pembina Pipeline Corporation: energy/pipeline company, NOT a real-estate developer. Demote out of curated.
UPDATE opportunities.CanonicalOrg SET Kind='Vendor', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=55055 AND Kind='Developer';
PRINT 'Pembina -> Vendor: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Generator Studio: Kansas City MO architect — out of KOR markets. Demote out of the curated Architect browse.
UPDATE opportunities.CanonicalOrg SET Kind='Vendor', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=68905 AND Kind='Architect';
PRINT 'Generator Studio -> Vendor (out-of-market): ' + CAST(@@ROWCOUNT AS varchar(10));

PRINT 'Migration 51: honing kind cleanup complete.';
GO
