#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    public sealed class BrochureProject
    {
        public string SectionLabel { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;

        public string ProjectDescription { get; set; } = string.Empty;

        public string Client { get; set; } = string.Empty;

        public string Architect { get; set; } = string.Empty;

        public List<BrochurePhoto> Photos { get; set; } = new();
    }
}
