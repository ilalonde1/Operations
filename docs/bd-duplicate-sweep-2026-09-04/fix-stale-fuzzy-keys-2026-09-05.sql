SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

-- Stale FuzzyNormalizedName repair, 2026-09-05, after the normalizer change.
--
-- CanonicalOrgResolver.NormalizeForFuzzyMatch now folds every '&' and every spaced '+' to
-- ' and ' (Codex duplicate-sweep fix 1). Every stored key computed before that is stale for
-- a name containing either: 164 rows (31 contain '&', 136 contain ' + ', 3 contain both). A stale key is invisible
-- to the write-time duplicate gate, so until this runs the next reference to any of these
-- firms can mint a twin.
--
-- Values are taken verbatim from ExpectedFuzzy in the integrity report's own
-- org_fuzzy_key_stale CSV (report stamp 20260905-045424), which computes them with the real
-- normalizer. They are NOT re-derived here.
--
-- ORDER: run tools/BdCanonicalDedup --pairs ampersand-fold-merge-2026-09-05.csv --commit FIRST
-- (the five pairs the old key kept apart), then this. Repairing keys first only turns those
-- five into fuzzy-key collisions. Merged losers are deleted rows and fall through the guard.
--
-- Deploy the Worker that carries the new normalizer in the same sitting: between repair and
-- deploy the two normalizers disagree and either side can mint a twin.
--
-- 71528 MCW Group of Companies is DELIBERATELY excluded. It stores 'mcw' as an override; the
-- report flags it as stale and it is not. This is exactly why --backfill-fuzzy-key is not used.

