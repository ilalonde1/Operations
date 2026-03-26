#nullable enable
using System;

namespace Kor.Operations.Core.Models.Brochure
{
    /// <summary>
    /// Represents a saved brochure proposal.
    /// </summary>
    public sealed class BrochureProposal
    {
        /// <summary>
        /// Gets or sets the proposal identifier.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the proposal name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the UTC time when the proposal was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the UTC time when the proposal was last modified.
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the content of the proposal.
        /// </summary>
        public BrochureContent Content { get; set; } = new();
    }
}
