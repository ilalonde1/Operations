#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    public sealed class BrochureContent
    {
        public string TemplateName { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        public string LogoPath { get; set; } = string.Empty;

        public List<BrochureProject> Projects { get; set; } = new();
    }
}
