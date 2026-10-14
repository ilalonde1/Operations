namespace Kor.Opportunities.Data.BdReports;

/// <summary>
/// SQL population rules shared by the report readers and integrity gates.
/// Fragments use fixed aliases/parameters; no user text is interpolated here.
/// </summary>
public static class BdReportPopulationSql
{
    // Caller SELECTs COUNT(DISTINCT p.Id) and adds the organisation predicate.
    public const string CurrentPeopleFrom = @"
FROM opportunities.IntelPersonAffiliation pa
JOIN opportunities.IntelPerson p ON p.Id = pa.IntelPersonId
WHERE pa.RetiredAtUtc IS NULL AND pa.IsCurrent = 1 AND p.RetiredAtUtc IS NULL";

    public const string HoningVerdict =
        "UPPER(LTRIM(RTRIM(COALESCE(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'), JSON_VALUE(e.ResultJson, '$.verdict')))))";

    // Fixed report aliases: inventory m and ProjectBriefHoning enrichment e.
    public const string LivePursuit = "m.RetiredAtUtc IS NULL AND m.DismissedAtUtc IS NULL AND "
        + HoningVerdict + " IN (N'PURSUE', N'PURSUE_URGENT')";

    // Prefix/parameter names are developer literals, never user input. Sharing
    // these builders keeps joined sector queries qualified without duplicating
    // the unaliased region eligibility rules. A read captures one @asOf.
    public static string LiveRfp(string prefix, string provinceParameter) => $@"
{prefix}Status = 1 AND {prefix}IsPrimeConsultantRfp = 1 AND {prefix}ProjectProvince = {provinceParameter}
AND {prefix}DismissedAtUtc IS NULL AND {prefix}SubmissionDeadlineUtc > @asOf";

    public static string ForwardProject(string prefix, string provinceParameter) => $@"
{prefix}RetiredAtUtc IS NULL AND {prefix}DismissedAtUtc IS NULL AND {prefix}Province = {provinceParameter}
AND {prefix}ProjectStage IN (N'CapitalPlan', N'FacilityRenewal')";

    public static readonly string RegionLiveRfp = LiveRfp("", "@prov");
    public static readonly string RegionForward = ForwardProject("", "@prov");

    // Explicit discipline evidence only. Ambiguous/mixed/freeform roles are
    // excluded rather than converting 'engineer' or a negation into structural.
    public const string StructuralRole = @"
LOWER(LTRIM(RTRIM(ISNULL(iw.Role,N'')))) IN
 (N'structural', N'structural engineer', N'structural engineering',
  N'structural consultant', N'structural engineering consultant',
  N'structural engineer of record', N'structural design engineer')";

    private const string CanadianProvinces = "N'AB',N'BC',N'MB',N'NB',N'NL',N'NS',N'NT',N'NU',N'ON',N'PE',N'QC',N'SK',N'YT'";
    private const string UsStates = "N'AL',N'AK',N'AZ',N'AR',N'CA',N'CO',N'CT',N'DE',N'DC',N'FL',N'GA',N'HI',N'ID',N'IL',N'IN',N'IA',N'KS',N'KY',N'LA',N'ME',N'MD',N'MA',N'MI',N'MN',N'MS',N'MO',N'MT',N'NE',N'NV',N'NH',N'NJ',N'NM',N'NY',N'NC',N'ND',N'OH',N'OK',N'OR',N'PA',N'RI',N'SC',N'SD',N'TN',N'TX',N'UT',N'VT',N'VA',N'WA',N'WV',N'WI',N'WY',N'AS',N'GU',N'MP',N'PR',N'VI'";

    // A deliberately narrow KOR involvement claim: the resolved project SE is
    // an active KorStructural organisation. Region NULL/unrecognised => no match.
    // NorthAmerica supports recorded Canadian/US subdivision codes; it does not
    // guess a country from names. International imposes no geographic restriction.
    public const string AwardProjectMatch = @"
m.RetiredAtUtc IS NULL
AND EXISTS (SELECT 1 FROM opportunities.CanonicalOrg kor
            WHERE kor.Id = m.StructuralEngineerCanonicalOrgId
              AND kor.Kind = N'KorStructural' AND kor.RetiredAtUtc IS NULL)
AND (ap.Region = N'International'
     OR (ap.Region = N'Canada' AND m.Province IN (" + CanadianProvinces + @"))
     OR (ap.Region = N'US' AND m.Province IN (" + UsStates + @"))
     OR (ap.Region = N'NorthAmerica' AND m.Province IN (" + CanadianProvinces + "," + UsStates + @"))
     OR (ap.Region = m.Province AND m.Province IN (" + CanadianProvinces + "," + UsStates + @")))";
}
