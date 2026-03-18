#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    /// <summary>
    /// Represents a section of brochure projects.
    /// </summary>
    public sealed class BrochureSection
    {
        /// <summary>
        /// Gets or sets the section heading.
        /// </summary>
        public string Heading { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the section blurb.
        /// </summary>
        public string Blurb { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the projects in the section.
        /// </summary>
        public List<BrochureProject> Projects { get; set; } = new();

        /// <summary>
        /// Gets or sets the zero-based indices of projects after which a page break should be inserted when rendering.
        /// </summary>
        public List<int> PageBreakAfterProjectIndex { get; set; } = new();
    }
}
