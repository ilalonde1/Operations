#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Brochure;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.Rendering.Brochure
{
    public sealed class BrochureRenderer : IBrochureRenderer
    {
        private readonly ILogger<BrochureRenderer> _logger;

        public BrochureRenderer(ILogger<BrochureRenderer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<string> RenderAsync(BrochureContent content, string outputPath, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            _logger.LogInformation(
                "Brochure render requested for template {TemplateName} to {OutputPath}. Cancellation requested: {CancellationRequested}",
                content.TemplateName,
                outputPath,
                ct.IsCancellationRequested);

            throw new NotImplementedException("BrochureRenderer not yet implemented");
        }
    }
}
