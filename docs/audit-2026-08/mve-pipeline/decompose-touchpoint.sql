/*
  decompose-touchpoint.sql   MVE, 28 August 2026

  The 27 August call with Dan Gura is a PAST event, so it is a CrmTouchpoint,
  not a CrmEngagement NextAction. Before this ran, MVE (org 76952) had ZERO
  touchpoints on file despite an hour-long call and a dossier built from it.

  Dan Gura is already IntelPerson 13539 (dgura@mve-architects.com), so this
  links to him rather than creating anything.

  NaturalKey follows the OrgFact pattern from Schema/289: SHA1 over the parts
  that make the event unique, so a re-run upserts.

  ⚠ PAST events only. Anything still to happen -- the follow-up with Matt,
    Mark Kim and Ken -- belongs on CrmEngagements.NextActionDueUtc, where the
    morning report will surface it. Do not record an intention as a touchpoint.
*/

SET NOCOUNT ON;

DECLARE @by nvarchar(100) = N'BrainDecompose-MVE-SixMarket-2026-08-28';
DECLARE @org bigint = 76952;          -- MVE + Partners
DECLARE @person bigint = 13539;       -- Daniel Gura
DECLARE @when datetimeoffset = '2026-08-27T00:00:00-07:00';

DECLARE @summary nvarchar(max) = N'Introductory call, Dan Gura (MVE + Partners). '
  + N'MVE has a full plate 18-24 months, mostly wood frame with concrete returning on the luxury condo side; no mass timber to date. '
  + N'Offices are Californian but the work is national: Arizona, Nevada, Hawaii, Houston, Charlotte, Miami - registered in all six. '
  + N'Dan asked for a package: a sample dossier plus everything submitted in Arizona, of the kind he runs in CoStar. '
  + N'Mark Kim is the selection voice; Dan undertook to pull Matt, Mark and Ken together for a follow-up next month. '
  + N'Delivered 2026-08-28 as KOR-MVE-Market-Snapshot: seven verified openings across Phoenix, Charlotte, Maui and Las Vegas, '
  + N'plus the Arizona record with a read on which layer of it is already spoken for.';

DECLARE @nk char(40) = CONVERT(char(40), HASHBYTES('SHA1', CAST(
    CAST(@org AS varchar(20)) + '|Call|' + CONVERT(varchar(10), @when, 23)
    + '|' + CAST(@person AS varchar(20)) AS varchar(8000))), 2);

MERGE opportunities.CrmTouchpoint WITH (HOLDLOCK) AS T
USING (SELECT @nk AS NK) AS S ON T.NaturalKey = S.NK
WHEN MATCHED THEN UPDATE SET Summary = @summary, OccurredAtUtc = @when
WHEN NOT MATCHED THEN INSERT
  (NaturalKey, CanonicalOrgId, IntelPersonId, Kind, OccurredAtUtc, Summary, KorStaff, CreatedBy)
  VALUES (@nk, @org, @person, N'Call', @when, @summary,
          N'Ian Lalonde; Jim DesRoches', @by);

SELECT TouchpointsOnMve = COUNT(*)
  FROM opportunities.CrmTouchpoint
 WHERE CanonicalOrgId = @org AND RetiredAtUtc IS NULL;
GO
