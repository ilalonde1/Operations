-- 120_LoblawParentRemint.sql
-- BD-Audit-2026-06-09 (m104 Major, deferred from the gate sweep): m104
-- folded "Loblaw Properties Ltd." (53602, the NATIONAL parent) into
-- "Loblaw Properties West Inc." (53331, the BC regional subsidiary) and
-- hard-deleted the parent — a category error, not a name-variant merge.
-- The deleted row cannot be restored, but the distinction can: re-mint
-- the parent canonical and move the alias (R95DirectDedup104, alias id
-- 100228) so future national-entity mentions resolve to the parent
-- instead of the BC subsidiary. No FK rows existed for the parent at
-- merge time beyond what m104 repointed to West; those stay on West
-- (they were BC work).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

DECLARE @parentId bigint;
SELECT @parentId = Id FROM opportunities.CanonicalOrg
WHERE NormalizedName = N'loblawpropertiesltd' AND RetiredAtUtc IS NULL;

IF @parentId IS NULL
BEGIN
    -- NormalizedName is computed from DisplayName — not insertable.
    INSERT INTO opportunities.CanonicalOrg
        (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
    VALUES
        (N'Developer', N'Loblaw Properties Ltd.',
         N'[m120: re-minted — m104 erroneously merged the national parent into Loblaw Properties West Inc. (53331, BC subsidiary) and deleted it. Distinct legal entities: parent = national real-estate arm of Loblaw Companies; West = BC/AB regional. BC project FKs deliberately remain on 53331.]',
         sysdatetimeoffset(), sysdatetimeoffset());
    SET @parentId = CONVERT(bigint, SCOPE_IDENTITY());
    PRINT 'Loblaw Properties Ltd. re-minted with Id: ' + CAST(@parentId AS varchar(12));
END
ELSE
    PRINT 'Loblaw Properties Ltd. already active with Id: ' + CAST(@parentId AS varchar(12));

UPDATE opportunities.OrgAlias
SET CanonicalOrgId = @parentId,
    Notes = N'm120: moved from 53331 — parent-entity name must not resolve to the BC subsidiary'
WHERE RawName = N'Loblaw Properties Ltd.' AND CanonicalOrgId <> @parentId;
PRINT 'Alias moved: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm120 committed.';
GO

SELECT o.Id, o.DisplayName, o.Kind, a.RawName AS Alias
FROM opportunities.CanonicalOrg o
LEFT JOIN opportunities.OrgAlias a ON a.CanonicalOrgId = o.Id
WHERE o.NormalizedName IN (N'loblawpropertiesltd', N'loblawpropertieswestinc');
GO
