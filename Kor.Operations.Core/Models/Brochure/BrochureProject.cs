#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    /// <summary>
    /// Represents a project entry in a brochure section.
    /// </summary>
    public sealed class BrochureProject
    {
        /// <summary>
        /// Gets or sets the section label for the project.
        /// </summary>
        public string SectionLabel { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the project name.
        /// </summary>
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the project description.
        /// </summary>
        public string ProjectDescription { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the client name.
        /// </summary>
        public string Client { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the architect name.
        /// </summary>
        public string Architect { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the project photos.
        /// </summary>
        public List<BrochurePhoto> Photos { get; set; } = new();
    }
}
