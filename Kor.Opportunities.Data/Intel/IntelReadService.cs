#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Intel;

public sealed class IntelReadService
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public IntelReadService(string opportunitiesConnectionString)
    {
        if (string.IsNullOrWhiteSpace(opportunitiesConnectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(opportunitiesConnectionString));
        }

        _connectionString = opportunitiesConnectionString;
    }

    public async Task<OrgIntelBundle> GetOrgIntelAsync(long canonicalOrgId, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var people = await GetPeopleAsync(con, canonicalOrgId, ct).ConfigureAwait(false);
        var signals = await GetSignalsAsync(con, canonicalOrgId, ct).ConfigureAwait(false);
        var actions = await GetActionsAsync(con, canonicalOrgId, ct).ConfigureAwait(false);
        var works = await GetWorksAsync(con, canonicalOrgId, ct).ConfigureAwait(false);
        var risks = await GetRisksAsync(con, canonicalOrgId, ct).ConfigureAwait(false);
        var narratives = await GetNarrativesAsync(con, canonicalOrgId, ct).ConfigureAwait(false);

        if (people.Count + signals.Count + actions.Count + works.Count + risks.Count + narratives.Count == 0)
        {
            return OrgIntelBundle.Empty;
        }

        var synopsis1 = FirstNarrative(narratives, "Current");
        var synopsis2 = FirstNarrative(narratives, "Action");
        return new OrgIntelBundle(
            synopsis1,
            synopsis2,
            people,
            signals,
            actions,
            works,
            risks,
            narratives);
    }

    public async Task<RegionIntelRollup> GetRegionIntelAsync(string province, string? city, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(province))
        {
            throw new ArgumentException("Province is required.", nameof(province));
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        // R81: Tokenize city via shared IntelRegionTokenizer so "GVRD" /
        // "Lower Mainland" / "Metro Vancouver" expand to the 21 munis, matching
        // SqlBriefDataStore.TokenizeCity. Previously the region intel queries
        // used a single LIKE '%@city%' and silently returned 0 rows for these
        // aliases, while the brief header counts were correct — visible
        // inconsistency on the rendered brief.
        var cityTokens = IntelRegionTokenizer.Tokenize(city);
        var topActions = await GetRegionActionsAsync(con, province, cityTokens, ct).ConfigureAwait(false);
        var leadership = await GetRegionLeadershipSignalsAsync(con, province, cityTokens, ct).ConfigureAwait(false);
        var capacity = await GetRegionCapacityRisksAsync(con, province, cityTokens, ct).ConfigureAwait(false);
        return new RegionIntelRollup(topActions, leadership, capacity);
    }

    public async Task<OpportunityIntelBundle> GetOpportunityIntelAsync(
        long? buyerCanonicalOrgId,
        long? architectCanonicalOrgId,
        CancellationToken ct)
    {
        var buyer = buyerCanonicalOrgId.HasValue
            ? await GetOrgIntelAsync(buyerCanonicalOrgId.Value, ct).ConfigureAwait(false)
            : null;
        var architect = architectCanonicalOrgId.HasValue
            ? await GetOrgIntelAsync(architectCanonicalOrgId.Value, ct).ConfigureAwait(false)
            : null;

        return new OpportunityIntelBundle(buyer, architect);
    }

    /// <summary>
    /// BD Dashboard Priority Actions queue. Returns up to <paramref name="take"/>
    /// open IntelAction rows joined to their CanonicalOrg, ranked High→Low
    /// confidence then most-recently-refreshed. Supports optional filter by
    /// province (via MPI canonical-org membership), action type, and minimum
    /// confidence.
    /// </summary>
    public async Task<IReadOnlyList<PriorityActionRow>> GetPriorityActionsAsync(
        PriorityActionFilter? filter,
        int take,
        CancellationToken ct)
    {
        if (take <= 0) take = 25;
        filter ??= new PriorityActionFilter();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var provinceClause = string.IsNullOrWhiteSpace(filter.Province)
            ? string.Empty
            : @"  AND a.CanonicalOrgId IN (
              SELECT DISTINCT v.CanonicalOrgId
              FROM opportunities.MajorProjectsInventory m
              CROSS APPLY (VALUES
                  (m.ArchitectCanonicalOrgId),
                  (m.ProponentCanonicalOrgId),
                  (m.StructuralEngineerCanonicalOrgId),
                  (m.GeneralContractorCanonicalOrgId)
              ) v(CanonicalOrgId)
              WHERE m.RetiredAtUtc IS NULL AND m.Province = @prov AND v.CanonicalOrgId IS NOT NULL)
