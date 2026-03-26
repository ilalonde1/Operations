#nullable enable

namespace Kor.Operations.Rendering.Brochure.Skins
{
    public sealed class BrochureSkinDefinition
    {
        public required string Id { get; init; }

        public required string DisplayName { get; init; }

        public string PrimaryColor { get; init; } = "#435363";

        public string AccentColor { get; init; } = "#FF5C36";

        public required string HeaderText { get; init; }

        public string FontFamily { get; init; } = "Mulish";

        public string TitleFontFamily { get; init; } = "Mulish Black";

        public float CoverTitleFontSize { get; init; } = 32f;

        public string CoverTreatment { get; init; } = "overlay";

        public string DividerStyle { get; init; } = "line";

        public float DividerThickness { get; init; } = 1.5f;

        public float SectionSpacingPoints { get; init; } = 6f;
    }
}
