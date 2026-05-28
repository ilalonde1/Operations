#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.MajorProjects;

public interface IPursuitBriefStore
{
    Task<PursuitBrief?> GetBriefForProjectAsync(long mpiId, CancellationToken ct);
}
