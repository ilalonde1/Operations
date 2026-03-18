#nullable enable
namespace Kor.Operations.Core.Models.Brochure
{
    /// <summary>
    /// Represents a person featured in a brochure.
    /// </summary>
    public sealed class BrochurePerson
    {
        /// <summary>
        /// Gets or sets the person's name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the person's credentials.
        /// </summary>
        public string Credentials { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the person's biography text.
        /// </summary>
        public string Bio { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the path to the person's photo.
        /// </summary>
        public string PhotoPath { get; set; } = string.Empty;
    }
}
