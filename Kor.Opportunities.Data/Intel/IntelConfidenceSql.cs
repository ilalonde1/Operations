#nullable enable
namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Single source of truth for the SQL CASE expression that converts the
/// NVARCHAR SourceConfidence column ('High'/'Medium'/'Low') into a sortable
/// integer. Previously inlined in IntelReadService.GetActionsAsync and
/// GetRegionActionsAsync but absent from other read queries, leading to
/// inconsistent ordering across briefs and dossier.
///
/// Usage: ORDER BY {IntelConfidenceSql.RankClause("a.SourceConfidence")} DESC, ...
/// </summary>
public static class IntelConfidenceSql
{
    public static string RankClause(string column) =>
        $"CASE {column} WHEN N'High' THEN 3 WHEN N'Medium' THEN 2 WHEN N'Low' THEN 1 ELSE 0 END";
}
