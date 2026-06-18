/* 191_IngestCaEcosystemContacts.sql — 2026-06-18 CA ecosystem decision-makers -> IntelPerson.
   Generated from people-enriched.csv (Apollo+Hunter verified) with a hand-verified company->org map.
   162/185/188 SHA1-NaturalKey MERGE (COALESCE-safe; app auto-ingest matches, never dups). */
SET XACT_ABORT ON; BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset(); DECLARE @provider nvarchar(100)=N'ca-ecosystem-2026-06';
DECLARE @c TABLE (OrgId bigint,Person nvarchar(200),Title nvarchar(200),Email nvarchar(200),Conf nvarchar(20),Notes nvarchar(max),
  NormName nvarchar(200),NormTitle nvarchar(200),NormDisplay nvarchar(200),PKey char(40),AffKey char(40),PersonId bigint,EnrId bigint);
INSERT INTO @c (OrgId,Person,Title,Email,Conf,Notes) VALUES
 (48,N'Joseph O. Wong',N'President',N'jwong@jwdainc.com',N'High',N'President at JWDA (Joseph Wong Design Associates) (SD). [ca-ecosystem-2026-06]'),
 (48,N'Vivian Tria',NULL,NULL,N'Medium',N' at JWDA (Joseph Wong Design Associates) (SD). [ca-ecosystem-2026-06]'),
 (112,N'David Huchteman',N'CEO',N'deh@carrierjohnson.com',N'High',N'CEO at Carrier Johnson + Culture (SD). [ca-ecosystem-2026-06]'),
 (112,N'Gordon Carrier',NULL,N'grc@carrierjohnson.com',N'High',N' at Carrier Johnson + Culture (SD). [ca-ecosystem-2026-06]'),
 (112,N'Marin Gertler',N'CDO',N'mlg@carrierjohnson.com',N'High',N'CDO at Carrier Johnson + Culture (SD). [ca-ecosystem-2026-06]'),
 (112,N'Jackie Angel',N'Chief Operating Officer',N'jam@carrierjohnson.com',N'High',N'Chief Operating Officer at Carrier Johnson + Culture (SD). [ca-ecosystem-2026-06]'),
 (75176,N'Pablo Collin',N'Senior Project Manager',N'pcollin@avrpstudios.com',N'High',N'Senior Project Manager at AVRP Studios (SD). [ca-ecosystem-2026-06]'),
 (75176,N'Christopher Veum',N'President',N'cveum@avrpstudios.com',N'High',N'President at AVRP Studios (SD). [ca-ecosystem-2026-06]'),
 (75176,N'Douglas Austin',N'Chairman, CEO',NULL,N'Medium',N'Chairman, CEO at AVRP Studios (SD). [ca-ecosystem-2026-06]'),
 (68631,N'Kevin Heinly',N'Principal, Managing Director Gensler San Diego',N'kevin_heinly@gensler.com',N'High',N'Principal, Managing Director Gensler San Diego at Gensler San Diego (SD). [ca-ecosystem-2026-06]'),
 (68631,N'Darrel Fullbright',N'Principal | Design Director',N'darrel_fullbright@gensler.com',N'High',N'Principal | Design Director at Gensler San Diego (SD). [ca-ecosystem-2026-06]'),
 (68631,N'Richard King',NULL,N'richard_king@gensler.com',N'High',N' at Gensler San Diego (SD). [ca-ecosystem-2026-06]'),
 (68631,N'Tom Heffernan',N'Principal',N'tom_heffernan@gensler.com',N'High',N'Principal at Gensler San Diego (SD). [ca-ecosystem-2026-06]'),
 (20,N'Pauly De Bartolo',NULL,N'pauly@dbrds.com',N'High',N' at DBRDS (De Bartolo + Rimanic Design Studio) (SD). [ca-ecosystem-2026-06]'),
 (20,N'Ivan Rimanic',N'Designer',N'ivan@dbrds.com',N'High',N'Designer at DBRDS (De Bartolo + Rimanic Design Studio) (SD). [ca-ecosystem-2026-06]'),
 (20,N'Haneen Khater',N'Principal Architect',N'haneen@dbrds.com',N'High',N'Principal Architect at DBRDS (De Bartolo + Rimanic Design Studio) (SD). [ca-ecosystem-2026-06]'),
 (20,N'Craig Howard',NULL,N'craig@dbrds.com',N'High',N' at DBRDS (De Bartolo + Rimanic Design Studio) (Sacramento). [ca-ecosystem-2026-06]'),
 (68910,N'Ricardo Rabines',N'Co-Founder',N'ricardo@safdierabines.com',N'High',N'Co-Founder at Safdie Rabines Architects (SD). [ca-ecosystem-2026-06]'),
 (68910,N'Taal Safdie',N'Co-Founder',N'taal@safdierabines.com',N'High',N'Co-Founder at Safdie Rabines Architects (SD). [ca-ecosystem-2026-06]'),
 (76970,N'Eric Naslund',N'Principal',N'enaslund@studioearchitects.com',N'High',N'Principal at Studio E Architects (SD). [ca-ecosystem-2026-06]'),
 (76970,N'John Sheehan',N'Partner',N'jsheehan@studioearchitects.com',N'High',N'Partner at Studio E Architects (SD). [ca-ecosystem-2026-06]'),
 (76952,N'Chase Rongé',NULL,N'cronge@mve-architects.com',N'High',N' at MVE + Partners (SD). [ca-ecosystem-2026-06]'),
 (75177,N'Dan Martorana',N'Senior Associate / Studio Manager',N'dmartorana@tuckersadler.com',N'High',N'Senior Associate / Studio Manager at Tucker Sadler Architects (SD). [ca-ecosystem-2026-06]'),
 (75177,N'Gregory Mueller',N'Principal Designer and CEO',N'gmueller@tuckersadler.com',N'High',N'Principal Designer and CEO at Tucker Sadler Architects (SD). [ca-ecosystem-2026-06]'),
 (76958,N'Joseph Martinez',NULL,N'jmartinez@martinezcutri.com',N'High',N' at Martinez + Cutri Urban Studio (SD). [ca-ecosystem-2026-06]'),
 (76958,N'Anthony Cutri',NULL,NULL,N'Medium',N' at Martinez + Cutri Urban Studio (SD). [ca-ecosystem-2026-06]'),
 (70737,N'Adam Covington',N'Senior Director',N'adam.covington@greystar.com',N'High',N'Senior Director at Greystar (SD). [ca-ecosystem-2026-06]'),
 (70737,N'Alex Leonard',N'Real Estate Development Leader',N'alex.leonard@greystar.com',N'High',N'Real Estate Development Leader at Greystar (SD). [ca-ecosystem-2026-06]'),
 (68778,N'Eric Hepfer',N'Managing Director',N'eric.hepfer@hines.com',N'High',N'Managing Director at Hines San Diego (SD). [ca-ecosystem-2026-06]'),
 (68778,N'Pete Shearer',N'Senior Director',N'pshearer@myhines.com',N'High',N'Senior Director at Hines San Diego (SD). [ca-ecosystem-2026-06]'),
 (76953,N'Ash Israni',N'Cloud Consultant',N'ash.israni@slalom.com',N'High',N'Cloud Consultant at Pacifica Companies (SD). [ca-ecosystem-2026-06]'),
 (76953,N'Sushil Israni',N'Managing Partner',N'sisrani@pacificacompanies.com',N'High',N'Managing Partner at Pacifica Companies (SD). [ca-ecosystem-2026-06]'),
 (76953,N'Naresh Kotwani',N'Principal',N'nkotwani@pacificacompanies.com',N'High',N'Principal at Pacifica Companies (SD). [ca-ecosystem-2026-06]'),
 (69677,N'Ryan Bosa',NULL,N'giveback@bosadevelopment.com',N'High',N' at Bosa Development (SD). [ca-ecosystem-2026-06]'),
 (76,N'Scott Murfey',N'Co-CEO',N'scott@murfeycompany.com',N'High',N'Co-CEO at Murfey Company (SD). [ca-ecosystem-2026-06]'),
 (76,N'Russ Murfey',NULL,N'russ@murfeycompany.com',N'High',N' at Murfey Company (SD). [ca-ecosystem-2026-06]'),
 (76972,N'Charles Schmid',N'Chief Executive Officer',N'charlesschmid@chelseainvestco.com',N'High',N'Chief Executive Officer at Chelsea Investment Corporation (SD). [ca-ecosystem-2026-06]'),
 (76972,N'Jim Andersen',N'Senior Vice President - Development',N'jimandersen@lyonliving.com',N'High',N'Senior Vice President - Development at Chelsea Investment Corporation (SD). [ca-ecosystem-2026-06]'),
 (76972,N'Cheri Hoffman',NULL,N'cherihoffman@chelseainvestco.com',N'High',N' at Chelsea Investment Corporation (SD). [ca-ecosystem-2026-06]'),
 (76971,N'Brad Termini',N'Co CEO',N'brad@zephyrpartners.com',N'High',N'Co CEO at Zephyr Partners (SD). [ca-ecosystem-2026-06]'),
 (76971,N'Ryan Herrell',N'Chief Operating Officer',N'rherrell@zephyrpartners.com',N'High',N'Chief Operating Officer at Zephyr Partners (SD). [ca-ecosystem-2026-06]'),
 (76971,N'Austin Richter',N'Vice President',N'arichter@zephyrpartners.com',N'High',N'Vice President at Zephyr Partners (SD). [ca-ecosystem-2026-06]'),
 (76973,N'Rebecca Louie',N'President and Chief Executive Officer',N'rlouie@wakelandhdc.com',N'High',N'President and Chief Executive Officer at Wakeland Housing (SD). [ca-ecosystem-2026-06]'),
 (73896,N'Andrew Gagliano',N'Senior Public Relations Manager',N'andrew.gagliano@dap.com',N'High',N'Senior Public Relations Manager at Toll Brothers / Kennedy Wilson (SD). [ca-ecosystem-2026-06]'),
 (75736,N'Christopher Tipre',NULL,N'ctipre@trammellcrow.com',N'High',N' at Trammell Crow Residential (SD). [ca-ecosystem-2026-06]'),
 (76974,N'Jimmy Silverwood',N'President',N'james@affirmedhousing.com',N'High',N'President at Affirmed Housing (SD). [ca-ecosystem-2026-06]'),
 (76805,N'Jason R. Wood',NULL,NULL,N'Medium',N' at Cisterra Development (SD). [ca-ecosystem-2026-06]'),
 (53665,N'Michael De Cotiis',NULL,N'akwok@pinnacleinternational.ca',N'High',N' at Pinnacle International (SD). [ca-ecosystem-2026-06]'),
 (76952,N'Pieter Berger',N'Associate Partner',NULL,N'Medium',N'Associate Partner at MVE + Partners (OC). [ca-ecosystem-2026-06]'),
 (76952,N'Daniel Gura',N'National Director of Business Development, Shareholder, and Board of Director',N'dgura@mve-architects.com',N'High',N'National Director of Business Development, Shareholder, and Board of Director at MVE + Partners (OC). [ca-ecosystem-2026-06]'),
 (76952,N'Matthew McLarand',N'President',N'mmclarand@mve-architects.com',N'High',N'President at MVE + Partners (Bay). [ca-ecosystem-2026-06]'),
 (76975,N'Rob Budetti',N'Managing Partner',N'rob@aoarchitects.com',N'High',N'Managing Partner at AO (Architects Orange) (OC). [ca-ecosystem-2026-06]'),
 (68645,N'Gino Canori',NULL,N'gcanori@related.com',N'High',N' at Related California (OC;LA;Bay). [ca-ecosystem-2026-06]'),
 (68645,N'Steven Oh',NULL,N'steven.oh@related.com',N'High',N' at Related California (OC). [ca-ecosystem-2026-06]'),
 (76967,N'Bill Shopoff',N'President & CEO',N'bshopoff@shopoff.com',N'High',N'President & CEO at Shopoff Realty Investments (OC). [ca-ecosystem-2026-06]'),
 (76967,N'Brian Rupp',N'Vice President',N'brupp@tkcteam.com',N'High',N'Vice President at Shopoff Realty Investments (OC). [ca-ecosystem-2026-06]'),
 (76956,N'Irwin Yau',N'President',N'iyau@tca-arch.com',N'High',N'President at TCA Architects (Irvine) (OC). [ca-ecosystem-2026-06]'),
 (76956,N'Daniel Lee',NULL,NULL,N'Medium',N' at TCA Architects (Irvine) (OC). [ca-ecosystem-2026-06]'),
 (74271,N'Rob Elliott',N'SVP Planning and Design',N'robelliott@irvinecompany.com',N'High',N'SVP Planning and Design at The Irvine Company (OC). [ca-ecosystem-2026-06]'),
 (74271,N'Jeff Davis',NULL,NULL,N'Medium',N' at The Irvine Company (OC). [ca-ecosystem-2026-06]'),
 (70737,N'Raul Tamez',N'Senior Director',N'raul.tamez@greystar.com',N'High',N'Senior Director at Greystar (OC). [ca-ecosystem-2026-06]'),
 (76977,N'Henry Samueli',NULL,NULL,N'Medium',N' at H&S Ventures (OC). [ca-ecosystem-2026-06]'),
 (75707,N'John Paul Youssef',N'CEO',N'jpyoussef@nyase.com',N'High',N'CEO at Nabih Youssef & Associates (NYA) (LA). [ca-ecosystem-2026-06]'),
 (74231,N'Garrett Lee',N'President',NULL,N'Medium',N'President at Jamison Properties (LA). [ca-ecosystem-2026-06]'),
 (74231,N'Phillip Lee',N'President',N'philliplee@jamisonservices.com',N'High',N'President at Jamison Services (LA). [ca-ecosystem-2026-06]'),
 (4906,N'Sean Burton',N'Chief Executive Officer',N'sburton@cityview.com',N'High',N'Chief Executive Officer at Cityview (LA). [ca-ecosystem-2026-06]'),
 (4906,N'Anh Le',NULL,N'ale@cityview.com',N'High',N' at Cityview (LA). [ca-ecosystem-2026-06]'),
 (68641,N'Will Cipes',N'Senior Vice President, Development',N'wcipes@carmelpartners.com',N'High',N'Senior Vice President, Development at Carmel Partners (LA). [ca-ecosystem-2026-06]'),
 (74239,N'Stuart Morkun',N'Executive Vice President - Development',N'stuartmorkun@3industrial.com',N'High',N'Executive Vice President - Development at Mitsui Fudosan America (LA). [ca-ecosystem-2026-06]'),
 (220,N'Nabih A. Faris',N'President&CEO',N'nfaris@intergulf.com',N'High',N'President&CEO at Intergulf Development Group (LA). [ca-ecosystem-2026-06]'),
 (220,N'Shaadi Faris',N'Chief Operating Officer',N'sfaris@intergulf.com',N'High',N'Chief Operating Officer at Intergulf Development Group (LA). [ca-ecosystem-2026-06]'),
 (220,N'Brian Buchanan',NULL,N'bbuchanan@intergulf.com',N'High',N' at Intergulf Development Group (LA). [ca-ecosystem-2026-06]'),
 (68644,N'Tom Warren',N'President',N'twarren@hollandpartnergroup.com',N'High',N'President at Holland Partner Group (LA). [ca-ecosystem-2026-06]'),
 (68644,N'Clyde Holland',N'Chairman',N'clyde@hollandpartnergroup.com',N'High',N'Chairman at Holland Partner Group (LA). [ca-ecosystem-2026-06]'),
 (76890,N'Keith McCloskey',NULL,N'kmccloskey@ktgy.com',N'High',N' at KTGY Architecture + Planning (LA). [ca-ecosystem-2026-06]'),
 (68642,N'Bruce Menin',N'Principal',N'bam@crescentheights.com',N'High',N'Principal at Crescent Heights (LA). [ca-ecosystem-2026-06]'),
 (68642,N'Russell Galbut',N'Principal',N'rgalbut@crescentheights.com',N'High',N'Principal at Crescent Heights (LA). [ca-ecosystem-2026-06]'),
 (75736,N'Christina Lee',NULL,N'clee4@trammellcrow.com',N'High',N' at Trammell Crow / High Street Residential (LA). [ca-ecosystem-2026-06]'),
 (75736,N'Brett Montgomery',NULL,N'bmontgomery@trammellcrow.com',N'High',N' at Trammell Crow / High Street Residential (LA). [ca-ecosystem-2026-06]'),
 (70737,N'Andrew Kuo',N'Managing Director of Development',N'andrew.kuo@greystar.com',N'High',N'Managing Director of Development at Greystar (LA). [ca-ecosystem-2026-06]'),
 (76956,N'Eric Olsen',N'Vice President & Principal in Charge',N'eric@tca-arch.com',N'High',N'Vice President & Principal in Charge at TCA Architects (LA). [ca-ecosystem-2026-06]'),
 (68634,N'Scott Johnson',NULL,N'sjohnson@johnsonfain.com',N'High',N' at Johnson Fain (LA). [ca-ecosystem-2026-06]'),
 (68634,N'William Fain',N'Co-President, Managing Partner, Director of Urban Design & Planning',N'wfain@johnsonfain.com',N'High',N'Co-President, Managing Partner, Director of Urban Design & Planning at Johnson Fain (LA). [ca-ecosystem-2026-06]'),
 (68644,N'John Wayland',N'Executive Managing Director',N'jwayland@hollandpartnergroup.com',N'High',N'Executive Managing Director at Holland Partner Group (Bay). [ca-ecosystem-2026-06]'),
 (75180,N'Francesco Mozzati',NULL,N'francesco.mozzati@scb.com',N'High',N' at SCB San Francisco (Bay). [ca-ecosystem-2026-06]'),
 (76956,N'Thomas Cox',N'Founder Emeritus TCA Architects',N'tcox@tca-arch.com',N'High',N'Founder Emeritus TCA Architects at TCA Architects (Bay). [ca-ecosystem-2026-06]'),
 (76956,N'Teresa Ruiz',N'Principal',N'truiz@tca-arch.com',N'High',N'Principal at TCA Architects (Bay). [ca-ecosystem-2026-06]'),
 (70737,N'Randy Ackerman',N'Managing Director, Development',N'rackerman@greystar.com',N'High',N'Managing Director, Development at Greystar (Bay;Sacramento). [ca-ecosystem-2026-06]'),
 (76901,N'Jay Paul',N'Associate',NULL,N'Medium',N'Associate at Jay Paul Company (Bay). [ca-ecosystem-2026-06]'),
 (73742,N'Mike Kim',N'Senior Managing Director',N'mkim@mcrtrust.com',N'High',N'Senior Managing Director at Mill Creek Residential (Bay). [ca-ecosystem-2026-06]'),
 (75180,N'Matt Bens',NULL,N'matt.bens@scb.com',N'High',N' at SCB San Francisco (Bay). [ca-ecosystem-2026-06]'),
 (76835,N'Renner Johnston',N'Principal',N'rjohnston@mogaveroarchitects.com',N'High',N'Principal at Mogavero Architects (Sacramento). [ca-ecosystem-2026-06]'),
 (68694,N'Peter Horn',N'Senior Vice President',N'phorn@lpc.com',N'High',N'Senior Vice President at Lincoln Property Company (Bay). [ca-ecosystem-2026-06]'),
 (68645,N'Nick Witte',NULL,N'nwitte@related.com',N'High',N' at Related California (Bay). [ca-ecosystem-2026-06]'),
 (68645,N'Ann Silverberg',N'President and CEO Related California and Northwest Affordable',N'asilverberg@related.com',N'High',N'President and CEO Related California and Northwest Affordable at Related California (Bay;LA). [ca-ecosystem-2026-06]'),
 (68645,N'Matt Witte',N'Partner',N'matthew.witte@related.com',N'High',N'Partner at Related California (OC;Bay). [ca-ecosystem-2026-06]'),
 (74231,N'Don Hankey',N'Owner',NULL,N'Medium',N'Owner at Hankey Investment Company (LA). [ca-ecosystem-2026-06]'),
 (68643,N'Geoff Palmer',NULL,N'gpalmer@ghpalmer.com',N'High',N' at GH Palmer Associates (LA). [ca-ecosystem-2026-06]'),
 (76967,N'William Shopoff',N'President & CEO',N'bshopoff@shopoff.com',N'High',N'President & CEO at Shopoff Realty (LA). [ca-ecosystem-2026-06]'),
 (76976,N'David Forbes Hibbert',NULL,N'hibbert@dfhaia.com',N'High',N' at DFH Architects (LA). [ca-ecosystem-2026-06]'),
 (76978,N'Greg Lyon',NULL,N'glyon@nadelarc.com',N'High',N' at Nadel Architecture + Planning (LA). [ca-ecosystem-2026-06]'),
 (76978,N'Martin Leitner',NULL,NULL,N'Medium',N' at Nadel Architecture + Planning (LA). [ca-ecosystem-2026-06]');
