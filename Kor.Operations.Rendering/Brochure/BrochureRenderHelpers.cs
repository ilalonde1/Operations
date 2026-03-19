#nullable enable
using System;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kor.Operations.Rendering.Brochure
{
    internal static class BrochureRenderHelpers
    {
        internal static void ConfigureStandardPage(PageDescriptor page)
        {
            page.Size(PageSizes.Letter);
            page.PageColor(Colors.White);
            page.MarginLeft(1f, Unit.Inch);
            page.MarginRight(1f, Unit.Inch);
            page.MarginTop(0);
            page.MarginBottom(0.2f, Unit.Inch);
            page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish"));
        }

        internal static TextStyle GetBodyTextStyle(float fontSize, string fontColor) =>
            TextStyle.Default
                .FontFamily("Mulish")
                .FontSize(fontSize)
                .FontColor(fontColor);

        internal static string BuildOverlayColor(string baseColor, float clampedOpacity)
        {
            var alpha = (int)((1.0f - clampedOpacity) * 255);
            var rgb = baseColor.StartsWith("#", StringComparison.Ordinal)
                ? baseColor[1..]
                : baseColor;

            return $"#{alpha:X2}{rgb}";
        }
    }
}
