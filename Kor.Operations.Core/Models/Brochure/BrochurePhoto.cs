#nullable enable
using System;
namespace Kor.Operations.Core.Models.Brochure
{
    /// <summary>
    /// Represents a brochure photo and its caption.
    /// </summary>
    public sealed class BrochurePhoto
    {
        /// <summary>
        /// Gets or sets the photo file path.
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the embedded photo bytes.
        /// </summary>
        public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Gets or sets the photo caption.
        /// </summary>
        public string Caption { get; set; } = string.Empty;
    }
}
