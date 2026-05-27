#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.MajorProjects;

public interface IPrimePipelineStore
{
    Task<IReadOnlyList<PrimePipelineRow>> GetAllAsync(CancellationToken ct);
}
