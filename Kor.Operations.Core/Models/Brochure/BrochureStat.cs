#nullable enable
namespace Kor.Operations.Core.Models.Brochure
{
    /// <summary>
    /// Represents a labeled brochure statistic.
    /// </summary>
    public sealed class BrochureStat
    {
        /// <summary>
        /// Gets or sets the statistic label.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the statistic value.
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }
}
