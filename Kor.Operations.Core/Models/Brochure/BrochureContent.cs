#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    public sealed class BrochureContent
    {
        public string TemplateName { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;

        public string LogoPath { get; set; } = string.Empty;

        public List<BrochurePhoto> Photos { get; set; } = new();

        public string ProjectDescription { get; set; } = string.Empty;

        public List<BrochureStat> Stats { get; set; } = new();

        public string Notes { get; set; } = string.Empty;
    }
}
