#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    public enum BrochureBlockType
    {
        Section,
        Personnel
    }

    public class BrochureBlock
    {
        public BrochureBlockType BlockType { get; set; }

        public BrochureSection? Section { get; set; }

        public List<BrochurePerson> People { get; set; } = new();
    }
}
