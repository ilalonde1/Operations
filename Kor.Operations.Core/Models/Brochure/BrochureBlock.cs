#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    public enum BrochureBlockType
    {
        Section,
        Personnel,
        CompanyOverview,
        Contact,
        PageBreak
    }

    public sealed class BrochureOverviewSection
    {
        public string Heading { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;
    }

    public sealed class BrochureBlock
    {
        public BrochureBlockType BlockType { get; set; }

        public BrochureSection? Section { get; set; }

        public List<BrochurePerson> People { get; set; } = new();

        public List<BrochureOverviewSection> OverviewSections { get; set; } = new();
    }
}
