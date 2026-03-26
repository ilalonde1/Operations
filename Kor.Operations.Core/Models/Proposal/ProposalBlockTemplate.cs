#nullable enable
using System;

namespace Kor.Operations.Core.Models.Proposal
{
    public sealed class ProposalBlockTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public ProposalBlockType BlockType { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        public FeeProposalBlock Content { get; set; } = new();
    }
}
