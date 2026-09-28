#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Kor.Operations.StandardDetails.BridgeJson;

namespace Kor.Operations.StandardDetails;

/// <summary>One drafting view or legend in the standards model, and the KOR-D number it carries (if any).</summary>
/// <param name="KnownToCatalogue">
/// True when the catalogue already holds an occurrence for this view id. When it does, the View
/// Prefix is not read at all and <see cref="Prefix"/> stays empty — see
/// <see cref="DetailIntake.ListDetailViewsAsync"/> for why.
/// </param>
internal sealed record ViewInModel(long Id, string Name, string Kind, string Prefix, bool KnownToCatalogue)
{
    /// <summary>
    /// True when this drawing is already a standard — either the catalogue knows the view, or the
    /// view itself carries a KOR-D number somebody typed in without telling the catalogue.
    /// </summary>
    internal bool IsCatalogued => KnownToCatalogue || IsDetailNumber(Prefix);
}

/// <summary>What the model said, and whether the bridge was reachable at all.</summary>
internal sealed record ModelViewSnapshot(
    string DocumentName,
    IReadOnlyList<ViewInModel> Views)
{
    internal IReadOnlyList<ViewInModel> Uncatalogued => Views.Where(x => !x.IsCatalogued).ToList();
}

/// <summary>
/// The model half of adding a detail: find the drawings that are not yet standards, and stamp a
/// minted number back onto the one the gatekeeper picked.
///
/// The catalogue half is detail.AddDetail (migration 102) via KorStandardsPromoterRepository. They
/// are deliberately separate: the number is minted and the row written first, and only then does
/// the model get stamped, so a failed stamp leaves a detail that can be fixed rather than a number
/// burned on nothing.
/// </summary>
internal sealed class DetailIntake
{
    // Matches MasterPublisher: the bridge reads parameters in batches, and 300 ids per call is the
    // size already proven against the 1,079-view standards model.
    private const int ParameterBatchSize = 300;

    private readonly DrafterBridgeClient _bridge;
    private readonly StandardDetailsMasterPublishOptions _options;

    internal DetailIntake(DrafterBridgeClient bridge, StandardDetailsMasterPublishOptions options)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// The document name an occurrence is recorded against. Taken from the configured AUTHORING
    /// path rather than from the bridge's activeDoc, because Revit reports a document TITLE, which
    /// may or may not carry the .rvt extension depending on version and settings — and that string
    /// is half of the occurrence's unique key, so it cannot be allowed to vary.
    ///
    /// NOTE: the 1,079 rows migration 018's census wrote say Kor_Structural_Standards_Template_R25.rvt,
    /// while AUTHORING is KOR-Standards-Authoring-R25.rvt. Those are different files. Details added
    /// from here are therefore recorded against AUTHORING, which is where their views actually live;
    /// the older rows still name the file the August crawl read. Reconcile will report that, and it
    /// should — it is exactly the catalogue-versus-model drift nobody could see before.
    /// </summary>
    internal string DocumentName => Path.GetFileName(_options.AuthoringPath);

