#nullable enable
using System;

namespace Kor.Operations.Core.Models.Brochure
{
    public sealed class BrochureProposal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        public BrochureContent Content { get; set; } = new();
    }
}
