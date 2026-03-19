#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    /// <summary>
    /// Represents the full content of a brochure proposal.
    /// </summary>
    public sealed class BrochureContent
    {
        /// <summary>
        /// Gets or sets the selected template name.
        /// </summary>
        public string TemplateName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the selected skin identifier.
        /// </summary>
        public string? SkinId { get; set; }

        /// <summary>
        /// Gets or sets the selected layout template identifier.
        /// </summary>
        public string? LayoutTemplateId { get; set; }

        /// <summary>
        /// Gets or sets the brochure cover title.
        /// </summary>
        public string CoverTitle { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the cover photo path.
        /// </summary>
        public string CoverPhotoPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the cover photo opacity.
        /// </summary>
        public float CoverPhotoOpacity { get; set; } = 0.85f;

        /// <summary>
        /// Gets or sets the year shown on the cover.
        /// </summary>
        public int? CoverYear { get; set; } = null;

        /// <summary>
        /// Gets or sets the company name shown in the brochure.
        /// </summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the logo file path.
        /// </summary>
        public string LogoPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ordered brochure blocks.
        /// </summary>
        public List<BrochureBlock> Blocks { get; set; } = new();
    }
}
