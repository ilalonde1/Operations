#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.MajorProjects;

public sealed class SqlPursuitBriefStore : IPursuitBriefStore
{
    private const int CommandTimeoutSeconds = 60;
    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");
    private readonly string _connectionString;

    public SqlPursuitBriefStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<PursuitBrief?> GetBriefForProjectAsync(long mpiId, CancellationToken ct)
    {
        var project = await ReadProjectAsync(mpiId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        var structuralJson = project.ArchitectCanonicalOrgId.HasValue
            ? await ReadEnrichmentJsonAsync(project.ArchitectCanonicalOrgId.Value, "StructuralPartnerMap", ct).ConfigureAwait(false)
            : null;
        var structural = ParseStructuralPartnerMap(structuralJson);

        var architectContacts = project.ArchitectCanonicalOrgId.HasValue
            ? ParsePeople(await ReadEnrichmentJsonAsync(project.ArchitectCanonicalOrgId.Value, "DecisionMakers", ct).ConfigureAwait(false))
            : Array.Empty<PursuitBriefPerson>();

        var ownerProcurement = project.ProponentCanonicalOrgId.HasValue
            ? ParseProcurement(await ReadEnrichmentJsonAsync(project.ProponentCanonicalOrgId.Value, "ProcurementProfile", ct).ConfigureAwait(false))
            : EmptyProcurement();

        var ownerContacts = project.ProponentCanonicalOrgId.HasValue
            ? ParsePeople(await ReadEnrichmentJsonAsync(project.ProponentCanonicalOrgId.Value, "DecisionMakers", ct).ConfigureAwait(false))
            : Array.Empty<PursuitBriefPerson>();

        var competitorNotes = await ReadCompetitorNotesAsync(structural.RecurringStructuralPartners, ct).ConfigureAwait(false);

        return new PursuitBrief(
            new PursuitBriefProject(
                project.Id,
                project.ProjectName,
                project.ProponentName,
                project.RegionName,
                project.MunicipalityName,
                project.Sector,
                project.ProjectStage ?? project.Stage,
                FormatCurrency(project.EstimatedCostCad),
                project.SourceUrl),
            project.OwnerClendorClientId,
            project.ArchitectClendorClientId,
            new PursuitBriefArchitect(
                project.ArchitectName,
                project.ArchitectCanonicalOrgId,
                structural.SeatStatus,
                structural.SeatPriority,
                structural.DisplacementRead,
                structural.RecurringStructuralPartners),
            ownerProcurement,
            architectContacts,
            ownerContacts,
            competitorNotes,
            KorEdge: null,
            ThePlay: null,
            FitScore: null);
    }

    private async Task<ProjectSeed?> ReadProjectAsync(long mpiId, CancellationToken ct)
    {
        const string sql = @"
SELECT mpi.Id,
       mpi.ProjectName,
       mpi.ProponentName,
       mpi.ProponentCanonicalOrgId,
       mpi.RegionName,
       mpi.MunicipalityName,
       mpi.Sector,
       mpi.Stage,
       mpi.ProjectStage,
       mpi.EstimatedCostCad,
       mpi.SourceUrl,
       mpi.ArchitectName,
       mpi.ArchitectCanonicalOrgId,
       ownerOrg.ClendorClientId AS OwnerClendorClientId,
       architectOrg.ClendorClientId AS ArchitectClendorClientId
FROM opportunities.MajorProjectsInventory mpi
LEFT JOIN opportunities.CanonicalOrg ownerOrg
    ON ownerOrg.Id = mpi.ProponentCanonicalOrgId
LEFT JOIN opportunities.CanonicalOrg architectOrg
    ON architectOrg.Id = mpi.ArchitectCanonicalOrgId
WHERE mpi.Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new ProjectSeed(
            r.GetInt64(0),
            r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetInt64(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.IsDBNull(9) ? null : r.GetDecimal(9),
            r.IsDBNull(10) ? null : r.GetString(10),
            r.IsDBNull(11) ? null : r.GetString(11),
            r.IsDBNull(12) ? null : r.GetInt64(12),
            r.IsDBNull(13) ? null : r.GetString(13),
            r.IsDBNull(14) ? null : r.GetString(14));
    }

    private async Task<string?> ReadEnrichmentJsonAsync(long canonicalOrgId, string providerName, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 ResultJson
FROM opportunities.CanonicalOrgEnrichment
WHERE CanonicalOrgId = @id
  AND ProviderName = @provider;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 60).Value = providerName;

        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<PursuitBriefCompetitorNote>> ReadCompetitorNotesAsync(
        IReadOnlyList<string> recurringStructuralPartners,
        CancellationToken ct)
    {
        var notes = new List<PursuitBriefCompetitorNote>();
        foreach (var firm in recurringStructuralPartners.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var orgId = await ReadCompetitorOrgIdAsync(firm, ct).ConfigureAwait(false);
            if (!orgId.HasValue)
            {
                continue;
            }

            var json = await ReadEnrichmentJsonAsync(orgId.Value, "CompetitorSignals", ct).ConfigureAwait(false);
            var capacityRead = ReadJsonString(json, "capacityRead");
            notes.Add(new PursuitBriefCompetitorNote(firm, capacityRead));
        }

        return notes;
    }

    private async Task<long?> ReadCompetitorOrgIdAsync(string displayName, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 Id
FROM opportunities.CanonicalOrg
WHERE Kind = N'Competitor'
  AND DisplayName = @name
ORDER BY Id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 500).Value = displayName.Trim();

        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static PursuitBriefOwnerProcurement ParseProcurement(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? EmptyProcurement()
            : WithRoot(
                json,
                root => new PursuitBriefOwnerProcurement(
                    JsonString(root, "procurementMethod"),
                    JsonString(root, "rosterProgram"),
                    JsonString(root, "evaluationCriteria"),
                    JsonString(root, "budgetCadence")),
                EmptyProcurement);

    private static PursuitBriefOwnerProcurement EmptyProcurement()
        => new(null, null, null, null);

    private static StructuralPartnerDetails ParseStructuralPartnerMap(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new StructuralPartnerDetails(null, null, null, Array.Empty<string>())
            : WithRoot(
                json,
                root => new StructuralPartnerDetails(
                    JsonString(root, "structuralPartnerStatus"),
                    JsonString(root, "korPriority"),
                    JsonString(root, "korDisplacementRead"),
                    JsonStringArray(root, "recurringStructuralPartners", "firm")),
                () => new StructuralPartnerDetails(null, null, null, Array.Empty<string>()));

    private static IReadOnlyList<PursuitBriefPerson> ParsePeople(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? Array.Empty<PursuitBriefPerson>()
            : WithRoot(
                json,
                root =>
                {
                    if (!root.TryGetProperty("people", out var people) || people.ValueKind != JsonValueKind.Array)
                    {
                        return Array.Empty<PursuitBriefPerson>();
                    }

                    return people.EnumerateArray()
                        .Where(p => p.ValueKind == JsonValueKind.Object)
                        .Select(p => new PursuitBriefPerson(
                            JsonString(p, "name"),
                            JsonString(p, "title"),
                            JsonString(p, "role"),
                            JsonString(p, "email")))
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                        .Take(5)
                        .ToArray();
                },
                Array.Empty<PursuitBriefPerson>);

    private static string? ReadJsonString(string? json, string propertyName)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : WithRoot(json, root => JsonString(root, propertyName), () => null);

    private static T WithRoot<T>(string json, Func<JsonElement, T> read, Func<T> fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? read(doc.RootElement) : fallback();
        }
        catch (JsonException)
        {
            return fallback();
        }
    }

    private static string? JsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => NullIfBlank(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => NullIfBlank(value.ToString()),
        };
    }

    private static IReadOnlyList<string> JsonStringArray(JsonElement element, string propertyName, string objectPropertyName)
    {
        if (!element.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return array.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Object ? JsonString(item, objectPropertyName) : JsonStringValue(item))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? JsonStringValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => NullIfBlank(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatCurrency(decimal? value)
        => value.HasValue ? string.Format(CanadianCulture, "{0:C0}", value.Value) : "";

    private sealed record ProjectSeed(
        long Id,
        string ProjectName,
        string? ProponentName,
        long? ProponentCanonicalOrgId,
        string? RegionName,
        string? MunicipalityName,
        string? Sector,
        string? Stage,
        string? ProjectStage,
        decimal? EstimatedCostCad,
        string? SourceUrl,
        string? ArchitectName,
        long? ArchitectCanonicalOrgId,
        string? OwnerClendorClientId,
        string? ArchitectClendorClientId);

    private sealed record StructuralPartnerDetails(
        string? SeatStatus,
        string? SeatPriority,
        string? DisplacementRead,
        IReadOnlyList<string> RecurringStructuralPartners);
}
