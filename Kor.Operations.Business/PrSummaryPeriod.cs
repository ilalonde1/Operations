#nullable enable

namespace Kor.Operations.Financials
{
    /// <summary>
    /// The latest posted PRSummaryMain period, on real projects only. A bare MAX(Period) read 202611 on 17 Oct 2026
    /// from one empty row on placeholder 99999-00, while the real latest period was 202610 (164 rows, 136 projects).
    /// </summary>
    public static class PrSummaryPeriod
    {
        // The same WBS1 rules as the Financials snapshot (FinancialsService project query).
        public static string MaxPostedPeriodSql(string catalog) => $@"SELECT MAX(Period) FROM [{catalog}].dbo.PRSummaryMain
WHERE WBS1 NOT LIKE '[A-Z]%' AND WBS1 NOT LIKE '9[A-Z]%' AND WBS1 NOT LIKE '99%';";
    }
}
