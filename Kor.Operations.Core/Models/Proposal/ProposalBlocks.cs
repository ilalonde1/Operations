#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

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
            var project = string.IsNullOrWhiteSpace(ProjectName) ? string.Empty : ProjectName;
            var client = string.IsNullOrWhiteSpace(ClientCompany) ? string.Empty : ClientCompany;
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
        public ObservableCollection<CompanySection> Sections { get; set; } = new();

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
        public ObservableCollection<string> AdditionalStaffIds { get; set; } = new();

        public string GetSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(LeadStaffId))
            {
                parts.Add("Lead assigned");
            }

            if (!string.IsNullOrWhiteSpace(SupportingStaffId))
            {
                parts.Add("Supporting assigned");
            }

            var additional = AdditionalStaffIds?.Count ?? 0;
            if (additional > 0)
            {
                parts.Add($"{additional} additional");
            }
            return parts.Count > 0 ? string.Join(" | ", parts) : "No staff assigned";
        }
    }

    public sealed class ReferencesBlockContent : ISummarizable
    {
        public string Heading { get; set; } = "References";
        public string Preamble { get; set; } = "Many of our best clients include Vancouver's most successful Developers. We have recently worked on and completed a wide variety of projects for the following:";
        public ObservableCollection<string> ClientNames { get; set; } = new();
        public string ProjectSpecificNote { get; set; } = string.Empty;

        public string GetSummary() => $"{ClientNames?.Count ?? 0} client references";
    }

    public sealed class ProjectDescriptionBlockContent : ISummarizable
    {
        public string Heading { get; set; } = "Project Description";
        public string Preamble { get; set; } = "Our proposed fees are based on the following assumptions:";
        public ObservableCollection<string> AssumptionBullets { get; set; } = new();

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
        public ObservableCollection<FeePhaseRow> Phases { get; set; } = new()
        {
            new FeePhaseRow { PhaseName = "Schematic Design to DP Application" },
            new FeePhaseRow { PhaseName = "Design Development to BP Application" },
            new FeePhaseRow { PhaseName = "Working Drawings Issued for Construction" },
            new FeePhaseRow { PhaseName = "Construction Administration & Field Reviews" },
        };
        public int FieldReviewVisits { get; set; }
        public decimal DisbursementsAllowance { get; set; } = 1500m;
        public ObservableCollection<string> AdditionalNotes { get; set; } = new();

        public string GetSummary()
        {
            var phases = Phases?.Count ?? 0;
            var total = 0m;
            if (Phases != null)
            {
                foreach (var p in Phases)
                {
                    total += p.Fee;
                }
            }
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
        public ObservableCollection<ScopeItem> IncludedServices { get; set; } = new();

        public string GetSummary()
        {
            var jurisdiction = string.IsNullOrWhiteSpace(Jurisdiction) ? string.Empty : Jurisdiction;
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
        public ObservableCollection<string> ExcludedItems { get; set; } = new();

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
        public ObservableCollection<string> SignatoryStaffIds { get; set; } = new();
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
        public ObservableCollection<HourlyRateRow> Rates { get; set; } = new()
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
            var date = string.IsNullOrWhiteSpace(EffectiveDate) ? string.Empty : EffectiveDate;
            var count = Rates?.Count ?? 0;
            return $"Effective: {date} | {count} roles";
        }
    }

    public sealed class RfpRateScheduleRow : ObservableObject
    {
        private string _name = string.Empty;
        private string _position = string.Empty;
        private bool _scored;
        private decimal? _baseHourly;
        private decimal? _quotedHourly;

        public string Name { get => _name; set => SetField(ref _name, value); }

        public string Position { get => _position; set => SetField(ref _position, value); }

        public bool Scored { get => _scored; set => SetField(ref _scored, value); }

        public decimal? BaseHourly { get => _baseHourly; set => SetField(ref _baseHourly, value); }

        /// <summary>Quoted hourly means including overhead, before profit (the RFP's incl. OH column).</summary>
        public decimal? QuotedHourly { get => _quotedHourly; set => SetField(ref _quotedHourly, value); }
    }

    public sealed record RfpRateCalculation(string Name, string Position, bool Scored, decimal? BaseHourly,
        decimal? ExcludingOverhead, decimal? IncludingOverhead, decimal? IncludingOverheadAndProfit,
        decimal? ImpliedOverhead, IReadOnlyList<string> Flags);

    public sealed class RfpRateScheduleBlockContent : ObservableObject, ISummarizable
    {
        private readonly HashSet<RfpRateScheduleRow> _observed = new();
        private decimal _burden;
        private decimal _overhead;
        private decimal _profit;
        private decimal _statutoryLoad;
        private ObservableCollection<RfpRateScheduleRow> _rows = new();

        public RfpRateScheduleBlockContent()
        {
            _rows.CollectionChanged += RowsChanged;
        }

        public decimal Burden
        {
            get => _burden;
            set
            {
                if (SetField(ref _burden, value))
                {
                    Recalculate();
                }
            }
        }

        public decimal Overhead
        {
            get => _overhead;
            set
            {
                if (SetField(ref _overhead, value))
                {
                    Recalculate();
                }
            }
        }

        public decimal Profit
        {
            get => _profit;
            set
            {
                if (SetField(ref _profit, value))
                {
                    Recalculate();
                }
            }
        }

        public decimal StatutoryLoad
        {
            get => _statutoryLoad;
            set
            {
                if (SetField(ref _statutoryLoad, value))
                {
                    Recalculate();
                }
            }
        }

        [JsonIgnore]
        public decimal BurdenPercent { get => Burden * 100; set => Burden = value / 100; }

        [JsonIgnore]
        public decimal OverheadPercent { get => Overhead * 100; set => Overhead = value / 100; }

        [JsonIgnore]
        public decimal ProfitPercent { get => Profit * 100; set => Profit = value / 100; }

        [JsonIgnore]
        public decimal StatutoryLoadPercent { get => StatutoryLoad * 100; set => StatutoryLoad = value / 100; }

        public ObservableCollection<RfpRateScheduleRow> Rows
        {
            get => _rows;
            set
            {
                _rows.CollectionChanged -= RowsChanged;
                _rows = value ?? new();
                _rows.CollectionChanged += RowsChanged;
                ObserveRows();
                OnPropertyChanged();
                Recalculate();
            }
        }

        [JsonIgnore]
        public IReadOnlyList<RfpRateCalculation> Calculations => Rows.Select(Calculate).ToArray();

        [JsonIgnore]
        public IReadOnlyList<string> Flags =>
            (Burden < StatutoryLoad ? new[] { $"Burden {Burden:P2} is below the stated statutory load {StatutoryLoad:P2}." } : Array.Empty<string>())
            .Concat(Calculations.SelectMany(r => r.Flags)).ToArray();

        public RfpRateCalculation Calculate(RfpRateScheduleRow row)
        {
            var excl = row.BaseHourly * (1 + Burden);
            var incl = excl * (1 + Overhead);
            var profit = incl * (1 + Profit);
            var implied = excl is > 0 && row.QuotedHourly.HasValue ? (row.QuotedHourly / excl) - 1 : null;
            var flags = new List<string>();
            if (row.Scored && row.BaseHourly is not > 0)
            {
                flags.Add($"{row.Name} / {row.Position}: scored position has no rate.");
            }

            if (implied is { } oh && Math.Abs(oh - Overhead) > 0.005m)
            {
                flags.Add($"{row.Name}: quoted hourly implies {oh:P2} overhead; stated {Overhead:P2} (more than 0.5 percentage points apart).");
            }

            return new(row.Name, row.Position, row.Scored, row.BaseHourly, excl, incl, profit, implied, flags);
        }

        public string GetSummary() => $"{Rows.Count} people · burden {Burden:P1} · overhead {Overhead:P1} · profit {Profit:P1}";

        private void RowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ObserveRows();
            Recalculate();
        }

        private void ObserveRows()
        {
            foreach (var row in _observed)
            {
                row.PropertyChanged -= RowChanged;
            }

            _observed.Clear();
            foreach (var row in Rows)
            {
                if (_observed.Add(row))
                {
                    row.PropertyChanged += RowChanged;
                }
            }
        }

        private void RowChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

        private void Recalculate()
        {
            OnPropertyChanged(nameof(Calculations));
            OnPropertyChanged(nameof(Flags));
            OnPropertyChanged(nameof(BurdenPercent));
            OnPropertyChanged(nameof(OverheadPercent));
            OnPropertyChanged(nameof(ProfitPercent));
            OnPropertyChanged(nameof(StatutoryLoadPercent));
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
