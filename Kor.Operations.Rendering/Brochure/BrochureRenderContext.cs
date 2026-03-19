#nullable enable
using System;
using System.Threading;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Rendering.Brochure.Skins;

namespace Kor.Operations.Rendering.Brochure
{
    internal sealed class BrochureRenderContext
    {
        public required BrochureContent Content { get; init; }

        public required BrochureSkinDefinition Skin { get; init; }

        public byte[]? LogoBytes { get; init; }

        public byte[]? CoverLogoBytes { get; init; }

        public byte[]? CoverPhotoBytes { get; init; }

        public required Func<string?, string?, byte[]?> ReadImage { get; init; }

        public required Func<string?, string> ResolvePath { get; init; }

        public CancellationToken CancellationToken { get; init; }
    }
}
