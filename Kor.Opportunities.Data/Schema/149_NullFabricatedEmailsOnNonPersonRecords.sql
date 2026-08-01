USE [KorOpportunitiesDb];
GO

/* =====================================================================
   149 — Null fabricated PatternInferred emails on non-person records.
   ---------------------------------------------------------------------
   17 IntelPerson rows are not people at all — they are firm names, municipal
   department/team names, or role titles ("HCMA Architecture", "City of Victoria
   Engineering Department", "Director of Engineering", "BC Housing — Development
   Consulting Team", "VP Construction / VP Development (unconfirmed)"). The free
   pattern-propagation fabricated an email for each by slugging the name onto a
   guessed domain (e.g. dengineering@whistler.ca, harchitecture@weiwaikum.ca,
   ubc.team@ubc.ca, vunconfirmed@polyhomes.com). These are fabricated contacts
   and a presentation hazard.

   Scope is deliberately narrow + safe: EmailSource='PatternInferred' ONLY (never
   touches a verified asis/Hunter email) AND DisplayName carries a firm/team/role
   marker (never a real personal name). The records themselves are left in place
   (they may anchor a firm->project signal); only the fabricated email is cleared.

   The broader affiliation-pollution problem (real people mis-affiliated to the
   wrong firm at high confidence — e.g. Concord Pacific staff under W.T. Leung,
   Grosvenor staff under Hariri Pontarini, Urban One staff under Acton Ostry)
   is a verified re-homing pass, NOT auto-mutated here (relationship data is not
   guessed — see feedback_no_guessing / feedback_honing_merge_audit).
   ===================================================================== */

UPDATE p
SET p.Email = NULL, p.EmailSource = NULL, p.EmailConfidence = NULL, p.EmailCheckedAtUtc = NULL
FROM opportunities.IntelPerson p
WHERE p.RetiredAtUtc IS NULL
  AND p.EmailSource = N'PatternInferred'
  AND NULLIF(LTRIM(RTRIM(p.Email)), '') IS NOT NULL
  AND ( p.DisplayName LIKE N'%Architect%'  OR p.DisplayName LIKE N'%Engineer%'
     OR p.DisplayName LIKE N'%Consulting%' OR p.DisplayName LIKE N'%Architecture%'
     OR p.DisplayName LIKE N'% Inc.%'      OR p.DisplayName LIKE N'% Ltd%'
     OR p.DisplayName LIKE N'%Partnership%'OR p.DisplayName LIKE N'% Group%'
     OR p.DisplayName LIKE N'% Team%'      OR p.DisplayName LIKE N'Director%'
     OR p.DisplayName LIKE N'Manager%'     OR p.DisplayName LIKE N'Board %'
     OR p.DisplayName LIKE N'VP %'         OR p.DisplayName LIKE N'Branch %'
     OR p.DisplayName LIKE N'%Design-Build%' );
GO
