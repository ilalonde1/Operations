USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 205: re-hone off new evidence.
  Rossano De Cotiis already had a real (asis) Onni address: rdecotiis@onni.com
  (no hyphen). That verifies Onni's pattern = {first-initial}{lastname,no spaces}
  @onni.com — so Hunter's hyphenated guesses (m/g/p "de-cotiis") for his
  brothers are the wrong format. Correct them and raise confidence (now
  corroborated by a sibling's verified address). Still PatternInferred (not
  individually verified).
  Also flag Beau Jarvis: on file as bjarvis@wesgroup.ca (Wesgroup, Vancouver).
  The research agent's "Onni VP Development, LA" claim conflicts with that and is
  unverified — note it for follow-up rather than trusting the Onni affiliation.
*/

BEGIN TRAN;

UPDATE p SET
    Email = N'mdecotiis@onni.com', EmailConfidence = 78,
    Notes = N'Pattern-inferred; format corroborated by Rossano De Cotiis verified address rdecotiis@onni.com.',
    EmailCheckedAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPerson p WHERE p.NormalizedName = N'morris de cotiis' AND p.EmailSource = N'PatternInferred';

UPDATE p SET
    Email = N'gdecotiis@onni.com', EmailConfidence = 78,
    Notes = N'Pattern-inferred; format corroborated by Rossano De Cotiis verified address rdecotiis@onni.com.',
    EmailCheckedAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPerson p WHERE p.NormalizedName = N'giulio de cotiis' AND p.EmailSource = N'PatternInferred';

UPDATE p SET
    Email = N'pdecotiis@onni.com', EmailConfidence = 78,
    Notes = N'Pattern-inferred; format corroborated by Rossano De Cotiis verified address rdecotiis@onni.com.',
    EmailCheckedAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPerson p WHERE p.NormalizedName = N'paolo de cotiis' AND p.EmailSource = N'PatternInferred';

UPDATE p SET
    Notes = LEFT(COALESCE(p.Notes + N' ', N'') + N'CONFLICT: on file at Wesgroup (Vancouver); research-claimed Onni VP Development LA is UNVERIFIED — confirm before relying on the Onni affiliation.', 4000),
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPerson p WHERE p.NormalizedName = N'beau jarvis';

PRINT 'Migration 205: De Cotiis email pattern corrected; Beau Jarvis conflict flagged.';
COMMIT TRAN;
GO
