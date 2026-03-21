#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.Converters
{
    [ValueConversion(typeof(ProposalBlockType), typeof(string))]
    public sealed class ProposalBlockTypeDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && System.Enum.TryParse<ProposalBlockType>(s, out var parsed))
                return Convert(parsed, targetType, parameter, culture);

            if (value is ProposalBlockType type)
            {
                return type switch
                {
                    ProposalBlockType.Cover => "Cover",
                    ProposalBlockType.Introduction => "Introduction",
                    ProposalBlockType.Company => "Company",
                    ProposalBlockType.Personnel => "Personnel",
                    ProposalBlockType.Scope => "Scope",
                    ProposalBlockType.FeeTable => "Fee Table",
                    ProposalBlockType.ExcludedServices => "Excluded Services",
                    ProposalBlockType.References => "References",
                    ProposalBlockType.ProjectDescription => "Project Description",
                    ProposalBlockType.ApprovalToProceed => "Approval to Proceed",
                    ProposalBlockType.SignaturePage => "Signature Page",
                    ProposalBlockType.RatesTable => "Rates Table",
                    ProposalBlockType.FreeText => "Free Text",
                    ProposalBlockType.PageBreak => "Page Break",
                    _ => value.ToString() ?? string.Empty
                };
            }

            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
