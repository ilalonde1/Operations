#nullable enable
using System.Collections.Generic;
using System.Threading;
using Kor.Operations.Core.Models.Brochure;
using QuestPDF.Infrastructure;

namespace Kor.Operations.Rendering.Brochure
{
    public interface IBrochureLayoutTemplate
    {
        string Id { get; }

        string DisplayName { get; }

        void ComposeCoverPage(IContainer container, BrochureRenderContext ctx);

        void ComposeSection(IDocumentContainer container, BrochureSection section, BrochureRenderContext ctx);

        void ComposePersonnel(IDocumentContainer container, BrochureBlock block, BrochureRenderContext ctx);

        void ComposeOverview(
            IDocumentContainer container,
            IReadOnlyList<BrochureOverviewSection> sections,
            IReadOnlyList<int>? pageBreaks,
            BrochureRenderContext ctx);

        void ComposeContact(IDocumentContainer container, BrochureRenderContext ctx);

        void ComposeClientList(IDocumentContainer container, BrochureBlock block, BrochureRenderContext ctx);

        int EstimatePageCount(BrochureContent content);
    }
}
