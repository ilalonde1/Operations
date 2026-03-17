#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    public class BrochureSection
    {
        public string Heading { get; set; } = string.Empty;

        public string Blurb { get; set; } = string.Empty;

        public List<BrochureProject> Projects { get; set; } = new();
    }
}
