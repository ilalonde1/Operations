#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Rendering.Brochure
{
    public interface IBrochureRenderer
    {
        Task<string> RenderAsync(BrochureContent content, string outputPath, CancellationToken ct);

        Task<IReadOnlyList<byte[]>> RenderPreviewAsync(
            BrochureContent content,
            int maxWidthPixels,
            CancellationToken ct);
    }
}
