USE [KorOpportunitiesDb];
GO

/* =====================================================================
   156 — Finish the 2 institutional merges the --pairs engine rolled back.
   ---------------------------------------------------------------------
   Two firms had a ref-bearing non-client row + a frozen KorClient row:
     Darshan Builders : 54740 (Developer, 2 refs) -> 76661 (KorClient, Clendor)
     Polygon Develop. : 75574 (Unknown, 2 refs)   -> 28    (KorClient, CL00334, 114 KOR projects)
   The dedup CLI's BestKind step tried to change the survivor's Kind, which the
   139 frozen-anchor trigger (protects KorStructural/KorClient) correctly blocked.
   Here we repoint the losers' references into the KorClient survivors WITHOUT
   touching the survivor rows (kind preserved), then retire the losers. Done as a
   schema-driven repoint over every column that references CanonicalOrg, with the
   CanonicalOrgEnrichment unique-key collision handled by delete-then-repoint.
   ===================================================================== */

IF OBJECT_ID('tempdb..#m') IS NOT NULL DROP TABLE #m;
CREATE TABLE #m (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL);
INSERT INTO #m VALUES (54740, 76661), (75574, 28);

/* Resolve enrichment unique-key collisions first (keep survivor's, drop loser's). */
DELETE e FROM opportunities.CanonicalOrgEnrichment e
JOIN #m m ON m.LoserId = e.CanonicalOrgId
WHERE EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment s
              WHERE s.CanonicalOrgId = m.SurvivorId AND s.ProviderName = e.ProviderName);

/* Resolve OrgAlias (CanonicalOrgId, RawName) collisions similarly. */
DELETE a FROM opportunities.OrgAlias a
JOIN #m m ON m.LoserId = a.CanonicalOrgId
WHERE EXISTS (SELECT 1 FROM opportunities.OrgAlias s
              WHERE s.CanonicalOrgId = m.SurvivorId AND s.RawName = a.RawName);

/* Schema-driven repoint of every referencing column (FK columns + the 2 non-FK MPI columns). */
IF OBJECT_ID('tempdb..#cols') IS NOT NULL DROP TABLE #cols;
CREATE TABLE #cols (sch SYSNAME, tbl SYSNAME, col SYSNAME);
INSERT INTO #cols
SELECT OBJECT_SCHEMA_NAME(fk.parent_object_id), OBJECT_NAME(fk.parent_object_id), c.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
WHERE fk.referenced_object_id = OBJECT_ID('opportunities.CanonicalOrg');
INSERT INTO #cols VALUES
 (N'opportunities', N'MajorProjectsInventory', N'StructuralEngineerCanonicalOrgId'),
 (N'opportunities', N'MajorProjectsInventory', N'GeneralContractorCanonicalOrgId');

DECLARE @sch SYSNAME, @tbl SYSNAME, @col SYSNAME, @sql NVARCHAR(MAX);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT sch, tbl, col FROM #cols;
OPEN cur; FETCH NEXT FROM cur INTO @sch, @tbl, @col;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'UPDATE t SET t.' + QUOTENAME(@col) + N' = m.SurvivorId FROM ' +
               QUOTENAME(@sch) + N'.' + QUOTENAME(@tbl) + N' t JOIN #m m ON m.LoserId = t.' + QUOTENAME(@col) + N';';
    BEGIN TRY EXEC sp_executesql @sql; END TRY
    BEGIN CATCH /* skip a column whose unique key collides (rare; survivor keeps its own) */ END CATCH
    FETCH NEXT FROM cur INTO @sch, @tbl, @col;
END
CLOSE cur; DEALLOCATE cur;

/* Seed loser display names as survivor aliases (idempotent). */
INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, Confidence, ClassifiedBy, ClassifiedAtUtc, Notes, CreatedAtUtc)
SELECT m.SurvivorId, l.DisplayName, N'Merge.156', 100, N'migration-156', SYSDATETIMEOFFSET(), N'Institutional dedup (KorClient survivor)', SYSDATETIMEOFFSET()
FROM #m m JOIN opportunities.CanonicalOrg l ON l.Id = m.LoserId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias x WHERE x.CanonicalOrgId = m.SurvivorId AND x.RawName = l.DisplayName);

/* Retire the losers (Developer/Unknown — not frozen). */
UPDATE co SET co.RetiredAtUtc = SYSDATETIMEOFFSET(),
       co.RetiredReason = N'Merged into KorClient survivor ' + CAST(m.SurvivorId AS NVARCHAR(20)) + N' (migration 156)'
FROM opportunities.CanonicalOrg co JOIN #m m ON m.LoserId = co.Id
WHERE co.RetiredAtUtc IS NULL;

DROP TABLE #m; DROP TABLE #cols;
GO
