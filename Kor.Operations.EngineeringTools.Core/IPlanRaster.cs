#nullable enable

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>A cropped region of a rendered sheet. <see cref="Lum"/> is row-major 8-bit luminance — the
    /// channel the slab poché needs — length <see cref="Width"/>×<see cref="Height"/>. <see cref="R"/>/
    /// <see cref="G"/>/<see cref="B"/> are the matching colour planes when the decoder supplies them; they are
    /// needed to isolate the neutral-GRAY wall/column fill from coloured line-work (luminance alone cannot tell
    /// a grey fill from an equally-bright colour). Null when only luminance was decoded — the vertical
    /// measurement then yields nothing rather than guessing.</summary>
    public readonly record struct RasterCrop(byte[] Lum, int Width, int Height,
        byte[]? R = null, byte[]? G = null, byte[]? B = null);

    /// <summary>
    /// The raster I/O the engine needs, kept behind an interface so the Core engine does NOT depend on an
    /// image library — the CLI and the WPF app each supply their own decoder. Measurement always runs on the
    /// full-resolution render; <see cref="LoadDownscaledPng"/> only shrinks the copy sent to the vision API
    /// to keep token cost down, and the normalized boxes it returns are resolution-free.
    /// </summary>
    public interface IPlanRaster
    {
        /// <summary>Full-resolution pixel dimensions of a rendered page (to map a normalized box to pixels).</summary>
        (int Width, int Height) ImageSize(string path);

        /// <summary>The page re-encoded with its long edge capped at <paramref name="maxEdge"/>, as PNG bytes.</summary>
        byte[] LoadDownscaledPng(string path, int maxEdge);

        /// <summary>The luminance of the pixel rectangle [x0,y0)–[x1,y1) of the full-resolution page.</summary>
        RasterCrop LoadCrop(string path, int x0, int y0, int x1, int y1);

        /// <summary>The pixel rectangle [x0,y0)–[x1,y1) re-encoded as a PNG with its long edge capped at
        /// <paramref name="maxEdge"/> — a focused image of ONE plate to send to a targeted vision call (e.g.
        /// drop-band apportionment), so the model sees just that plate, not the whole sheet.</summary>
        byte[] LoadCropPng(string path, int x0, int y0, int x1, int y1, int maxEdge);
    }
}
