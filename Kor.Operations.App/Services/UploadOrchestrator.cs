#nullable enable
using Kor.Operations.Core;
using Kor.Operations.Graph;
using Kor.Operations.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    public sealed class UploadOrchestrator : IUploadOrchestrator
    {
        private readonly IGraphFacade _graphFacade;

        public UploadOrchestrator(IGraphFacade graphFacade)
        {
            _graphFacade = graphFacade ?? throw new ArgumentNullException(nameof(graphFacade));
        }

        public Task RenderPreviewAsync(
            string outputPath,
            Transmittal header,
            IReadOnlyList<TransmittalFile> files,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return CoverSheetRenderer.RenderAsync(outputPath, header, files);
        }

        public async Task<UploadOrchestrationResult> UploadAsync(
            Transmittal header,
            IReadOnlyList<TransmittalFile> files,
            string folder,
            bool needExternal,
            IProgress<(string file, long sent, long total)>? progress,
            IProgress<string>? status,
            CancellationToken ct)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Folder is required.", nameof(folder));

            // Resolve the destination folder once — avoids N redundant Graph round trips.
            status?.Report("Preparing folder...");
            var folderId = await _graphFacade.EnsureFolderAsync(folder, ct).ConfigureAwait(false);

            // Upload all content files in parallel (≤3 concurrent).
            using var semaphore = new SemaphoreSlim(3, 3);
            var uploadTasks = files.Select(async file =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    status?.Report($"Uploading {file.FileName}...");
                    var localPath = file.LocalPath;
                    var result = await _graphFacade.UploadToFolderAsync(
                        folderId,
                        file.FileName,
                        localPath,
                        new Progress<(string file, long sent, long total)>(p =>
                            progress?.Report((localPath, p.sent, p.total))),
                        ct).ConfigureAwait(false);
                    return (file, webUrl: result.WebUrl);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var uploadResults = await Task.WhenAll(uploadTasks).ConfigureAwait(false);
            foreach (var (file, webUrl) in uploadResults)
                file.SharePointPath = webUrl;

            // Render cover sheet (needs SharePointPath set on each file above).
            status?.Report("Creating cover sheet...");
            var fallbackName = $"{header.TransmittalNo}-Cover.pdf";
            var coverLocalPath = Path.Combine(Path.GetTempPath(), fallbackName);
            await CoverSheetRenderer.RenderAsync(coverLocalPath, header, files).ConfigureAwait(false);

            var coverFileName = string.IsNullOrWhiteSpace(header.CoverSheetFileName)
                ? fallbackName
                : header.CoverSheetFileName;
            header.CoverSheetFileName = coverFileName;

            status?.Report("Uploading cover sheet...");
            var coverUpload = await _graphFacade.UploadToFolderAsync(
                folderId,
                coverFileName,
                coverLocalPath,
                new Progress<(string file, long sent, long total)>(p =>
                    progress?.Report((coverLocalPath, p.sent, p.total))),
                ct).ConfigureAwait(false);

            status?.Report("Creating links...");
            var links = await _graphFacade.CreateLinksForFolderAsync(folderId, needExternal, ct).ConfigureAwait(false);
            header.InternalLink = links.InternalLink;
            header.ExternalLink = links.ExternalLink;

            return new UploadOrchestrationResult(
                folder,
                coverLocalPath,
                coverUpload.DriveId,
                coverUpload.ItemId,
                coverUpload.WebUrl,
                links.InternalLink,
                links.ExternalLink);
        }
    }
}