";

        var actionTypeClause = string.IsNullOrWhiteSpace(filter.ActionType)
            ? string.Empty
            : "  AND a.ActionType = @actionType\n";

        var minConfidenceClause = filter.MinConfidence is null
            ? string.Empty
            : $"  AND {IntelConfidenceSql.RankClause("a.SourceConfidence")} >= @minConfidenceRank\n";

        var sql = $@"
SELECT TOP (@take)
       a.Id, a.CanonicalOrgId,
       co.DisplayName AS OrgDisplayName, co.Kind AS OrgKind, co.ClendorClientId,
       a.ActionType, a.Recommendation, a.TargetPersonName, a.TimingNotes,
       a.SourceProviderName, a.SourceConfidence, a.LastSeenAtUtc
FROM opportunities.IntelAction a
JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
WHERE a.Status = N'Open'
  AND a.RetiredAtUtc IS NULL
{provinceClause}{actionTypeClause}{minConfidenceClause}ORDER BY {IntelConfidenceSql.RankClause("a.SourceConfidence")} DESC,
         a.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
        if (!string.IsNullOrWhiteSpace(filter.Province))
            cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = filter.Province;
        if (!string.IsNullOrWhiteSpace(filter.ActionType))
            cmd.Parameters.Add("@actionType", SqlDbType.NVarChar, 50).Value = filter.ActionType;
        if (filter.MinConfidence is { } mc)
            cmd.Parameters.Add("@minConfidenceRank", SqlDbType.Int).Value = mc switch
            {
                IntelConfidence.High => 3,
                IntelConfidence.Medium => 2,
                IntelConfidence.Low => 1,
                _ => 0,
            };

        var now = DateTimeOffset.UtcNow;
        var rows = new List<PriorityActionRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var refreshed = r.GetDateTimeOffset(11);
            rows.Add(new PriorityActionRow(
                ActionId: r.GetInt64(0),
                CanonicalOrgId: r.GetInt64(1),
                OrgDisplayName: r.GetString(2),
                OrgKind: r.GetString(3),
                OrgClendorClientId: r.IsDBNull(4) ? null : r.GetString(4),
                ActionType: r.GetString(5),
                Recommendation: r.GetString(6),
                TargetPersonName: r.IsDBNull(7) ? null : r.GetString(7),
                TimingNotes: r.IsDBNull(8) ? null : r.GetString(8),
                SourceProviderName: r.GetString(9),
                Confidence: ParseConfidence(r.GetString(10)),
                RefreshedAtUtc: refreshed,
                Freshness: ComputeFreshness(now, refreshed)));
        }
        return rows;
    }

    private static async Task<IReadOnlyList<IntelPersonRow>> GetPeopleAsync(SqlConnection con, long canonicalOrgId, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP 20 p.Id, p.DisplayName, p.Email, p.Phone, p.LinkedinUrl,
       a.Title, a.IsCurrent, a.Notes,
       p.Corroborations, a.SourceProviderName, a.SourceConfidence, a.LastSeenAtUtc
FROM opportunities.IntelPerson p
JOIN opportunities.IntelPersonAffiliation a ON a.IntelPersonId = p.Id
WHERE a.CanonicalOrgId = @id
  AND a.RetiredAtUtc IS NULL
  AND p.RetiredAtUtc IS NULL
ORDER BY a.IsCurrent DESC,
         {IntelConfidenceSql.RankClause("a.SourceConfidence")} DESC,
         p.LastSeenAtUtc DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;

        var now = DateTimeOffset.UtcNow;
        var rows = new List<IntelPersonRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var refreshed = r.GetDateTimeOffset(11);
            rows.Add(new IntelPersonRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetBoolean(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.GetInt32(8),
                r.GetString(9),
                ParseConfidence(r.GetString(10)),
                refreshed,
                ComputeFreshness(now, refreshed)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<IntelSignalRow>> GetSignalsAsync(SqlConnection con, long canonicalOrgId, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP 20 Id, SignalType, Subject, Detail, OccurredAtApprox, SourceUrl,
       Corroborations, SourceProviderName, SourceConfidence, LastSeenAtUtc
FROM opportunities.IntelSignal
WHERE CanonicalOrgId = @id
  AND RetiredAtUtc IS NULL
ORDER BY {IntelConfidenceSql.RankClause("SourceConfidence")} DESC,
         LastSeenAtUtc DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        return await ReadSignalsAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IntelActionRow>> GetActionsAsync(SqlConnection con, long canonicalOrgId, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP 20 Id, ActionType, Recommendation, TargetPersonName, TimingNotes, Status,
       SourceProviderName, SourceConfidence, LastSeenAtUtc
FROM opportunities.IntelAction
WHERE CanonicalOrgId = @id
  AND Status = N'Open'
  AND RetiredAtUtc IS NULL
ORDER BY {IntelConfidenceSql.RankClause("SourceConfidence")} DESC,
         LastSeenAtUtc DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        return await ReadActionsAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IntelWorkRow>> GetWorksAsync(SqlConnection con, long canonicalOrgId, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP 20 Id, ProjectName, Role, YearApprox, EstimatedValueCad, EstimatedValueText,
       Notes, MajorProjectsInventoryId, SourceProviderName, SourceConfidence, LastSeenAtUtc
FROM opportunities.IntelWork
WHERE CanonicalOrgId = @id
  AND RetiredAtUtc IS NULL
ORDER BY {IntelConfidenceSql.RankClause("SourceConfidence")} DESC,
         LastSeenAtUtc DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;

        var now = DateTimeOffset.UtcNow;
        var rows = new List<IntelWorkRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var refreshed = r.GetDateTimeOffset(10);
            rows.Add(new IntelWorkRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetDecimal(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetInt64(7),
                r.GetString(8),
                ParseConfidence(r.GetString(9)),
                refreshed,
                ComputeFreshness(now, refreshed)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<IntelRiskRow>> GetRisksAsync(SqlConnection con, long canonicalOrgId, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP 20 Id, RiskType, Description, MitigationNotes, SourceProviderName, SourceConfidence, LastSeenAtUtc
FROM opportunities.IntelRisk
WHERE CanonicalOrgId = @id
  AND RetiredAtUtc IS NULL
ORDER BY {IntelConfidenceSql.RankClause("SourceConfidence")} DESC,
         LastSeenAtUtc DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        return await ReadRisksAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IntelNarrativeRow>> GetNarrativesAsync(SqlConnection con, long canonicalOrgId, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 20 Id, NarrativeType, ParagraphText, SourceProviderName, SourceConfidence, LastSeenAtUtc
FROM opportunities.IntelNarrative
WHERE CanonicalOrgId = @id
  AND RetiredAtUtc IS NULL
ORDER BY CASE NarrativeType WHEN N'Current' THEN 0 WHEN N'Action' THEN 1 WHEN N'Summary' THEN 2 ELSE 3 END,
         LastSeenAtUtc DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        return await ReadNarrativesAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IntelActionRow>> GetRegionActionsAsync(
        SqlConnection con,
        string province,
        IReadOnlyList<string> cityTokens,
        CancellationToken ct)
    {
        var cityClause = BuildMpiCityClause(cityTokens, "c");
        var sql = $@"
WITH RegionOrgIds AS (
    SELECT DISTINCT v.CanonicalOrgId
    FROM opportunities.MajorProjectsInventory m
    CROSS APPLY (VALUES
        (m.ArchitectCanonicalOrgId),
        (m.ProponentCanonicalOrgId),
        (m.StructuralEngineerCanonicalOrgId),
        (m.GeneralContractorCanonicalOrgId)
    ) v(CanonicalOrgId)
    WHERE m.RetiredAtUtc IS NULL
      AND m.Province = @prov
      {cityClause}
      AND v.CanonicalOrgId IS NOT NULL
)
SELECT TOP 20 a.Id, a.ActionType, a.Recommendation, a.TargetPersonName, a.TimingNotes, a.Status,
       a.SourceProviderName, a.SourceConfidence, a.LastSeenAtUtc
FROM opportunities.IntelAction a
JOIN RegionOrgIds r ON r.CanonicalOrgId = a.CanonicalOrgId
WHERE a.Status = N'Open'
  AND a.RetiredAtUtc IS NULL
ORDER BY {IntelConfidenceSql.RankClause("a.SourceConfidence")} DESC,
         a.LastSeenAtUtc DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindProvince(cmd, province);
        BindCityTokens(cmd, cityTokens, "c");
        return await ReadActionsAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IntelSignalRow>> GetRegionLeadershipSignalsAsync(
        SqlConnection con,
        string province,
        IReadOnlyList<string> cityTokens,
        CancellationToken ct)
    {
        var cityClause = BuildMpiCityClause(cityTokens, "c");
        var sql = $@"
WITH RegionOrgIds AS (
    SELECT DISTINCT v.CanonicalOrgId
    FROM opportunities.MajorProjectsInventory m
    CROSS APPLY (VALUES
        (m.ArchitectCanonicalOrgId),
        (m.ProponentCanonicalOrgId),
        (m.StructuralEngineerCanonicalOrgId),
        (m.GeneralContractorCanonicalOrgId)
    ) v(CanonicalOrgId)
    WHERE m.RetiredAtUtc IS NULL
      AND m.Province = @prov
      {cityClause}
      AND v.CanonicalOrgId IS NOT NULL
)
SELECT TOP 20 s.Id, s.SignalType, s.Subject, s.Detail, s.OccurredAtApprox, s.SourceUrl,
       s.Corroborations, s.SourceProviderName, s.SourceConfidence, s.LastSeenAtUtc
FROM opportunities.IntelSignal s
JOIN RegionOrgIds r ON r.CanonicalOrgId = s.CanonicalOrgId
WHERE s.SignalType = N'LeadershipChange'
  AND s.LastSeenAtUtc >= DATEADD(DAY, -90, sysdatetimeoffset())
  AND s.RetiredAtUtc IS NULL
ORDER BY {IntelConfidenceSql.RankClause("s.SourceConfidence")} DESC,
         s.LastSeenAtUtc DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindProvince(cmd, province);
        BindCityTokens(cmd, cityTokens, "c");
        return await ReadSignalsAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IntelRiskRow>> GetRegionCapacityRisksAsync(
        SqlConnection con,
        string province,
        IReadOnlyList<string> cityTokens,
        CancellationToken ct)
    {
        var cityClause = BuildMpiCityClause(cityTokens, "c");
        var sql = $@"
WITH RegionOrgIds AS (
    SELECT DISTINCT v.CanonicalOrgId
    FROM opportunities.MajorProjectsInventory m
    CROSS APPLY (VALUES
        (m.ArchitectCanonicalOrgId),
        (m.ProponentCanonicalOrgId),
        (m.StructuralEngineerCanonicalOrgId),
        (m.GeneralContractorCanonicalOrgId)
    ) v(CanonicalOrgId)
    WHERE m.RetiredAtUtc IS NULL
      AND m.Province = @prov
      {cityClause}
      AND v.CanonicalOrgId IS NOT NULL
)
SELECT TOP 20 x.Id, x.RiskType, x.Description, x.MitigationNotes,
       x.SourceProviderName, x.SourceConfidence, x.LastSeenAtUtc
FROM opportunities.IntelRisk x
JOIN RegionOrgIds r ON r.CanonicalOrgId = x.CanonicalOrgId
WHERE x.RiskType = N'CapacityStrain'
  AND x.RetiredAtUtc IS NULL
ORDER BY {IntelConfidenceSql.RankClause("x.SourceConfidence")} DESC,
         x.LastSeenAtUtc DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindProvince(cmd, province);
        BindCityTokens(cmd, cityTokens, "c");
        return await ReadRisksAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IntelActionRow>> ReadActionsAsync(SqlCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<IntelActionRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var refreshed = r.GetDateTimeOffset(8);
            rows.Add(new IntelActionRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5),
                r.GetString(6),
                ParseConfidence(r.GetString(7)),
                refreshed,
                ComputeFreshness(now, refreshed)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<IntelSignalRow>> ReadSignalsAsync(SqlCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<IntelSignalRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var refreshed = r.GetDateTimeOffset(9);
            rows.Add(new IntelSignalRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetInt32(6),
                r.GetString(7),
                ParseConfidence(r.GetString(8)),
                refreshed,
                ComputeFreshness(now, refreshed)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<IntelRiskRow>> ReadRisksAsync(SqlCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<IntelRiskRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var refreshed = r.GetDateTimeOffset(6);
            rows.Add(new IntelRiskRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4),
                ParseConfidence(r.GetString(5)),
                refreshed,
                ComputeFreshness(now, refreshed)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<IntelNarrativeRow>> ReadNarrativesAsync(SqlCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<IntelNarrativeRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var refreshed = r.GetDateTimeOffset(5);
            rows.Add(new IntelNarrativeRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                ParseConfidence(r.GetString(4)),
                refreshed,
                ComputeFreshness(now, refreshed)));
        }

        return rows;
    }

    private static void BindProvince(SqlCommand cmd, string province)
    {
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
    }

    /// <summary>
    /// Binds @{prefix}0, @{prefix}1, ... for each city token. Matches the
    /// fragment built by <see cref="BuildMpiCityClause"/>.
    /// </summary>
    private static void BindCityTokens(SqlCommand cmd, IReadOnlyList<string> tokens, string paramPrefix)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            cmd.Parameters.Add("@" + paramPrefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture), SqlDbType.NVarChar, 150).Value = tokens[i];
        }
    }

    /// <summary>
    /// Builds an "AND (m.MunicipalityName IN (@c0,@c1,...) OR m.RegionName IN (@c0,@c1,...))"
    /// fragment for the supplied tokens. Returns empty string when tokens is empty
    /// (province-wide query).
    ///
    /// Uses IN match (sargable) rather than LIKE '%X%' (non-sargable) because
    /// IntelRegionTokenizer expands aliases to exact municipality names. A
    /// 21-muni GVRD expansion ran in &gt;30s as LIKE; under IN it runs in
    /// sub-second against the MajorProjectsInventory indexes.
    /// </summary>
    private static string BuildMpiCityClause(IReadOnlyList<string> tokens, string paramPrefix)
    {
        if (tokens.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder(" AND (m.MunicipalityName IN (");
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('@').Append(paramPrefix).Append(i);
        }
        sb.Append(") OR m.RegionName IN (");
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('@').Append(paramPrefix).Append(i);
        }
        sb.Append("))");
        return sb.ToString();
    }

    private static string? FirstNarrative(IReadOnlyList<IntelNarrativeRow> narratives, string narrativeType)
    {
        foreach (var n in narratives)
        {
            if (string.Equals(n.NarrativeType, narrativeType, StringComparison.OrdinalIgnoreCase))
            {
                return n.ParagraphText;
            }
        }

        return null;
    }

    private static IntelConfidence ParseConfidence(string value) =>
        value.Equals("High", StringComparison.OrdinalIgnoreCase)
            ? IntelConfidence.High
            : value.Equals("Low", StringComparison.OrdinalIgnoreCase)
                ? IntelConfidence.Low
                : IntelConfidence.Medium;

    private static IntelFreshness ComputeFreshness(DateTimeOffset now, DateTimeOffset refreshedAtUtc)
    {
        var days = (now - refreshedAtUtc).TotalDays;
        if (days <= 30)
        {
            return IntelFreshness.Fresh;
        }

        return days <= 90 ? IntelFreshness.Aged : IntelFreshness.Stale;
    }
}