IF OBJECT_ID('tempdb..#fix') IS NOT NULL DROP TABLE #fix;
CREATE TABLE #fix (Id bigint PRIMARY KEY, StoredFuzzy nvarchar(400), ExpectedFuzzy nvarchar(400));
INSERT INTO #fix (Id, StoredFuzzy, ExpectedFuzzy) VALUES
 (31, N'wndrarchitecturedesign', N'wndrarchitectureanddesign'),
 (85, N'daarchitectsplanners', N'daarchitectsandplanners'),
 (112, N'carrierjohnsonculture', N'carrierjohnsonandculture'),
 (940, N'1080architectureplanninginteriors', N'1080architectureplanningandinteriors'),
 (1502, N'arconstruction', N'aandrconstruction'),
 (1503, N'arcontracting', N'aandrcontracting'),
 (3910, N'cbalbertasolardevelopmentulc', N'candbalbertasolardevelopmentulc'),
 (5464, N'couplandkraemerarchitectureinteriordesign', N'couplandkraemerarchitectureandinteriordesign'),
 (7345, N'fastepp', N'fastandepp'),
 (7828, N'fusearchitecturedesign', N'fusearchitectureanddesign'),
 (8799, N'hcmaarchitecturedesign', N'hcmaarchitectureanddesign'),
 (9836, N'jrobertthibodeauarchitecturedesign', N'jrobertthibodeauarchitectureanddesign'),
 (11419, N'maskellplenzikandpartnersengineeringincmpp', N'maskellplenzikandpartnersengineeringincmpandp'),
 (11948, N'modernofficedesignarchitecture', N'modernofficedesignandarchitecture'),
 (12699, N'o2planningdesign', N'o2planninganddesign'),
 (14009, N'rlconstruction', N'randlconstruction'),
 (14910, N'sahuripartnersarchitecture', N'sahuriandpartnersarchitecture'),
 (14932, N'sandalackassociates', N'sandalackandassociates'),
 (15532, N'smithandersen', N'smithandandersen'),
 (17075, N'tokerassociatesarchitecture', N'tokerandassociatesarchitecture'),
 (19223, N'khoraarchitectureinteriors', N'khoraarchitectureandinteriors'),
 (19357, N'anyonearchitecturedesign', N'anyonearchitectureanddesign'),
 (19531, N'tonyosbornarchitecturedesign', N'tonyosbornarchitectureanddesign'),
 (20024, N'kokaarchitecturedesign', N'kokaarchitectureanddesign'),
 (20151, N'jandrkatzdesignarchitecture', N'jandrkatzdesignandarchitecture'),
 (20300, N'studiosenbelarchitecturedesign', N'studiosenbelarchitectureanddesign'),
 (20523, N'lopesrenovationconstruction', N'lopesrenovationandconstruction'),
 (20783, N'rmvancouverconstruction', N'randmvancouverconstruction'),
 (21012, N'seanbestarchitecturedesign', N'seanbestarchitectureanddesign'),
 (21339, N'yldevelopmentscanada', N'yandldevelopmentscanada'),
 (21595, N'baustudioarchitecture', N'baustudioandarchitecture'),
 (21856, N'noblearchitectureinteriors', N'noblearchitectureandinteriors'),
 (22105, N'jrproperties', N'jandrproperties'),
 (22298, N'dnconstruction', N'dandnconstruction'),
 (23375, N'taylorkurtzarchitecturedesign', N'taylorkurtzarchitectureanddesign'),
 (23687, N'oneseedarchitectureinteriors', N'oneseedarchitectureandinteriors'),
 (24291, N'ggelitehomebuilders', N'gandgelitehomebuilders'),
 (24306, N'fritsdevriesarchitectsassociates', N'fritsdevriesarchitectsandassociates'),
 (24547, N'hnpaarchitectureplanning', N'hnpaarchitectureandplanning'),
 (26654, N'simplexgarchitecture', N'simplexandgarchitecture'),
 (26751, N'bgbhattiholdings', N'bandgbhattiholdings'),
 (27469, N'harmonicarchitecturedesign', N'harmonicarchitectureanddesign'),
 (28098, N'maranathaarchitecture', N'maraandnathaarchitecture'),
 (28938, N'dandykwollinarchitects', N'dandykandwollinarchitects'),
 (30803, N'mfconstructionmanagement', N'mandfconstructionmanagement'),
 (33938, N'hesseyconsultingarchitecture', N'hesseyconsultingandarchitecture'),
 (35713, N'euoistudioarchitecturedesign', N'euoistudioarchitectureanddesign'),
 (38969, N'officeofmcfarlanebiggararchitectsdesignersomb', N'officeofmcfarlanebiggararchitectsanddesignersomb'),
 (38972, N'publicarchitecturedesign', N'publicarchitectureanddesign'),
 (40036, N'stewarttsaiarchitects', N'stewartandtsaiarchitects'),
 (46592, N'hhconstruction', N'handhconstruction'),
 (46646, N'ajhannaconstruction', N'aandjhannaconstruction'),
 (46709, N'csbuilders', N'candsbuilders'),
 (46814, N'jokinenojdrovicengineersinjointventure', N'jokinenandojdrovicengineersinjointventure'),
 (47201, N'dlengineeringsales', N'dandlengineeringsales'),
 (47863, N'aocontracting', N'aandocontracting'),
 (51053, N'lalandedoylearchitects', N'lalandeanddoylearchitects'),
 (52018, N'costantinoassociatesarchitect', N'costantinoandassociatesarchitect'),
 (54375, N'dambrosioarchitectureurbanism', N'dambrosioarchitectureandurbanism'),
 (54731, N'dawsonsawyer', N'dawsonandsawyer'),
 (54975, N'vancouvercommunitycollegelangaracollegecombined', N'vancouvercommunitycollegeandlangaracollegecombined'),
 (55016, N'musqueamtsleilwaututhaquilinidevelopment', N'musqueamandtsleilwaututhandaquilinidevelopment'),
 (55018, N'nchḵay̓westnchḵay̓developmentcorpoptrust', N'nchḵay̓westnchḵay̓developmentcorpandoptrust'),
 (55020, N'nchḵay̓developmentcorporationhiy̓ám̓housing', N'nchḵay̓developmentcorporationandhiy̓ám̓housing'),
 (55021, N'musqueamcapitalcorporationpolygonhomes', N'musqueamcapitalcorporationandpolygonhomes'),
 (55022, N'takayadevelopmentsaquilinigroup', N'takayadevelopmentsandaquilinigroup'),
 (55034, N'songheesdevelopmentcorporationbchousing', N'songheesdevelopmentcorporationandbchousing'),
 (55039, N'haislanationpembinapipeline', N'haislanationandpembinapipeline'),
 (55042, N'squialafirstnationpropertydevelopmentgroupleagueassets', N'squialafirstnationandpropertydevelopmentgroupandleagueassets'),
 (55105, N'fastsolterradevelopment', N'fastandsolterradevelopment'),
 (58416, N'kobayashizeddaarchitects', N'kobayashiandzeddaarchitects'),
 (63332, N'norrlimitedarchitectsengineers', N'norrlimitedarchitectsandengineers'),
 (66380, N'buildingsciencearchitecture', N'buildingscienceandarchitecture'),
 (68756, N'aciarchitectureincstephenskozakaci', N'aciarchitectureincandstephenskozakaci'),
 (68818, N'saucierperrottearchitectes', N'saucierandperrottearchitectes'),
 (68883, N'ehddpayette', N'ehddandpayette'),
 (68889, N'sommithun', N'somandmithun'),
 (68897, N'tkadarchitecturedesign', N'tkadarchitectureanddesign'),
 (68898, N'tkadrdha', N'tkadandrdha'),
 (68917, N'localpracticearchitecturedesign', N'localpracticearchitectureanddesign'),
 (68976, N'berryarchitectureassociates', N'berryarchitectureandassociates'),
 (69095, N'formlinearchitectureurbanism', N'formlinearchitectureandurbanism'),
 (69122, N'prosceniumarchitectureinteriors', N'prosceniumarchitectureandinteriors'),
 (69243, N'bjarkeingelsgroupbigdialog', N'bjarkeingelsgroupbiganddialog'),
 (69249, N'busbyassociatesarchitects', N'busbyandassociatesarchitects'),
 (69348, N'studio9architectureplanning', N'studio9architectureandplanning'),
 (69540, N'vanderzalmassociates', N'vanderzalmandassociates'),
 (69688, N'perkinswill', N'perkinsandwill'),
 (69758, N'leckiestudioarchitecturedesign', N'leckiestudioarchitectureanddesign'),
 (69775, N'humanstudioarchitectureurbandesign', N'humanstudioarchitectureandurbandesign'),
 (70109, N'normanfosterfosterpartners', N'normanfosterfosterandpartners'),
 (70110, N'hedmoorerubleyudell', N'hedandmoorerubleyudell'),
 (70623, N'sebastiengaronarchitecturedesign', N'sebastiengaronarchitectureanddesign'),
 (70824, N'lakemonsterstudioarchitecturedesign', N'lakemonsterstudioarchitectureanddesign'),
 (71201, N'khdevelopments', N'kandhdevelopments'),
 (71621, N'aberecanada', N'abeandrecanada'),
 (71809, N'inengineeringplanninginengineeringplanning', N'inengineeringandplanninginengineeringandplanning'),
 (72012, N'aearchitecturalandengineeringgroup', N'aandearchitecturalandengineeringgroup'),
 (72104, N'civicworksplanningdesign', N'civicworksplanninganddesign'),
 (72368, N'smithandersencalgary', N'smithandandersencalgary'),
 (74000, N'thujaarchitecturedesign', N'thujaarchitectureanddesign'),
 (74052, N'emilycarruniversityofartdesign', N'emilycarruniversityofartanddesign'),
 (75628, N'semiahmualelenhousingsocietybchousing', N'semiahmualelenhousingsocietyandbchousing'),
 (75633, N'zahahadidarchitectsarchistar', N'zahahadidarchitectsandarchistar'),
 (75634, N'matulliaesquimaltsonghees', N'matulliaesquimaltandsonghees'),
 (75635, N'urbanstrategiesmasterplanmcelhanneyvamosadvisors', N'urbanstrategiesmasterplanandmcelhanneyandvamosadvisors'),
 (75721, N'househousearchitectshouseandhousearchitects', N'houseandhousearchitectshouseandhousearchitects'),
 (75899, N'mcfarlanebiggararchitectsdesigners', N'mcfarlanebiggararchitectsanddesigners'),
 (76788, N'wisemanrohystructuralengineers', N'wisemanandrohystructuralengineers'),
 (76832, N'dreyfussblackfordarchitecture', N'dreyfussandblackfordarchitecture'),
 (76834, N'lpasarchitecturedesign', N'lpasarchitectureanddesign'),
 (76840, N'19sixarchitectsformerlywilliamspaddon', N'19sixarchitectsformerlywilliamsandpaddon'),
 (76851, N'dsdevelopment', N'dandsdevelopment'),
 (76871, N'wexfordsciencetechnology', N'wexfordscienceandtechnology'),
 (76878, N'rutherfordchekene', N'rutherfordandchekene'),
 (76952, N'mvepartners', N'mveandpartners'),
 (76958, N'martinezcutri', N'martinezandcutri'),
 (76977, N'hsventures', N'handsventures'),
 (77097, N'labibfunkassociates', N'labibfunkandassociates'),
 (77124, N'labibfunkassociateslfa', N'labibfunkandassociateslfa'),
 (77209, N'ktgyarchitectureplanning', N'ktgyarchitectureandplanning'),
 (77210, N'mkmarchitecturedesign', N'mkmarchitectureanddesign'),
 (77293, N'vlmkengineeringdesign', N'vlmkengineeringanddesign'),
 (77662, N'tsstructuralengineers', N'tandsstructuralengineers'),
 (77820, N'lakecountryhighway97hotelresidentialunnameddeveloper', N'lakecountryhighway97hotelandresidentialunnameddeveloper'),
 (77824, N'placemarkdesigndevelopment', N'placemarkdesignanddevelopment'),
 (77899, N'mcfarlanebiggararchitectsdesignersomb', N'mcfarlanebiggararchitectsanddesignersomb'),
 (271538, N'simcicuhricharchitects', N'simcicanduhricharchitects'),
 (271539, N'officeofmcfarlanebiggararchitectsdesigners', N'officeofmcfarlanebiggararchitectsanddesigners'),
 (271547, N'axisgfaarchitecturedesign', N'axisgfaarchitectureanddesign'),
 (271550, N'johnstondavidsonarchitectureculture', N'johnstondavidsonarchitectureandculture'),
 (271559, N'zgfarchitectsparkinarchitects', N'zgfarchitectsandparkinarchitects'),
 (271569, N'actonostryarchitectsmjmaarchitectureanddesign', N'actonostryarchitectsandmjmaarchitectureanddesign'),
 (271572, N'marmolradzinerlargearchitecture', N'marmolradzinerandlargearchitecture'),
 (271576, N'hksgenslerkfarelm', N'hksandgenslerandkfaandrelm'),
 (271579, N'tcac2collaborative', N'tcaandc2collaborative'),
 (271580, N'genslerrios', N'genslerandrios'),
 (271583, N'jameskmchengsteinberghart', N'jameskmchengandsteinberghart'),
 (271585, N'westofwesthouseandrobertson', N'westofwestandhouseandrobertson'),
 (271587, N'desarchitectsengineers', N'desarchitectsandengineers'),
 (271590, N'herdmanarchitecturedesign', N'herdmanarchitectureanddesign'),
 (271600, N'formadevelopmentdesignamp', N'formadevelopmentdesignandamp'),
 (271601, N'casscaldersmitharchitectureinteriors', N'casscaldersmitharchitectureandinteriors'),
 (271615, N'solomoncordwellbuenziwamotoscottarchitecture', N'solomoncordwellbuenzandiwamotoscottarchitecture'),
 (271639, N'scbhenninglarsen', N'scbandhenninglarsen'),
 (271640, N'grimshawperryarchitects', N'grimshawandperryarchitects'),
 (271641, N'adjayeassociatesstudiooneeleven', N'adjayeassociatesandstudiooneeleven'),
 (271645, N'karinpaysonarchitecturedesign', N'karinpaysonarchitectureanddesign'),
 (271652, N'wspcanadawithfastepp', N'wspcanadawithfastandepp'),
 (271683, N'damp', N'dandamp'),
 (271722, N'lendleaseawaresuper', N'lendleaseandawaresuper'),
 (271725, N'hinescjsegerstrom', N'hinesandcjsegerstrom'),
 (271731, N'presidiocapitalfulleraptbolthouse', N'presidiocapitalandfulleraptandbolthouse'),
 (271734, N'hinessullivanfamily', N'hinesandsullivanfamily'),
 (271740, N'zephyrchelseainvestment', N'zephyrandchelseainvestment'),
 (271741, N'skkdevelopmentsblackpine', N'skkdevelopmentsandblackpine'),
 (271742, N'bridgehousingavalonbay', N'bridgehousingandavalonbay'),
 (271743, N'skkdevelopmentsurbancore', N'skkdevelopmentsandurbancore'),
 (271745, N'laconiadevelopmentgilbane', N'laconiadevelopmentandgilbane'),
 (272355, N'sahuriassociatesarchitecture', N'sahuriandassociatesarchitecture'),
 (272365, N'johnmcaslanpartners', N'johnmcaslanandpartners'),
 (272398, N'spstructuralengineers', N'sandpstructuralengineers'),
 (927762, N'barefootplanningdesign', N'barefootplanninganddesign'),
 (927795, N'leesassociates', N'leesandassociates');

