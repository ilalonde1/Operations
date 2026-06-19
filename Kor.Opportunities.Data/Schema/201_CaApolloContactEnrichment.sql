USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 201: Apollo enrichment for the CA contacts extracted in migration 199.
  Only the 10 matches whose Apollo company aligned with the requested firm are
  persisted (org-mismatch false matches like "Kevin Willis -> Swinerton" were
  rejected during collection). All emails are Apollo email_status=verified.
  Email is COALESCEd so any existing on-file email is never clobbered.
*/

BEGIN TRAN;

DECLARE @work TABLE (PersonId bigint, Email nvarchar(200), Title nvarchar(200), LinkedIn nvarchar(500));
INSERT INTO @work (PersonId, Email, Title, LinkedIn) VALUES
 (13623, N'abonasera@devcon-const.com',   N'Project Manager',                        N'http://www.linkedin.com/in/anthony-bonasera-b9364379'),
 (13626, N'cepperson@arcomurray.com',     N'Principal',                              N'http://www.linkedin.com/in/colin-epperson'),
 (13634, N'kate@architectsfora.com',      N'Principal',                              N'http://www.linkedin.com/in/kate-conley'),
 (13638, N'mark@swenson.com',             N'President of Development',               N'http://www.linkedin.com/in/mark-pilarczyk-32904956'),
 (13639, N'matt.lindsay@pdbgroup.com',    N'Senior Project Manager',                 N'http://www.linkedin.com/in/matt-lindsay-84206092'),
 (13641, N'nicole.olaes@arup.com',        N'Data Center Design Manager | Associate', N'http://www.linkedin.com/in/nicole-olaes-9a1a0a5a'),
 (13642, N'remami@roemcorp.com',          N'CEO',                                    N'http://www.linkedin.com/in/robert-emami-a40601120'),
 (13645, N'tyuen@level10gc.com',          N'Senior Project Manager',                 N'http://www.linkedin.com/in/tobiasyuen'),
 (13646, N'thomasbliska@dbarchitect.com', N'Architect',                              N'http://www.linkedin.com/in/tom-bliska-77441750'),
 (13648, N'vince@oarcon.com',             N'Co Owner',                               N'http://www.linkedin.com/in/vince-o-driscoll-bb80b847');

-- People: set verified email (COALESCE, never clobber), LinkedIn, email provenance.
UPDATE p
SET Email           = COALESCE(p.Email, w.Email),
    EmailSource     = COALESCE(p.EmailSource, N'asis'),
    EmailConfidence = COALESCE(p.EmailConfidence, 85),
    EmailCheckedAtUtc = sysdatetimeoffset(),
    LinkedinUrl     = COALESCE(p.LinkedinUrl, w.LinkedIn),
    LastSeenAtUtc   = sysdatetimeoffset(),
    UpdatedAtUtc    = sysdatetimeoffset()
FROM opportunities.IntelPerson p
JOIN @work w ON w.PersonId = p.Id;
PRINT CONCAT('IntelPerson enriched: ', @@ROWCOUNT);

-- Affiliations: set the title on the CaEcosystemContactExtract affiliation.
UPDATE a
SET Title = w.Title, LastSeenAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation a
JOIN @work w ON w.PersonId = a.IntelPersonId
WHERE a.SourceProviderName = N'CaEcosystemContactExtract';
PRINT CONCAT('Affiliations titled: ', @@ROWCOUNT);

SELECT p.DisplayName, p.Email, p.EmailSource, a.Title
FROM @work w
JOIN opportunities.IntelPerson p ON p.Id = w.PersonId
LEFT JOIN opportunities.IntelPersonAffiliation a ON a.IntelPersonId = p.Id AND a.SourceProviderName = N'CaEcosystemContactExtract'
ORDER BY p.DisplayName;

COMMIT TRAN;
GO
