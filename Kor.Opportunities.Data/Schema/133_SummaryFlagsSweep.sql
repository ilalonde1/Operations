-- 133_SummaryFlagsSweep.sql
-- First systematic harvest of session-summary flags (the honing sessions
-- file evidenced corrections in SUMMARY-*.txt; until 2026-06-12 nobody
-- read them — found when Ian pointed at the competitor misclassification
-- table). Mechanical-confidence set only; verify-class items live in
-- docs/BD-SummaryFlags-Worklist-2026-06-12.md.
SET XACT_ABORT ON;
GO
BEGIN TRAN;

-- Deceased people (evidence: memorials/obituaries cited in summaries).
UPDATE p SET RetiredAtUtc = sysdatetimeoffset(),
  RetiredReason = N'm133: deceased per honing research (summary flag)',
  UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPerson p
WHERE p.Id IN (3757, 138, 317, 50, 53) AND p.RetiredAtUtc IS NULL;
PRINT 'Deceased persons retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Misclassified orgs (oil sands co, concrete sub, oilfield civil, municipal
-- civil — all evidenced non-BD-kind in summaries).
UPDATE co SET Kind = N'Unknown',
  EnrichmentSuppressedAtUtc = COALESCE(co.EnrichmentSuppressedAtUtc, sysdatetimeoffset()),
  EnrichmentSuppressedReason = COALESCE(co.EnrichmentSuppressedReason, N'm133: misclassified kind per honing research (summary flag)'),
  UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg co
WHERE co.Id IN (53319, 13820, 13875, 16470) AND co.RetiredAtUtc IS NULL;
PRINT 'Misclassified orgs reclassified: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Defunct/absorbed orgs (bankrupt or acquired-and-dissolved per research).
UPDATE co SET RetiredAtUtc = sysdatetimeoffset(),
  RetiredReason = CASE co.Id
    WHEN 74293 THEN N'm133: Oceanwide Holdings defunct/bankrupt (summary flag)'
    WHEN 16365 THEN N'm133: Switch Engineering acquired by Integral Group 2022 (summary flag)'
    WHEN 16473 THEN N'm133: Taifa Engineering acquired by RESA Power 2023 (summary flag)'
    WHEN 21441 THEN N'm133: SRA Architects permanently closed, founder deceased (summary flag)'
    WHEN 54282 THEN N'm133: Hotson Bakker Boniface Haden defunct since 2010 merger; successor DIALOG (summary flag)'
    WHEN 17074 THEN N'm133: Toker & Associates dissolved into Lemay Calgary 2017 (summary flag)'
    WHEN 7397  THEN N'm133: Field Field & Field dissolved 2014 (summary flag)'
  END,
  UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg co
WHERE co.Id IN (74293, 16365, 16473, 21441, 54282, 17074, 7397) AND co.RetiredAtUtc IS NULL;
PRINT 'Defunct orgs retired: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
GO