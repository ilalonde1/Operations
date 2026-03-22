#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Proposal
{
    public sealed class CoverBlockContent : ISummarizable
    {
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectAddress { get; set; } = string.Empty;
        public string ClientCompany { get; set; } = string.Empty;
        public string ClientAddress { get; set; } = string.Empty;
        public string AttentionName { get; set; } = string.Empty;
        public string AttentionTitle { get; set; } = string.Empty;
        public string AttentionEmail { get; set; } = string.Empty;
        public string CcName { get; set; } = string.Empty;
        public string CcTitle { get; set; } = string.Empty;
        public string CcEmail { get; set; } = string.Empty;
        public string PreparerStaffId { get; set; } = string.Empty;
        public string ProposalDate { get; set; } = string.Empty;
        public string Jurisdiction { get; set; } = string.Empty;

        public string GetSummary()
        {
            var project = string.IsNullOrWhiteSpace(ProjectName) ? "" : ProjectName;
            var client = string.IsNullOrWhiteSpace(ClientCompany) ? "" : ClientCompany;
            return $"Project: {project} | Client: {client}";
        }
    }

    public sealed class IntroductionBlockContent : ISummarizable
    {
        public string SalutationName { get; set; } = string.Empty;
        public string ProjectAddress { get; set; } = string.Empty;
        public string DrawingReference { get; set; } = string.Empty;
        public string CloserText { get; set; } = "Thank you for the opportunity to present you with this proposal and we look forward to hearing from you. Please do not hesitate to contact us with any questions.";
        public string SignatoryStaffId { get; set; } = string.Empty;

        public string GetSummary() => string.IsNullOrWhiteSpace(SalutationName)
            ? "Salutation not set"
            : $"Dear {SalutationName}";
    }

    public sealed class CompanyBlockContent : ISummarizable
    {
        public string Heading { get; set; } = "Our Company";
        public List<CompanySection> Sections { get; set; } = new();

        public string GetSummary() => $"{Sections?.Count ?? 0} section(s)";
    }

    public sealed class CompanySection
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }

    public sealed class PersonnelBlockContent : ISummarizable
    {
        public string LeadStaffId { get; set; } = string.Empty;
        public string LeadBioOverride { get; set; } = string.Empty;
        public string SupportingStaffId { get; set; } = string.Empty;
        public string SupportingBioOverride { get; set; } = string.Empty;
        public string CollaborationNote { get; set; } = string.Empty;
        public List<string> AdditionalStaffIds { get; set; } = new();

        public string GetSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(LeadStaffId)) parts.Add("Lead assigned");
            if (!string.IsNullOrWhiteSpace(SupportingStaffId)) parts.Add("Supporting assigned");
            var additional = AdditionalStaffIds?.Count ?? 0;
            if (additional > 0) parts.Add($"{additional} additional");
            return parts.Count > 0 ? string.Join(" | ", parts) : "No staff assigned";
        }
    }

    public sealed class ReferencesBlockContent : ISummarizable
    {
        public string Heading { get; set; } = "References";
        public string Preamble { get; set; } = "Many of our best clients include Vancouver's most successful Developers. We have recently worked on and completed a wide variety of projects for the following:";
        public List<string> ClientNames { get; set; } = new();
        public string ProjectSpecificNote { get; set; } = string.Empty;

        public string GetSummary() => $"{ClientNames?.Count ?? 0} client references";
    }

    public sealed class ProjectDescriptionBlockContent : ISummarizable
    {
        public string Heading { get; set; } = "Project Description";
        public string Preamble { get; set; } = "Our proposed fees are based on the following assumptions:";
        public List<string> AssumptionBullets { get; set; } = new();

        public string GetSummary() => $"{AssumptionBullets?.Count ?? 0} assumptions";
    }

    public sealed class FeePhaseRow
    {
        public string PhaseName { get; set; } = string.Empty;
        public decimal Fee { get; set; }
    }

    public sealed class FeeTableBlockContent : ISummarizable
    {
        public string Heading { get; set; } = "Proposed Fees";
        public string Preamble { get; set; } = "The following fees are for complete Structural Engineering services for the project as noted in the following Scope of Structural Services. The proposed fixed fees for our services are as follows:";
        public List<FeePhaseRow> Phases { get; set; } = new()
        {
            new FeePhaseRow { PhaseName = "Schematic Design to DP Application" },
            new FeePhaseRow { PhaseName = "Design Development to BP Application" },
            new FeePhaseRow { PhaseName = "Working Drawings Issued for Construction" },
            new FeePhaseRow { PhaseName = "Construction Administration & Field Reviews" },
        };
        public int FieldReviewVisits { get; set; }
        public decimal DisbursementsAllowance { get; set; } = 1500m;
        public List<string> AdditionalNotes { get; set; } = new();

        public string GetSummary()
        {
            var phases = Phases?.Count ?? 0;
            var total = 0m;
            if (Phases != null)
                foreach (var p in Phases) total += p.Fee;
            return total > 0
                ? $"{phases} phases  Total: {total:C0}"
                : $"{phases} phases  fees not entered";
        }
    }

    public sealed class ScopeBlockContent : ISummarizable
    {
        public string Heading { get; set; } = "Scope of Structural Services";
        public string Narrative { get; set; } = string.Empty;
        public string CadPlatform { get; set; } = "Revit/BIM LOD 300";
        public string Jurisdiction { get; set; } = string.Empty;
        public List<ScopeItem> IncludedServices { get; set; } = new();

        public string GetSummary()
        {
            var jurisdiction = string.IsNullOrWhiteSpace(Jurisdiction) ? "" : Jurisdiction;
            var count = IncludedServices?.Count ?? 0;
            return $"{jurisdiction} | {count} service(s)";
        }
    }

    public sealed class ScopeItem
    {
        public string Text { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public sealed class ExcludedServicesBlockContent : ISummarizable
    {
        public string Heading { get; set; } = "Excluded Services";
        public string Preamble { get; set; } = "Our services would not include any of the following:";
        public List<string> ExcludedItems { get; set; } = new();

        public string GetSummary() => $"{ExcludedItems?.Count ?? 0} excluded items";
    }

    public sealed class ApprovalToProceedBlockContent : ISummarizable
    {
        public string Heading { get; set; } = "Approval to Proceed";
        public string IntroParagraph { get; set; } = "We will proceed with our work upon receipt of a signed copy of this fee proposal. If you have any questions or need additional information, please contact the undersigned.";
        public string InvoicingParagraph { get; set; } = "We propose to invoice on a monthly basis. It is our policy to state that we reserve the right to discontinue our work if the client fails to meet his commitments, and we will not be responsible for any damages arising therefrom.";

        public string GetSummary() => "Approval to proceed + invoicing terms";
    }

    public sealed class SignaturePageBlockContent : ISummarizable
    {
        public string ClosingParagraph { get; set; } = "We trust the foregoing proposal meets your expectations. We wish to thank you for your consideration of Kor Structural for this project, and we look forward to working with you and your team on this project.";
        public List<string> SignatoryStaffIds { get; set; } = new();
        public string ClientCompanyName { get; set; } = string.Empty;
        public bool IncludeRatesAppendix { get; set; } = true;

        public string GetSummary() => $"{SignatoryStaffIds?.Count ?? 0} signator(ies)";
    }

    public sealed class HourlyRateRow
    {
        public string Role { get; set; } = string.Empty;
        public decimal RatePerHour { get; set; }
    }

    public sealed class RatesTableBlockContent : ISummarizable
    {
        public string EffectiveDate { get; set; } = string.Empty;
        public List<HourlyRateRow> Rates { get; set; } = new()
        {
            new HourlyRateRow { Role = "Senior Structural Engineer, Managing Principal", RatePerHour = 275m },
            new HourlyRateRow { Role = "Senior Structural Consultant / Engineer / Designer, Principal", RatePerHour = 250m },
            new HourlyRateRow { Role = "Senior Structural Engineer, Associate Principal", RatePerHour = 225m },
            new HourlyRateRow { Role = "Structural Engineer, P.Eng.", RatePerHour = 200m },
            new HourlyRateRow { Role = "Structural Designer / EIT / Technologist", RatePerHour = 175m },
            new HourlyRateRow { Role = "Structural Construction Field Reviewer", RatePerHour = 175m },
        };

        public string GetSummary()
        {
            var date = string.IsNullOrWhiteSpace(EffectiveDate) ? "" : EffectiveDate;
            var count = Rates?.Count ?? 0;
            return $"Effective: {date} | {count} roles";
        }
    }

    public sealed class FreeTextBlockContent : ISummarizable
    {
        public string Heading { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;

        public string GetSummary() => string.IsNullOrWhiteSpace(Heading) ? "No heading" : Heading;
    }

    public sealed class PageBreakBlockContent : ISummarizable
    {
        public string GetSummary() => " page break ";
    }
}
