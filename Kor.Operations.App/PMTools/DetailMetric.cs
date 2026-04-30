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
        public bool IsHeader { get; init; }
        public bool IsExplanation { get; init; }
        public string AccentColor { get; init; } = "";

        public DetailMetric(string label, string value, string tooltip = "")
        {
            Label = label;
            Value = value;
            Tooltip = tooltip;
        }

        public static DetailMetric Header(string label, string accentColor = "#3F5364")
            => new(label, "", "") { IsHeader = true, AccentColor = accentColor };

        public static DetailMetric Explanation(string text, string bgColor = "#F8FAFC")
            => new(text, "", "") { IsExplanation = true, AccentColor = bgColor };
    }
}
