USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 256: BD audit close-out 2026-06-20 M5.
  Exclude issued/construction-stage CA permits from the pre-selection funnel.
  Issued building permits imply stamped structural drawings exist, so the SE
  selection window has passed.
*/
BEGIN TRAN;

DECLARE @SfWhere nvarchar(max) =
    N'(proposed_units::number >= 20 OR estimated_cost::number >= 2000000) ' +
    N'AND permit_type_definition like ''%new construction%'' ' +
    N'AND (status IS NULL OR NOT(status in(''complete'',''withdrawn'',''expired'',''cancelled'',''canceled'',''disapproved'',''suspend'',''suspended'',''closed'',''void'',''issued'')))';

UPDATE opportunities.OpportunitySources
SET ConfigJson = JSON_MODIFY(ConfigJson, '$.where', @SfWhere),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'CA_SocrataSF'
  AND ISJSON(ConfigJson) = 1;
PRINT 'CA_SocrataSF ConfigJson.where post-selection issued-stage filter updated: ' + CONVERT(varchar(20), @@ROWCOUNT);

DECLARE @SdWhere nvarchar(max) =
    N'(APPROVAL_DU_NET_CHANGE >= 20 OR APPROVAL_VALUATION >= 2000000) ' +
    N'AND (APPROVAL_STATUS IS NULL OR NOT(APPROVAL_STATUS in(''Complete'',''Closed'',''Withdrawn'',''Expired'',''Cancelled'',''Canceled'',''Disapproved'',''Suspend'',''Suspended'',''Void'',''Issued'',''Inspection Followup'')))';

UPDATE opportunities.OpportunitySources
SET ConfigJson = JSON_MODIFY(ConfigJson, '$.where', @SdWhere),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'CA_SocrataSanDiego'
  AND ISJSON(ConfigJson) = 1;
PRINT 'CA_SocrataSanDiego ConfigJson.where post-selection construction-stage filter updated: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'm256: post-selection permit stage (issued/construction); reversible CA pre-selection funnel filter',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE RetiredAtUtc IS NULL
  AND
  (
      (SourceKey LIKE N'sf:%' AND Stage = N'issued')
      OR
      (SourceKey LIKE N'sdcity:%' AND Stage IN (N'Issued', N'Inspection Followup'))
  );
PRINT 'CA issued/construction-stage active MPI rows soft-retired: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 256 complete: CA post-selection permit stages excluded and active rows soft-retired.';
COMMIT TRAN;
GO
