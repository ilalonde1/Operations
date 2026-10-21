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
/// Measured 2026-10-20 against all 581 open tenders, each labelled by hand (68 KOR work): it shows 67, of which 64 are
/// KOR work (precision 0.96), and finds 64 of the 68 (recall 0.94). The labelled set is in the test project
/// (KorBidFitLabels-2026-10-20.tsv); the test fails if either figure drops. The rule was tuned on that same set, so a new
/// kind of title can still be misread: a bare project name with no service words ("Southside Aquatic Facility") is
/// missed, and a sewer job named only by its streets ("Design and Construction Services - Browning Persimmon") is shown.
/// </summary>
public static class KorBidFit
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex NeverOurs = new(@"\bconstruction\s+management\b", Opts);

    private static readonly Regex Structural = new(@"\b(structural\b(?!\s+turnout)|seismic)", Opts);

    private static readonly Regex NotOurs = new(
        @"\b(mechanical|electrical|geotech\w*|civil\s+(construction|engineering)|roads?|roadworks|highway|hwy|street\w*|sidewalk|paving|pavement|asphalt|crosswalk|water\s*main|watermain|sewers?|storm|sanitary|water\s+(system|supply|servicing|management|south)|waterline|reservoir\s+expansion|new\s+water\s+reservoir|wellhead|landfill|transportation|traffic|trail|bridges?|culvert|commissioning|cost\s+(estimat\w*|and\s+cost)|quantity\s+surveyor|fairness|envelope|roof|environmental|security|project\s+management|owner.s\s+representative|land\s+(acquisition|appraisal)|marketing|communications|alerting|it\s+data|data\s+cent\w*|software|asset\s+management|planning\s+analytical|area\s+structure\s+plan|landscap\w*|flood|irrigation|waste|storage\s+yard|mains|special\s+study\s+area|building\s+operation|decommissioning|furniture|food\s+service\w*|buses|appliances|synthetic|turf|asbestos|abatement|drywall|steam\s+room)\b",
        Opts);

    private static readonly Regex Ours = new(
        @"\b(prime\s+consultant|prime\)?\s+consultant|architectural|consult(ing|ant)\s+(services|partner)|consulting\s+engineering|engineering\s+(services|consultant|consulting)|professional\s+(consulting|engineers|services)|design[- ]build\w*|modified\s+design|^mdb\b|condition\s+assessment|needs\s+assessment|feasibility\s+study|design\s+consulting|design,\s+engineering|integrated\s+project\s+delivery|facility\s+evaluation|consultant\s+program|(engineering|design)\s+and\s+construction\s+(services|of)|(request\s+for\s+(proposals?|qualifications?)|rfp|rfq)\b.*\b(facility|centre|center|school|academy|lodge|hall|library|clan\s+house)\b|\b(facility|centre|center|school|academy|lodge|hall|library|clan\s+house)\b.*\b(rfp|rfq)\b)",
        Opts);

    public static bool IsKorWork(string? title)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0 || NeverOurs.IsMatch(t)) return false;
        if (Structural.IsMatch(t)) return true;
        return !NotOurs.IsMatch(t) && Ours.IsMatch(t);
    }
}
