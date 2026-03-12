#nullable enable
using Kor.Operations.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    public interface IUploadOrchestrator
    {
        Task RenderPreviewAsync(
            string outputPath,
            Transmittal header,
            IReadOnlyList<TransmittalFile> files,
            CancellationToken ct);

        Task<UploadOrchestrationResult> UploadAsync(
            Transmittal header,
            IReadOnlyList<TransmittalFile> files,
            string folder,
            bool needExternal,
            IProgress<(string file, long sent, long total)>? progress,
            IProgress<string>? status,
            CancellationToken ct);
    }

    public sealed record UploadOrchestrationResult(
        string Folder,
        string CoverLocalPath,
        string CoverSharePointUrl,
        string InternalLink,
        string? ExternalLink);
}
