#nullable enable
using System;
using System.Text.Json.Serialization;

namespace Kor.Operations.Core.Models.Proposal
{
    public sealed class FeeProposalBlock
    {
        public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
        public string? TemplateId { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public ProposalBlockType BlockType { get; set; }

        public CoverBlockContent? Cover { get; set; }
        public IntroductionBlockContent? Introduction { get; set; }
        public CompanyBlockContent? Company { get; set; }
        public PersonnelBlockContent? Personnel { get; set; }
        public ReferencesBlockContent? References { get; set; }
        public ProjectDescriptionBlockContent? ProjectDescription { get; set; }
        public FeeTableBlockContent? FeeTable { get; set; }
        public ScopeBlockContent? Scope { get; set; }
        public ExcludedServicesBlockContent? ExcludedServices { get; set; }
        public ApprovalToProceedBlockContent? ApprovalToProceed { get; set; }
        public SignaturePageBlockContent? SignaturePage { get; set; }
        public RatesTableBlockContent? RatesTable { get; set; }
        public FreeTextBlockContent? FreeText { get; set; }
    }
}
