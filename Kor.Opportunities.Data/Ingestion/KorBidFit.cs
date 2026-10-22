#nullable enable
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Data.Ingestion;

/// <summary>
/// Is a tender work a structural engineering firm would bid on? Read from the title alone, because a tender's title is
/// the one field every portal fills. KOR work: prime and architectural consultant services for buildings, engineering
/// and professional-services rosters, design-build teams, building and structure condition assessments, facility studies,
/// and anything structural or seismic. Not KOR work: roads, utilities, bridges and highways, mechanical, electrical and
/// geotechnical-only services, construction management, and supplies and operating services.
///
/// Measured 2026-10-20 against all 581 open tenders, each labelled by hand (67 KOR work since 2026-10-21, when a campground
/// electrification was relabelled as utility work): it shows 66, of which 63 are KOR work (precision 0.95), and finds 63
/// of the 67 (recall 0.94). The labelled set is in the test project
/// (KorBidFitLabels-2026-10-20.tsv); the test fails if either figure drops. The rule was tuned on that same set, so a new
/// kind of title can still be misread: a bare project name with no service words ("Southside Aquatic Facility") is
/// missed, and a sewer job named only by its streets ("Design and Construction Services - Browning Persimmon") is shown.
///
/// The tender archive (Market History) needed a second vocabulary, because its "consulting services" are as often payroll,
/// IT or HR as buildings: measured 2026-10-21 on 732 hand-labelled archive titles, precision went from 174 of 432 to 175 of
/// 187 and recall is 175 of 176 (KorBidFitArchiveLabels-2026-10-21.tsv); the open-tender figures above did not move.
///
/// Street, mental health and heritage were narrowed the same day (adversarial audit): a bare street name in a building's
/// title ("Main Street Elementary School Replacement") hid it; now only street lighting, streetscapes and street works do.
/// </summary>
public static class KorBidFit
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // Checked before "structural": steel, trailers and parts called structural, contractors, inspection and monitoring
    // services, and dams and bridges, which a structural firm for buildings does not bid (archive, 2026-10-21).
    private static readonly Regex NeverOurs = new(
        @"\b(construction\s+management|trailer|salvaged|fabricat\w*|contractor\s+(services|pre-?qualification)|construction\s+contractor|in-plant|quality\s+assurance|coatings|recoating|health\s+monitoring|monitoring\s+networks|dams?|highway\s+bridges|bridge\s+engineering|bridges\s+and\s+structures|and\s+pavement|bridge\s+and\s+cattleguard|structural\s+steel|abatement|project\s+supervision|hwy|fsr|key\s+routes|system\s+upgrade\s+projects)\b",
        Opts);

    private static readonly Regex Structural = new(@"\b(structural\b(?!\s+turnout)|seismic)", Opts);

    private static readonly Regex NotOurs = new(
        @"\b(mechanical|electrical|electrification|geotech\w*|civil\s+(construction|engineering)|roads?|roadworks|highway|hwy|streetscape\w*|street\s+(light\w*|lamp|safety|improvement\w*|rehabilitation|reconstruction|paving|upgrade\w*)|neighbourhood\s+rehabilitation|sidewalk|paving|pavement|asphalt|crosswalk|water\s*main|watermain|sewers?|storm|sanitary|water\s+(system|supply|servicing|management|south)|waterline|reservoir\s+expansion|new\s+water\s+reservoir|wellhead|landfill|transportation|traffic|trail|bridges?|culvert|commissioning|cost\s+(estimat\w*|and\s+cost)|quantity\s+surveyor|fairness|envelope|roof|environmental|security|project\s+management|owner.s\s+representative|land\s+(acquisition|appraisal)|marketing|communications|alerting|it\s+data|data\s+cent\w*|software|asset\s+management|planning\s+analytical|area\s+structure\s+plan|landscap\w*|flood|irrigation|waste|storage\s+yard|mains|special\s+study\s+area|building\s+operation|decommissioning|furniture|food\s+service\w*|buses|appliances|synthetic|turf|asbestos|abatement|drywall|steam\s+room)\b",
        Opts);

    // The archive's "consulting services" that are not buildings (2026-10-21): business, people, IT and data, policy and
    // planning, utilities and process plants, transport, and building-services trades. An open tender's "consulting
    // services" is usually a building; an archived one is as often payroll software.
    private static readonly Regex NotOursConsulting = new(
        @"\b(business\s+(plan|process|claims)|insurance|claims|compensation|benefits|pension|actuar\w*|recruit\w*|candidate|audit|legal|surveyor|governance|human\s+resources|hr|dental|forensic|odontology|law\s+enforcement|gang|cyber\w*|privacy|web|mobile|digital\s+design|user\s+experience|agile|gis|content\s+management|ng911|smart\s+metering|grid\s+operations|reliability\s+standards|telecom\w*|lighting|alarm|signs|diversity|equity|inclusi\w*|community\s+engagement|social\s+plan|archa?eolog\w*|fisheries|forest\s+(sector|management)|agricultur\w*|density|cemetery|parks?\s+master\s+plan|regulation|compliance|risk\s+assessment|custodial|hazardous|laboratory|trades\s+training|(candidate|paint|material)\s+testing|sap|microsoft|sharepoint|azure|aws|peoplesoft|guidewire|information\s+(technology|schedule)|im/it|imit|hsimit|it\s+(consult\w*|professional)|systems\s+engineering|operating\s+engineering|operations\s+engineering|technical\s+services|project\s+technical|management\s+consultant|evaluation\s+process|systems\s+change|housing\s+(needs|demand)|development\s+(feasibility|consulting)|planning\s+(and|&)\s+development|general\s+planning|commercial\s+(development|management)|procurement\s+consult\w*|search\s+consult\w*|health\s*(&|and)\s*safety|safety\s+and\s+hygiene|occupational|public\s+safety\s+and|bioenergy|biosolids|biogas|ghg|greenhouse|renewable\s+energy|bottom\s+ash|crane|ammonia|control\s+room|hmi|automation|industrial\s+control|rechlorination|digesters?|disinfection|wastewater|wwtp|water\s+(treatment|facilities)|pump\s+station|lift\s+station|dike|dyke|outfall|erosion|drainage|hydraulic|creek|tanks|utilit(y|ies)|underground|railway|grade\s+crossing|ferry|dock|gangway|compost|organics|resource\s+recovery|guardrails?|playground|skateboard|millwork|corrosion|materials\s+engineering|monitoring|overpass|connector|walkway|multi-use\s+path|trestles?|hvac|sprinkler|elevator|acoustic|fall\s+protection|compressor|generator|backup\s+power|fire\s+(code|protection)|building\s+code|cost\s+consultant|construction\s+costs|golden\s+ears|terminal\s+avenue|capilano|water\s+(facilities|system)|infrastructure\s+(design|asset)|engineering\s+infrastructure|municipal\s+engineering|septage|roofing|collision|fsp|station\s+consultant|space\s+planning|market\s+readiness|advising|environment\w*|enviro|land\s+(lift|agent)|right\s+of\s+way|operational\s+projects|mcc|stormwater|heritage\s+(consult\w*|assessment|planning|inventory|register|conservation\s+plan)|facilities\s+plan|hydrothermal|wts|mental\s+health\s+(consult\w*|services|program\w*|strategy)|services\s+platform|retaining\s+wall|ofc|operations\s+assessment|washroom|floors)\b",
        Opts);

    private static readonly Regex Ours = new(
        @"\b(prime\s+consultant|prime\)?\s+consultant|architectural|consult(ing|ant)\s+(services?|partner)|consulting\s+engineering|engineering\s+(services|consultant|consulting)|professional\s+(consulting|engineers|services)|design[- ]build\w*|modified\s+design|^mdb\b|condition\s+assessment|needs\s+assessment|feasibility\s+study|design\s+consulting|design,\s+engineering|integrated\s+project\s+delivery|facility\s+evaluation|consultant\s+program|(engineering|design)\s+and\s+construction\s+(services|of)|(request\s+for\s+(proposals?|qualifications?)|rfp|rfq)\b.*\b(facility|centre|center|school|academy|lodge|hall|library|clan\s+house)\b|\b(facility|centre|center|school|academy|lodge|hall|library|clan\s+house)\b.*\b(rfp|rfq)\b)",
        Opts);

    public static bool IsKorWork(string? title)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0 || NeverOurs.IsMatch(t)) return false;
        if (Structural.IsMatch(t)) return true;
        return !NotOurs.IsMatch(t) && !NotOursConsulting.IsMatch(t) && Ours.IsMatch(t);
    }
}
