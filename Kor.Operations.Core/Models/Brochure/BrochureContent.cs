#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    public sealed class BrochureContent
    {
        public string TemplateName { get; set; } = string.Empty;

        public string CoverTitle { get; set; } = string.Empty;

        public string CoverPhotoPath { get; set; } = string.Empty;

        public float CoverPhotoOpacity { get; set; } = 0.85f;

        public string CompanyName { get; set; } = string.Empty;

        public string LogoPath { get; set; } = string.Empty;

        public List<BrochureBlock> Blocks { get; set; } = new();
    }
}
