#nullable enable
using System;

namespace Kor.Operations.Core.Models.Proposal
{
    public sealed class ProposalStaffMember
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FullName { get; set; } = string.Empty;
        public string Credentials { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public string SignatureImagePath { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
    }
}
