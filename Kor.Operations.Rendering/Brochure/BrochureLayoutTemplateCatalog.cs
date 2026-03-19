#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Kor.Operations.Rendering.Brochure.Layouts;

namespace Kor.Operations.Rendering.Brochure
{
    internal sealed class BrochureLayoutTemplateCatalog
    {
        private readonly IReadOnlyDictionary<string, IBrochureLayoutTemplate> _templates;

        static BrochureLayoutTemplateCatalog()
        {
            Default = new BrochureLayoutTemplateCatalog(
                new Dictionary<string, IBrochureLayoutTemplate>(StringComparer.OrdinalIgnoreCase)
                {
                    ["standard-portfolio"] = new StandardPortfolioLayout()
                });
        }

        private BrochureLayoutTemplateCatalog(IReadOnlyDictionary<string, IBrochureLayoutTemplate> templates)
        {
            _templates = templates;
            All = new ReadOnlyCollection<IBrochureLayoutTemplate>(
                new[]
                {
                    _templates["standard-portfolio"]
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
