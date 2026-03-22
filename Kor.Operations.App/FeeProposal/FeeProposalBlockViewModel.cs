#nullable enable
using System;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal
{
    internal sealed class FeeProposalBlockViewModel : ObservableObject
    {
        public FeeProposalBlock Block { get; }

        public string TemplateName
        {
            get => Block.TemplateName;
            set
            {
                Block.TemplateName = value;
                OnPropertyChanged();
            }
        }

        public ProposalBlockType BlockType => Block.BlockType;

        public string Summary
        {
            get
            {
                return Block.BlockType switch
                {
                    ProposalBlockType.Cover => BuildCoverSummary(),
                    ProposalBlockType.Introduction => string.IsNullOrWhiteSpace(Block.Introduction?.SalutationName)
                        ? "No salutation set"
                        : $"Dear {Block.Introduction.SalutationName}",
                    ProposalBlockType.Company => $"{Block.Company?.Sections?.Count ?? 0} section(s)",
                    ProposalBlockType.Personnel => BuildPersonnelSummary(),
                    ProposalBlockType.References => $"{Block.References?.ClientNames?.Count ?? 0} client references",
                    ProposalBlockType.ProjectDescription => $"{Block.ProjectDescription?.AssumptionBullets?.Count ?? 0} assumptions",
                    ProposalBlockType.FeeTable => BuildFeeTableSummary(),
                    ProposalBlockType.Scope => BuildScopeSummary(),
                    ProposalBlockType.ExcludedServices => $"{Block.ExcludedServices?.ExcludedItems?.Count ?? 0} excluded items",
                    ProposalBlockType.ApprovalToProceed => "Approval to proceed + invoicing terms",
                    ProposalBlockType.SignaturePage => $"{Block.SignaturePage?.SignatoryStaffIds?.Count ?? 0} signatories",
                    ProposalBlockType.RatesTable => BuildRatesSummary(),
                    ProposalBlockType.FreeText => string.IsNullOrWhiteSpace(Block.FreeText?.Heading)
                        ? "No heading"
                        : Block.FreeText.Heading,
                    ProposalBlockType.PageBreak => " page break ",
                    _ => string.Empty,
                };
            }
        }

        public FeeProposalBlockViewModel(FeeProposalBlock block)
        {
            Block = block;
        }

        private string BuildCoverSummary()
        {
            var project = string.IsNullOrWhiteSpace(Block.Cover?.ProjectName) ? "" : Block.Cover.ProjectName;
            var client = string.IsNullOrWhiteSpace(Block.Cover?.ClientCompany) ? "" : Block.Cover.ClientCompany;
            return $"Project: {project} | Client: {client}";
        }

        private string BuildPersonnelSummary()
        {
            var additional = Block.Personnel?.AdditionalStaffIds?.Count ?? 0;
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(Block.Personnel?.LeadStaffId)) parts.Add("Lead assigned");
            if (!string.IsNullOrWhiteSpace(Block.Personnel?.SupportingStaffId)) parts.Add("Supporting assigned");
            if (additional > 0) parts.Add($"{additional} additional");
            return parts.Count > 0 ? string.Join(" | ", parts) : "No staff assigned";
        }

        private string BuildFeeTableSummary()
        {
            var phases = Block.FeeTable?.Phases?.Count ?? 0;
            var total = 0m;
            if (Block.FeeTable?.Phases != null)
                foreach (var p in Block.FeeTable.Phases) total += p.Fee;
            return total > 0
                ? $"{phases} phases  Total: {total:C0}"
                : $"{phases} phases  fees not yet entered";
        }

        private string BuildScopeSummary()
        {
            var jurisdiction = string.IsNullOrWhiteSpace(Block.Scope?.Jurisdiction) ? "" : Block.Scope.Jurisdiction;
            var count = Block.Scope?.IncludedServices?.Count ?? 0;
            return $"{jurisdiction} | {count} service(s)";
        }

        private string BuildRatesSummary()
        {
            var date = string.IsNullOrWhiteSpace(Block.RatesTable?.EffectiveDate) ? "" : Block.RatesTable.EffectiveDate;
            var count = Block.RatesTable?.Rates?.Count ?? 0;
            return $"Effective: {date} | {count} roles";
        }
    }
}
