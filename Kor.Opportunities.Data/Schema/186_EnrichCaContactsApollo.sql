/*
186_EnrichCaContactsApollo.sql
WHAT: Fold Apollo enrichment (2026-06-18) into the CA decision-maker IntelPerson rows:
      Ryan Bosa's email (the Hunter gap) + LinkedIn URLs.
HOW:  UPDATE by NaturalKey (SHA1 of stripped-normalized DisplayName), COALESCE so existing
      verified emails are never overwritten (Bosa's NULL email gets filled; LinkedIn fills
      where empty). Titles left as-is (the synthesis titles are more current than Apollo's).
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();

DECLARE @u TABLE (DisplayName nvarchar(200), Email nvarchar(200), Linkedin nvarchar(500), NormName nvarchar(200), PKey char(40));
INSERT INTO @u (DisplayName, Email, Linkedin) VALUES
 (N'John Wayland',     NULL,                            N'http://www.linkedin.com/in/john-wayland-3b354b6'),
 (N'Michael Eadie',    NULL,                            N'http://www.linkedin.com/in/michaeleadie'),
 (N'Pablo Collin',     NULL,                            N'http://www.linkedin.com/in/pablo-collin-74129334'),
 (N'Matthew McLarand', NULL,                            N'http://www.linkedin.com/in/matthew-mclarand-34091713'),
 (N'Chase Ronge',      NULL,                            N'http://www.linkedin.com/in/chaseronge'),
 (N'Craig Howard',     NULL,                            N'http://www.linkedin.com/in/craig-howard-ab0845358'),
 (N'Ryan Bosa',        N'rbosa@bosadevelopment.com',    NULL);

UPDATE @u SET NormName = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
   LOWER(LTRIM(RTRIM(DisplayName))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N'');
UPDATE @u SET PKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(NormName AS VARCHAR(8000))), 2);

UPDATE p SET
  p.Email       = COALESCE(p.Email, u.Email),
  p.LinkedinUrl = COALESCE(p.LinkedinUrl, u.Linkedin),
  p.Corroborations = p.Corroborations + 1,
  p.LastSeenAtUtc = @now, p.UpdatedAtUtc = @now
FROM opportunities.IntelPerson p
JOIN @u u ON p.NaturalKey = u.PKey;

COMMIT;

SELECT p.DisplayName, p.Email, p.LinkedinUrl, p.Corroborations
FROM opportunities.IntelPerson p
JOIN @u u ON p.NaturalKey = u.PKey
ORDER BY p.DisplayName;
