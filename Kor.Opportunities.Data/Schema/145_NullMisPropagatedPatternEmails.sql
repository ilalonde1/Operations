USE [KorOpportunitiesDb];
GO

/* =====================================================================
   145 — Null mis-propagated PatternInferred emails (wrong-domain).
   ---------------------------------------------------------------------
   The free pattern-propagation derived a firm's email format from its known
   emails — but where a firm's affiliations were polluted with people from a
   DIFFERENT company (e.g. Acton Ostry's roster contained Urban One Builders /
   JWest people on @urbanonebuilders.com), the derived domain was foreign and got
   propagated onto the real firm's people. This nulls PatternInferred emails whose
   domain CONFLICTS with the firm's own known website (firm has a website, but the
   inferred email is on a different domain). Website-less firms are left alone
   (can't adjudicate; already flagged conf 55). The 644 own-domain inferred emails
   are untouched.

   Durable fix: ContactEnrichmentService.PatternPropagate now only propagates when
   the derived domain matches the firm's own website domain.
   ===================================================================== */

UPDATE p
SET Email = NULL, EmailSource = NULL, EmailConfidence = NULL, EmailCheckedAtUtc = NULL
FROM opportunities.IntelPerson p
WHERE p.EmailSource = N'PatternInferred'
  AND EXISTS (
        SELECT 1 FROM opportunities.IntelPersonAffiliation a
        JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
        WHERE a.IntelPersonId = p.Id AND a.RetiredAtUtc IS NULL
          AND NULLIF(LTRIM(RTRIM(co.Website)), '') IS NOT NULL)
  AND NOT EXISTS (
        SELECT 1 FROM opportunities.IntelPersonAffiliation a
        JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
        WHERE a.IntelPersonId = p.Id AND a.RetiredAtUtc IS NULL
          AND co.Website LIKE '%' + SUBSTRING(p.Email, CHARINDEX('@', p.Email) + 1, 200) + '%');
GO
