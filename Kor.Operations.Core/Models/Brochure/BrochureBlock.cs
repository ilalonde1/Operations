#nullable enable
#pragma warning disable SA1649
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    /// <summary>
    /// Identifies the type of brochure block to render.
    /// </summary>
    public enum BrochureBlockType
    {
        /// <summary>
        /// A project section block.
        /// </summary>
        Section,

        /// <summary>
        /// A personnel block.
        /// </summary>
        Personnel,

        /// <summary>
        /// A company overview block.
        /// </summary>
        CompanyOverview,

        /// <summary>
        /// A contact information block.
        /// </summary>
        Contact,

        /// <summary>
        /// A forced page break block.
        /// </summary>
        PageBreak,
    }

    /// <summary>
    /// Represents a heading/body pair for a brochure overview block.
    /// </summary>
    public sealed class BrochureOverviewSection
    {
        /// <summary>
        /// Gets or sets the overview section heading.
        /// </summary>
        public string Heading { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the overview section body content.
        /// </summary>
        public string Body { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a block of content within a brochure.
    /// </summary>
    public sealed class BrochureBlock
    {
        /// <summary>
        /// Gets or sets the block type.
        /// </summary>
        public BrochureBlockType BlockType { get; set; }

        /// <summary>
        /// Gets or sets the section content when the block is a section.
        /// </summary>
        public BrochureSection? Section { get; set; }

        /// <summary>
        /// Gets or sets the people shown in the block.
        /// </summary>
        public List<BrochurePerson> People { get; set; } = new();

        /// <summary>
        /// Gets or sets the overview sections shown in the block.
        /// </summary>
        public List<BrochureOverviewSection> OverviewSections { get; set; } = new();

        /// <summary>
        /// Gets or sets the zero-based indices of overview sections after which a page break should be inserted when rendering.
        /// </summary>
        public List<int> PageBreakAfterOverviewIndex { get; set; } = new();
    }
}
