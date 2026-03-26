#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Proposal
{
    public sealed class FeeProposal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        public List<FeeProposalBlock> Blocks { get; set; } = new();
    }
}
