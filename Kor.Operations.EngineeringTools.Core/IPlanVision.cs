#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Layer 2 (the only AI in the takeoff): the two judgment-and-location calls the engine needs, kept
    /// behind an interface so the Core engine has NO dependency on an HTTP client or an API key — the CLI
    /// and the WPF app each supply their own implementation. Both calls return the model's RAW JSON string
    /// (the engine parses it); neither call ever reads a dimension — numbers always come from exact vector
    /// text and poché geometry. <see cref="SynthesizePageAsync"/> classifies a sheet (floor_plan vs other);
    /// <see cref="LocatePlateAsync"/> points at the slab plate's normalized bounding box on the rendered page.
    /// </summary>
    public interface IPlanVision
    {
        /// <summary>Classify one page from its serialized <c>DrawingDigest</c> page JSON. Returns raw JSON
        /// with at least a <c>sheetKind</c> string.</summary>
        Task<string> SynthesizePageAsync(string pageJson, CancellationToken ct = default);

        /// <summary>Locate the slab plate on a page: given the page JSON and the downscaled PNG bytes,
        /// returns raw JSON with a normalized <c>slabBox</c> (and optional <c>level</c>/<c>slabThicknessIn</c>).</summary>
        Task<string> LocatePlateAsync(string pageJson, byte[] downscaledPng, CancellationToken ct = default);

        /// <summary>The ONE targeted thickness call: a transfer/podium plate the deterministic pass already
        /// proved is thickened (its drawing calls out drop bands deeper than the field slab), but cannot
        /// cheaply apportion. Given a PNG crop of just that plate and the thickness values (inches) its
        /// drawing calls out, return raw JSON <c>{ "fractions": [ { "thicknessIn", "areaPct" } ] }</c> — the
        /// % of the plan AREA at each thickness, summing to ~100. The model only apportions the bands it can
        /// SEE; it never invents a thickness (those came from the exact text). Fired only on band plates, so
        /// a whole set costs a handful of calls, not one per page.</summary>
        Task<string> ApportionThicknessAsync(byte[] plateCropPng, IReadOnlyList<int> thicknessesIn, CancellationToken ct = default);
    }
}
