#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Kor.Operations.Rendering.Brochure;

internal sealed class BrochureSkin
{
    public required string Name { get; init; }

    public required string HeaderText { get; init; }

    public required string PrimaryColor { get; init; }

    public required string AccentColor { get; init; }
}

/// <summary>
/// Provides the available brochure skins and their visual settings.
/// </summary>
public static class BrochureSkinCatalog
{
    private static readonly IReadOnlyDictionary<string, BrochureSkin> Skins =
        new ReadOnlyDictionary<string, BrochureSkin>(
            new Dictionary<string, BrochureSkin>(StringComparer.OrdinalIgnoreCase)
            {
                ["Corporate Profile"] = new BrochureSkin
                {
                    Name = "Corporate Profile",
                    HeaderText = "KOR Structural Portfolio",
                    PrimaryColor = "#435363",
                    AccentColor = "#FF5C36"
                },
                ["Project Showcase"] = new BrochureSkin
                {
                    Name = "Project Showcase",
                    HeaderText = "KOR Structural Project Showcase",
                    PrimaryColor = "#304D6D",
                    AccentColor = "#FF7A3D"
                },
                ["Regional Overview"] = new BrochureSkin
                {
                    Name = "Regional Overview",
                    HeaderText = "KOR Structural Regional Overview",
                    PrimaryColor = "#4A5A47",
                    AccentColor = "#D68C45"
                },
                ["Executive Minimal"] = new BrochureSkin
                {
                    Name = "Executive Minimal",
                    HeaderText = "KOR Structural Executive Minimal",
                    PrimaryColor = "#1F2A36",
                    AccentColor = "#A68A64"
                },
                ["Bold Portfolio"] = new BrochureSkin
                {
                    Name = "Bold Portfolio",
                    HeaderText = "KOR Structural Bold Portfolio",
                    PrimaryColor = "#123B5D",
                    AccentColor = "#FF6B2C"
                }
            });

    /// <summary>
    /// Gets the available brochure skin names for the UI.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = new ReadOnlyCollection<string>(
        new[]
        {
            "Corporate Profile",
            "Project Showcase",
            "Regional Overview",
            "Executive Minimal",
            "Bold Portfolio"
        });

    internal static BrochureSkin GetSkin(string? skinName) =>
        !string.IsNullOrWhiteSpace(skinName) && Skins.TryGetValue(skinName, out var skin)
            ? skin
            : Skins["Corporate Profile"];
}
