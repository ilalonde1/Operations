#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Rendering.Brochure
{
    public interface IBrochureDocxRenderer
    {
        Task<string> RenderAsync(BrochureContent content, string outputPath, CancellationToken ct);
    }
}
