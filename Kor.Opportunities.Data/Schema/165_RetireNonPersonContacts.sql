USE [KorOpportunitiesDb];
GO

/* =====================================================================
   165 — Retire non-person "contact" records (org names, team/department
   names, bare job titles, placeholders) that slipped past
   IntelPersonNameGuard.
   ---------------------------------------------------------------------
   These pollute vOrgWarmPath counts + vReportCallSheet (e.g. "Arcadis IBI
   Group Vancouver Office" surfaced as a person). Soft-retire the person +
   all its affiliations.

   SAFETY — verified before running (see session preview):
     - Real First Nations leaders are KEPT: 'Chief <Name>' / 'Kukpi7 <Name>'
       (only role-title forms 'Chief Administrative/Project/of/and ...' retire).
     - 'Dr. <Name>' kept. Substring false-positives avoided (e.g. 'Jimmie
       Inch' not matched — org pattern requires ' Inc.' with the period).
     - Of the full RETIRE set, only 2 members lacked an obvious junk keyword
       and both were genuine placeholders ("Marta (last name not publicly
       listed)", "Stewardship Commissioner (name not publicly identified)").
   Reversible: RetiredAtUtc soft-delete, not a hard delete.

   Upstream fix (Codex, separate): strengthen IntelPersonNameGuard with the
   same org/role/team patterns + the Chief/Kukpi7/Dr carve-out so this can't
   recur. Relates to [[feedback_clean_at_source]].
   ===================================================================== */

SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();
DECLARE @reason NVARCHAR(200) = N'Non-person record (org/team/title/placeholder) — migration 165';

IF OBJECT_ID('tempdb..#retire') IS NOT NULL DROP TABLE #retire;
SELECT p.Id
INTO #retire
FROM opportunities.IntelPerson p
WHERE p.RetiredAtUtc IS NULL AND (
      p.DisplayName LIKE '% Inc.%' OR p.DisplayName LIKE '% Ltd.%' OR p.DisplayName LIKE '% LLP%' OR p.DisplayName LIKE '%Corporation%'
   OR p.DisplayName LIKE '%Partnership%' OR p.DisplayName LIKE '% Group%' OR p.DisplayName LIKE '%Developments%' OR p.DisplayName LIKE '%Holdings%'
   OR p.DisplayName LIKE '% Properties%' OR p.DisplayName LIKE '%Architects%' OR p.DisplayName LIKE '%Architecture%' OR p.DisplayName LIKE '%Associates%'
   OR p.DisplayName LIKE '%ULC%' OR p.DisplayName LIKE '%Real Estate%' OR p.DisplayName LIKE '%Team%' OR p.DisplayName LIKE '%Department%'
   OR p.DisplayName LIKE '% Office%' OR p.DisplayName LIKE '%Facilities%' OR p.DisplayName LIKE '%Head Office%' OR p.DisplayName LIKE '% Studio%'
   OR p.DisplayName LIKE '%Leadership%' OR p.DisplayName LIKE '%Procurement%' OR p.DisplayName LIKE '%Engineering Dep%' OR p.DisplayName LIKE '%Pre-Construction%'
   OR p.DisplayName LIKE '%Construction Inc%' OR p.DisplayName LIKE '%(confirm%' OR p.DisplayName LIKE '%name unknown%' OR p.DisplayName LIKE '%unconfirmed%'
   OR p.DisplayName LIKE '%not publicly%' OR p.DisplayName LIKE '%vacant%' OR p.DisplayName LIKE '%TBD%' OR p.DisplayName LIKE '%(builder)%'
   OR p.DisplayName LIKE '%(Monitor)%' OR p.DisplayName LIKE '%(role)%' OR p.DisplayName LIKE '%(name not%' OR p.DisplayName LIKE '%(Program Manager)%'
   OR p.DisplayName LIKE '%(General Inquiries)%' OR p.DisplayName LIKE 'Director of %' OR p.DisplayName LIKE 'Director, %' OR p.DisplayName LIKE 'Director /%'
   OR p.DisplayName LIKE 'VP %' OR p.DisplayName LIKE 'Executive Director,%' OR p.DisplayName LIKE 'Executive Director %' OR p.DisplayName LIKE 'Manager%'
   OR p.DisplayName LIKE 'Superintendent%' OR p.DisplayName LIKE 'Branch Manager%' OR p.DisplayName LIKE 'District Manager%' OR p.DisplayName LIKE 'Zone Manager%'
   OR p.DisplayName LIKE 'Chief Administrative%' OR p.DisplayName LIKE 'Chief Project%' OR p.DisplayName LIKE 'Chief of %' OR p.DisplayName LIKE 'Chief and %'
   OR p.DisplayName LIKE 'Secretary-Treasurer%' OR p.DisplayName LIKE 'Real Property Officer%' OR p.DisplayName LIKE 'Principal Architect%' OR p.DisplayName LIKE 'Principal /%'
   OR p.DisplayName LIKE 'Senior Director,%' OR p.DisplayName LIKE 'Infrastructure /%' OR p.DisplayName LIKE 'County Manager%' OR p.DisplayName LIKE 'Airport Manager%'
   OR p.DisplayName LIKE 'Commission Manager%' OR p.DisplayName LIKE 'Business Area Manager%' OR p.DisplayName LIKE 'Regional Emergency%' OR p.DisplayName LIKE 'Timber Sales Manager%'
   OR p.DisplayName LIKE 'CEO /%' OR p.DisplayName LIKE 'CEO Chief%' OR p.DisplayName LIKE 'Development Team%' OR p.DisplayName LIKE 'Planning Team%'
   OR p.DisplayName LIKE 'Projects Team%' OR p.DisplayName LIKE 'Capital Projects %' OR p.DisplayName LIKE 'Director / CEO%' OR p.DisplayName LIKE 'Administrator /%'
   )
   AND NOT (p.DisplayName LIKE 'Chief [A-Z]%' AND p.DisplayName NOT LIKE 'Chief Administrative%' AND p.DisplayName NOT LIKE 'Chief Project%'
            AND p.DisplayName NOT LIKE 'Chief of %' AND p.DisplayName NOT LIKE 'Chief and %' AND p.DisplayName NOT LIKE 'Chief (%')
   AND p.DisplayName NOT LIKE 'Kukpi7 %' AND p.DisplayName NOT LIKE 'Dr. %' AND p.DisplayName NOT LIKE 'Dr [A-Z]%';

/* retire affiliations of the junk persons */
UPDATE a SET a.RetiredAtUtc = @now, a.RetiredReason = @reason, a.UpdatedAtUtc = @now
FROM opportunities.IntelPersonAffiliation a
JOIN #retire r ON r.Id = a.IntelPersonId
WHERE a.RetiredAtUtc IS NULL;

/* retire the junk persons */
UPDATE p SET p.RetiredAtUtc = @now, p.RetiredReason = @reason, p.UpdatedAtUtc = @now
FROM opportunities.IntelPerson p
JOIN #retire r ON r.Id = p.Id
WHERE p.RetiredAtUtc IS NULL;

DROP TABLE #retire;
GO
