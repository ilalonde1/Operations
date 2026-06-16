USE [KorOpportunitiesDb];
GO

/* =====================================================================
   151 — Resolve US / ON / YT rows mis-tagged Province='BC'.
   ---------------------------------------------------------------------
   ~126 MajorProjectsInventory rows for US-west-coast (+ a few ON/YT) cities
   carried Province='BC', polluting BC province counts. Investigation via the
   UX_MPI_Province_SourceKey unique key showed two distinct situations:

     - CA / OR / WA (122 rows): EVERY one already has a same-SourceKey twin
       correctly tagged CA/OR/WA. They are duplicate double-ingestions; the
       correct-province copy already exists. -> RETIRE the redundant BC copy
       (archive-not-delete, consistent with the DataRetirementJob pattern).
     - ON / YT (4 rows): no twin exists. -> RE-PROVINCE to the correct code.

   The DB already uses 2-letter codes for non-BC/AB (CA 454, OR 80, WA 50).
   Cariboo / remote-BC names (Quesnel, Williams Lake, 100 Mile House, Anahim
   Lake, Lytton, Wells, First-Nations IRs, Peace River North) are genuinely BC
   and untouched. Idempotent.
   ===================================================================== */

IF OBJECT_ID('tempdb..#ust') IS NOT NULL DROP TABLE #ust;
SELECT m.Id, m.SourceKey,
  CASE
    WHEN m.MunicipalityName IN (N'Los Angeles',N'San Diego',N'Irvine',N'Riverside',N'Santa Barbara',
         N'Long Beach',N'Fullerton',N'Baldwin Park',N'Emeryville',N'Marina del Rey',N'Northridge',
         N'Oakland',N'Sacramento',N'San Francisco',N'San Leandro',N'San Rafael',N'Santa Clara',N'Victorville')
         OR m.MunicipalityName LIKE N'%La Jolla%' THEN N'CA'
    WHEN m.MunicipalityName IN (N'Seattle',N'Pullman',N'Bothell',N'Puyallup',N'Spokane',N'Wenatchee',
         N'King County',N'East King County',N'South King County') THEN N'WA'
    WHEN m.MunicipalityName IN (N'Portland',N'Corvallis',N'Eugene',N'Gresham') THEN N'OR'
    WHEN m.MunicipalityName IN (N'Toronto',N'Ottawa') THEN N'ON'
    WHEN m.MunicipalityName = N'Whitehorse' THEN N'YT'
  END AS Target
INTO #ust
FROM opportunities.MajorProjectsInventory m
WHERE m.Province = N'BC';

DELETE FROM #ust WHERE Target IS NULL;

/* Mark which ones already have a correct-province twin (= duplicate). */
ALTER TABLE #ust ADD HasTwin BIT;
UPDATE u SET HasTwin =
  CASE WHEN EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory t
                    WHERE t.Province = u.Target AND t.SourceKey = u.SourceKey AND t.Id <> u.Id)
       THEN 1 ELSE 0 END
FROM #ust u;

/* 1. Unique (no twin) -> re-province to the correct code. */
UPDATE m SET m.Province = u.Target, m.UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.MajorProjectsInventory m
JOIN #ust u ON u.Id = m.Id
WHERE u.HasTwin = 0 AND m.Province = N'BC';

/* 2. Duplicate (twin exists) -> retire the redundant BC copy. */
UPDATE m SET m.RetiredAtUtc = SYSDATETIMEOFFSET(),
       m.RetiredReason = N'Duplicate of correctly-provinced ' + u.Target + N' twin (migration 151)'
FROM opportunities.MajorProjectsInventory m
JOIN #ust u ON u.Id = m.Id
WHERE u.HasTwin = 1 AND m.Province = N'BC' AND m.RetiredAtUtc IS NULL;

DROP TABLE #ust;
GO
