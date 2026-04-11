#nullable enable
namespace Kor.Operations.PMTools
{
    /// <summary>
    /// A single metric row in the detail sidebar with label, value, and tooltip.
    /// </summary>
    internal sealed class DetailMetric
    {
        public string Label { get; init; } = "";
        public string Value { get; init; } = "";
        public string Tooltip { get; init; } = "";

        public DetailMetric(string label, string value, string tooltip = "")
        {
            Label = label;
            Value = value;
            Tooltip = tooltip;
        }
    }
}
