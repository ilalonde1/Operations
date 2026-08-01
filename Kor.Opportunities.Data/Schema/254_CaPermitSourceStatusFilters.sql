USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 254: BD audit 2026-06-19 M5.
  Add source-config status filters as a secondary guard for California permit
  sources. The provider-side stage gate is the primary protection because the
  Socrata fetcher can retry without $where after a 400.

  SF live sample confirms lowercase status values such as 'expired' and
  'complete'. The SD source in this checkout is CSV; its where value is retained
  for any future Socrata-backed retarget and is ignored by CSV fetches.
*/
BEGIN TRAN;

DECLARE @SfWhere nvarchar(max) =
    N'(proposed_units::number >= 20 OR estimated_cost::number >= 2000000) ' +
    N'AND permit_type_definition like ''%new construction%'' ' +
    N'AND (status IS NULL OR NOT(status in(''complete'',''withdrawn'',''expired'',''cancelled'',''canceled'',''disapproved'',''suspend'',''suspended'',''closed'',''void'')))';

UPDATE opportunities.OpportunitySources
SET ConfigJson = JSON_MODIFY(ConfigJson, '$.where', @SfWhere),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'CA_SocrataSF'
  AND ISJSON(ConfigJson) = 1;
PRINT 'CA_SocrataSF ConfigJson.where status filter updated: ' + CONVERT(varchar(20), @@ROWCOUNT);

DECLARE @SdWhere nvarchar(max) =
    N'(APPROVAL_DU_NET_CHANGE >= 20 OR APPROVAL_VALUATION >= 2000000) ' +
    N'AND (APPROVAL_STATUS IS NULL OR NOT(APPROVAL_STATUS in(''Complete'',''Closed'',''Withdrawn'',''Expired'',''Cancelled'',''Canceled'',''Disapproved'',''Suspend'',''Suspended'',''Void'')))';

UPDATE opportunities.OpportunitySources
SET ConfigJson = JSON_MODIFY(ConfigJson, '$.where', @SdWhere),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'CA_SocrataSanDiego'
  AND ISJSON(ConfigJson) = 1;
PRINT 'CA_SocrataSanDiego ConfigJson.where status filter updated: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 254 complete: CA permit status filters configured.';
COMMIT TRAN;
GO
