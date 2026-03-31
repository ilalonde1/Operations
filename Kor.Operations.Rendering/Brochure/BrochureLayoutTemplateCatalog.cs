#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Kor.Operations.Rendering.Brochure.Layouts;

namespace Kor.Operations.Rendering.Brochure
{
    public sealed class BrochureLayoutTemplateCatalog
    {
        private readonly IReadOnlyDictionary<string, IBrochureLayoutTemplate> _templates;

        static BrochureLayoutTemplateCatalog()
        {
            Default = new BrochureLayoutTemplateCatalog(
                new Dictionary<string, IBrochureLayoutTemplate>(StringComparer.OrdinalIgnoreCase)
                {
                    ["standard-portfolio"] = new StandardPortfolioLayout(),
                    ["project-showcase"] = new ProjectShowcaseLayout(),
                    ["executive-summary"] = new ExecutiveSummaryLayout(),
                    ["islam-march-2026"] = new IslamMarch2026Layout()
                });
        }

        private BrochureLayoutTemplateCatalog(IReadOnlyDictionary<string, IBrochureLayoutTemplate> templates)
        {
            _templates = templates;
            All = new ReadOnlyCollection<IBrochureLayoutTemplate>(
                new[]
                {
                    _templates["standard-portfolio"],
                    _templates["project-showcase"],
                    _templates["executive-summary"],
                    _templates["islam-march-2026"]
                });
        }

        public static readonly BrochureLayoutTemplateCatalog Default;

        public IReadOnlyList<IBrochureLayoutTemplate> All { get; }

        public IBrochureLayoutTemplate Resolve(string? layoutTemplateId)
        {
            if (!string.IsNullOrWhiteSpace(layoutTemplateId) &&
                _templates.TryGetValue(layoutTemplateId, out var layout))
            {
                return layout;
            }

            return _templates["standard-portfolio"];
        }
    }
}
