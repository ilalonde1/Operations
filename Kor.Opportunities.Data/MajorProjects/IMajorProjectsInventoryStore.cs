#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.MajorProjects;

public interface IMajorProjectsInventoryStore
{
    Task<IReadOnlyList<MajorProjectRow>> ListAllAsync(CancellationToken ct);
    Task<MajorProjectsFilterOptions> GetFilterOptionsAsync(CancellationToken ct);
}

public sealed record MajorProjectsFilterOptions(
    IReadOnlyList<string> Provinces,
    IReadOnlyList<string> Stages,
    IReadOnlyList<string> Sectors,
    IReadOnlyList<string> Regions);
