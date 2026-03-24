#nullable enable
#pragma warning disable SA1649
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
        /// A client name listing block.
        /// </summary>
        ClientList,

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
        public ObservableCollection<BrochurePerson> People { get; set; } = new();

        /// <summary>
        /// Gets or sets the heading shown at the top of the personnel page.
        /// </summary>
        public string PersonnelHeading { get; set; } = "People";

        /// <summary>
        /// Gets or sets the intro text shown below the personnel heading.
        /// </summary>
        public string PersonnelBlurb { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the overview sections shown in the block.
        /// </summary>
        public ObservableCollection<BrochureOverviewSection> OverviewSections { get; set; } = new();

        /// <summary>
        /// Gets or sets the zero-based indices of overview sections after which a page break should be inserted when rendering.
        /// </summary>
        public List<int> PageBreakAfterOverviewIndex { get; set; } = new();

        /// <summary>
        /// Gets or sets the heading shown at the top of the client list page.
        /// </summary>
        public string ClientListHeading { get; set; } = "Our Clients";

        /// <summary>
        /// Gets or sets the introductory paragraph shown above the client names.
        /// </summary>
        public string ClientListPreamble { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the client names to display.
        /// </summary>
        public ObservableCollection<string> ClientNames { get; set; } = new();

        /// <summary>
        /// Gets or sets the optional note shown below the client names.
        /// </summary>
        public string ClientListNote { get; set; } = string.Empty;
    }
}