UPDATE @c SET NormName=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(Person))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N''),
  NormTitle=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(COALESCE(Title,N'')))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N''),
  NormDisplay=LOWER(LTRIM(RTRIM(Person)));
UPDATE @c SET PKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(NormName AS VARCHAR(8000))),2);
MERGE opportunities.CanonicalOrgEnrichment WITH (HOLDLOCK) AS T USING (SELECT DISTINCT OrgId FROM @c) AS S
  ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId,ProviderName,Status,Attempts,LastRefreshAtUtc,CreatedAtUtc,UpdatedAtUtc) VALUES (S.OrgId,@provider,N'ok',1,@now,@now,@now);
UPDATE c SET EnrId=e.Id FROM @c c JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=c.OrgId AND e.ProviderName=@provider;
MERGE opportunities.IntelPerson WITH (HOLDLOCK) AS T
USING (SELECT PKey,MIN(Person) Person,MIN(NormDisplay) NormDisplay,MIN(Email) Email,MIN(Conf) Conf,MIN(Notes) Notes,MIN(EnrId) EnrId FROM @c GROUP BY PKey) AS S
  ON T.NaturalKey=S.PKey
WHEN MATCHED THEN UPDATE SET Email=COALESCE(T.Email,S.Email),LastSeenAtUtc=@now,UpdatedAtUtc=@now,Corroborations=T.Corroborations+1
WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,DisplayName,NormalizedName,Email,Notes,Corroborations)
  VALUES (@provider,S.EnrId,S.Conf,S.PKey,@now,@now,S.Person,S.NormDisplay,S.Email,S.Notes,1);
UPDATE c SET PersonId=p.Id FROM @c c JOIN opportunities.IntelPerson p ON p.NaturalKey=c.PKey;
UPDATE @c SET AffKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CAST(PersonId AS VARCHAR(20))+'|'+CAST(OrgId AS VARCHAR(20))+'|'+NormTitle AS VARCHAR(8000))),2);
MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T USING (SELECT AffKey,PersonId,OrgId,Title,Conf,EnrId FROM @c) AS S
  ON T.NaturalKey=S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=@now,UpdatedAtUtc=@now
WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,IntelPersonId,CanonicalOrgId,Title,IsCurrent)
  VALUES (@provider,S.EnrId,S.Conf,S.AffKey,@now,@now,S.PersonId,S.OrgId,S.Title,1);
COMMIT;
SELECT COUNT(*) AS NewPeople, SUM(CASE WHEN Email IS NOT NULL THEN 1 ELSE 0 END) AS WithEmail FROM opportunities.IntelPerson WHERE SourceProviderName=N'ca-ecosystem-2026-06';


