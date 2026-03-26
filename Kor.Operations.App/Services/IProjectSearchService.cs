#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    public interface IProjectSearchService
    {
        Task BuildIndexAsync(CancellationToken ct = default);
        Task<IReadOnlyList<ProjectSearchResult>> SearchAsync(string query, int limit, CancellationToken ct);
        ProjectSelection Resolve(ProjectSearchResult project);
    }

    public sealed record ProjectSearchResult(string Name, string? Path);

    public sealed record ProjectSelection(
        string DisplayName,
        string? FolderPath,
        string ProjectNumber,
        string ProjectName);
}