SELECT 'rows found live with the stored key still in place' AS Section, COUNT(*) AS N
FROM opportunities.CanonicalOrg co JOIN #fix f ON f.Id = co.Id
WHERE co.RetiredAtUtc IS NULL AND ISNULL(co.FuzzyNormalizedName, N'') = f.StoredFuzzy;

BEGIN TRANSACTION;
UPDATE co
SET co.FuzzyNormalizedName = f.ExpectedFuzzy,
    co.UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg co
JOIN #fix f ON f.Id = co.Id
WHERE co.RetiredAtUtc IS NULL
  AND ISNULL(co.FuzzyNormalizedName, N'') = f.StoredFuzzy   -- guard: only the value the report saw
  AND co.FuzzyNormalizedName <> f.ExpectedFuzzy;
SELECT 'rows updated' AS Section, @@ROWCOUNT AS N;
COMMIT TRANSACTION;

SELECT 'any of these keys now collide with another LIVE row?' AS Section;
SELECT co.FuzzyNormalizedName, COUNT(*) AS LiveRows,
       LEFT(STRING_AGG(CAST(co.DisplayName AS nvarchar(max)), ' | '), 90) AS Names
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND co.FuzzyNormalizedName IN (SELECT ExpectedFuzzy FROM #fix)
GROUP BY co.FuzzyNormalizedName
HAVING COUNT(*) > 1;
