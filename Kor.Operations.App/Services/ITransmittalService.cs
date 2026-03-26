#nullable enable
using Kor.Operations.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    public interface ITransmittalService
    {
        Task<TransmittalSendResult> SendAsync(TransmittalSendRequest request, CancellationToken ct);
    }

    public sealed class TransmittalSendRequest
    {
        public required Transmittal Header { get; init; }
        public required IReadOnlyList<TransmittalFile> Files { get; init; }
        public required IReadOnlyList<string> ToRecipients { get; init; }
        public required IReadOnlyList<string> CcRecipients { get; init; }
        public required string Folder { get; init; }
        public required string SenderUpn { get; init; }
        public string? RemarksHtml { get; init; }
        public string? SignatureHtml { get; init; }
        public bool NeedExternal { get; init; }
        public bool AttachIfSmall { get; init; }
        public IProgress<(string file, long sent, long total)>? UploadProgress { get; init; }
        public IProgress<string>? Status { get; init; }
    }

    public sealed record TransmittalSendResult(
        string Folder,
        string CoverLocalPath,
        IReadOnlyList<string> AllRecipients);
}
