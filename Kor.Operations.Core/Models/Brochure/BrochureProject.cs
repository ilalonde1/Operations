#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    public class BrochureProject
    {
        public string SectionLabel { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;

        public string ProjectLocation { get; set; } = string.Empty;

        public string ProjectDescription { get; set; } = string.Empty;

        public List<BrochurePhoto> Photos { get; set; } = new();

        public List<BrochureStat> Stats { get; set; } = new();

        public string Notes { get; set; } = string.Empty;
    }
}
