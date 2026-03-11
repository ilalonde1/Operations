using System;
using System.Windows.Markup;

namespace Kor.Operations.Financials
{
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class FinancialMetricTooltipExtension : MarkupExtension
    {
        public string Key { get; set; } = "";

        public override object? ProvideValue(IServiceProvider serviceProvider)
            => FinancialMetricDefinitions.TryGetTooltipText(Key);
    }
}

