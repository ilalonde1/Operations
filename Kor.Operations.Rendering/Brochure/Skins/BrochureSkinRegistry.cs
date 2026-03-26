#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Kor.Operations.Rendering.Brochure.Skins
{
    public sealed class BrochureSkinRegistry
    {
        private static readonly IReadOnlyDictionary<string, BrochureSkinDefinition> BuiltInSkins =
            new ReadOnlyDictionary<string, BrochureSkinDefinition>(
                new Dictionary<string, BrochureSkinDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["corporate-profile"] = new BrochureSkinDefinition
                    {
                        Id = "corporate-profile",
                        DisplayName = "Corporate Profile",
                        PrimaryColor = "#435363",
                        AccentColor = "#FF5C36",
                        HeaderText = "KOR Structural Portfolio"
                    },
                    ["project-showcase"] = new BrochureSkinDefinition
                    {
                        Id = "project-showcase",
                        DisplayName = "Project Showcase",
                        PrimaryColor = "#304D6D",
                        AccentColor = "#FF7A3D",
                        HeaderText = "KOR Structural Project Showcase"
                    },
                    ["regional-overview"] = new BrochureSkinDefinition
                    {
                        Id = "regional-overview",
                        DisplayName = "Regional Overview",
                        PrimaryColor = "#4A5A47",
                        AccentColor = "#D68C45",
                        HeaderText = "KOR Structural Regional Overview"
                    },
                    ["executive-minimal"] = new BrochureSkinDefinition
                    {
                        Id = "executive-minimal",
                        DisplayName = "Executive Minimal",
                        PrimaryColor = "#1F2A36",
                        AccentColor = "#A68A64",
                        HeaderText = "KOR Structural Executive Minimal"
                    },
                    ["bold-portfolio"] = new BrochureSkinDefinition
                    {
                        Id = "bold-portfolio",
                        DisplayName = "Bold Portfolio",
                        PrimaryColor = "#123B5D",
                        AccentColor = "#FF6B2C",
                        HeaderText = "KOR Structural Bold Portfolio"
                    }
                });

        private static readonly IReadOnlyDictionary<string, string> LegacyTemplateNameMap =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Corporate Profile"] = "corporate-profile",
                    ["Project Showcase"] = "project-showcase",
                    ["Regional Overview"] = "regional-overview",
                    ["Executive Minimal"] = "executive-minimal",
                    ["Bold Portfolio"] = "bold-portfolio"
                });

        public static IReadOnlyList<BrochureSkinDefinition> All { get; } = new ReadOnlyCollection<BrochureSkinDefinition>(
            new[]
            {
                BuiltInSkins["corporate-profile"],
                BuiltInSkins["project-showcase"],
                BuiltInSkins["regional-overview"],
                BuiltInSkins["executive-minimal"],
                BuiltInSkins["bold-portfolio"]
            });

        public static BrochureSkinDefinition Resolve(string? skinId, string? legacyTemplateName)
        {
            if (!string.IsNullOrWhiteSpace(skinId) && BuiltInSkins.TryGetValue(skinId, out var skin))
                return skin;

            if (!string.IsNullOrWhiteSpace(legacyTemplateName) &&
                LegacyTemplateNameMap.TryGetValue(legacyTemplateName, out var legacySkinId) &&
                BuiltInSkins.TryGetValue(legacySkinId, out skin))
            {
                return skin;
            }

            return BuiltInSkins["corporate-profile"];
        }
    }
}
