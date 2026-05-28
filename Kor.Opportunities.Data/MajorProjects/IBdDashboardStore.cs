#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.MajorProjects;

public interface IBdDashboardStore
{
    Task<IReadOnlyList<BdOpenStructuralSeatRow>> GetOpenStructuralSeatsAsync(int take, CancellationToken ct);

    Task<IReadOnlyList<BdCompetitorWatchRow>> GetCompetitorWatchAsync(int take, CancellationToken ct);

    Task<IReadOnlyList<BdForwardPipelineRow>> GetForwardPipelineAsync(int take, CancellationToken ct);

    Task<IReadOnlyList<DataHealthRow>> GetDataHealthAsync(CancellationToken ct);
}

public sealed record BdOpenStructuralSeatRow(
    long OrgId,
    string DisplayName,
    string? Market,
    string? Status,
    string? Priority,
    string? DisplacementRead);

public sealed record BdCompetitorWatchRow(
    long OrgId,
    string DisplayName,
    string? CapacityRead);

public sealed record BdForwardPipelineRow(
    long Id,
    string ProjectName,
    string? ProponentName,
    string? Province,
    string? Sector,
    decimal? EstimatedCostCad,
    string? Stage);

public sealed record DataHealthRow(
    string Category,
    string Label,
    long Count);