    /// <summary>
    /// Every drafting view and legend in the open model. Views that are not already standards are
    /// the candidates for "make this a standard detail".
    ///
    /// WHY <paramref name="knownViewIds"/> EXISTS. getparams returns EVERY parameter on an element
    /// — 46 of them on a drafting view — and there is no way to ask for one by name. Reading the
    /// View Prefix on all 1,079 views in the standards model measured 25.1 seconds against the live
    /// bridge on 2026-09-28, in four batches of 300. That is the same "takes far too long with no
    /// visual cue" the sheet composer was pulled up for.
    ///
    /// But the catalogue already knows which view ids it holds, and that read is milliseconds. So
    /// the prefix is only read for views the catalogue has NEVER heard of — which is the handful a
    /// gatekeeper just drew, plus anything somebody numbered by hand. On the live model today that
    /// is 0 of 1,079, and the screen opens instantly.
    ///
    /// Pass an empty set to force the slow, complete read (what reconcile wants when it is checking
    /// the catalogue rather than trusting it).
    /// </summary>
    internal async Task<ModelViewSnapshot> ListDetailViewsAsync(IReadOnlySet<long> knownViewIds, TimeSpan bridgeTimeout)
    {
        var reply = await _bridge.SendAsync(new { verb = "query", kind = "views" }, bridgeTimeout);
        if (!reply.Ok)
        {
            throw new InvalidOperationException($"Drafter bridge refused the view query: {reply.Error ?? "no reason given"}.");
        }

        var candidates = new List<(long Id, string Name, string Kind)>();
        foreach (var item in EnumerateResultItems(reply.Result, "views", "items", "elements", "results"))
        {
            if (!TryGetInt64(item, "id", out var id))
            {
                continue;
            }

            // A view template is a setting, not a drawing.
            if (TryGetBool(item, "isTemplate", out var isTemplate) && isTemplate)
            {
                continue;
            }

            // detail.DetailOccurrence only admits these two (CK_Occ_ViewKind), and they are the only
            // kinds a standard detail is ever drawn in.
            var kind = TryGetString(item, "type") ?? "";
            if (!string.Equals(kind, "DraftingView", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(kind, "Legend", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates.Add((id, TryGetString(item, "name") ?? "", kind));
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "The open model reports no drafting views or legends. Open AUTHORING in the Revit session the bridge is watching, then try again.");
        }

        var unknown = candidates.Where(x => !knownViewIds.Contains(x.Id)).Select(x => x.Id).ToList();
        var prefixes = await ReadViewPrefixesAsync(unknown, bridgeTimeout);

        var views = candidates
            .Select(x => new ViewInModel(
                x.Id, x.Name, x.Kind,
                prefixes.TryGetValue(x.Id, out var p) ? p : "",
                knownViewIds.Contains(x.Id)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModelViewSnapshot(DocumentName, views);
    }

    /// <summary>
    /// Writes the minted number into the view's View Prefix parameter and reads it back from the
    /// reply the bridge produces AFTER its commit — so this returns what the model actually holds,
    /// not an echo of what was asked for.
    /// </summary>
    internal async Task<(bool ok, string message)> StampViewPrefixAsync(long viewElementId, string detailNumber, TimeSpan bridgeTimeout)
    {
        try
        {
            var reply = await _bridge.SendAsync(new
            {
                verb = "setparams",
                edits = new[]
                {
                    new { id = viewElementId, @params = new Dictionary<string, string> { ["View Prefix"] = detailNumber } },
                },
            }, bridgeTimeout);

            if (!reply.Ok)
            {
                return (false, reply.Error ?? "The bridge refused the parameter write and gave no reason.");
            }

            // setparams is all-or-nothing and reports nowReads after the commit and regeneration.
            // If it does not report our number back, say so rather than assume.
            foreach (var applied in EnumerateResultItems(reply.Result, "applied", "items", "results"))
            {
                if (!TryGetInt64(applied, "id", out var id) || id != viewElementId)
                {
                    continue;
                }

                var nowReads = TryGetString(applied, "nowReads") ?? TryGetString(applied, "value") ?? "";
                if (string.Equals(nowReads.Trim(), detailNumber, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, $"View Prefix on the Revit view now reads {detailNumber}.");
                }

                return (false, $"The bridge wrote the parameter but the view now reads '{nowReads}' instead of {detailNumber}.");
            }

            return (true, $"View Prefix set to {detailNumber} (the bridge did not echo the new value back).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<IReadOnlyDictionary<long, string>> ReadViewPrefixesAsync(IReadOnlyList<long> viewIds, TimeSpan bridgeTimeout)
    {
        var prefixes = new Dictionary<long, string>();
        if (viewIds.Count == 0)
        {
            return prefixes;
        }

        for (var index = 0; index < viewIds.Count; index += ParameterBatchSize)
        {
            var batch = viewIds.Skip(index).Take(ParameterBatchSize).ToArray();
            var reply = await _bridge.SendAsync(new { verb = "getparams", ids = batch }, bridgeTimeout);
            if (!reply.Ok)
            {
                throw new InvalidOperationException($"Drafter bridge refused getparams: {reply.Error ?? "no reason given"}.");
            }

            foreach (var item in EnumerateResultItems(reply.Result, "elements", "items", "results"))
            {
                // A view with no View Prefix parameter is a legitimate non-detail, not an error —
                // it is simply a candidate. Same rule MasterPublisher learned the hard way.
                if (TryGetInt64(item, "id", out var id) && TryReadViewPrefix(item, out var prefix))
                {
                    prefixes[id] = prefix;
                }
            }
        }

        return prefixes;
    }
}
