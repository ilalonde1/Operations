#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kor.Operations.StandardDetails;

/// <summary>
/// What the catalogue says versus what the model holds.
///
/// The census that built the catalogue ran once, in August, as a migration. Nothing has compared
/// the two since, so between 2026-08-06 and now the only way to learn that a view had been renamed,
/// deleted, or given a KOR-D number by hand was for somebody to notice. This is the comparison,
/// on demand.
///
/// It REPORTS. It changes nothing. Healing is done deliberately, through detail.RecordOccurrence
/// and detail.RemoveOccurrence, because every one of these findings has more than one right answer
/// and picking automatically would be a guess.
/// </summary>
internal static class DetailReconciler
{
    internal static async Task<ReconcileReport> CompareAsync(
        DetailIntake intake, KorStandardsReadRepository catalogue, TimeSpan bridgeTimeout)
    {
        // An EMPTY known-set on purpose: reconcile exists to check the catalogue, so it reads the
        // View Prefix on every view rather than trusting the catalogue's own record of which views
        // it holds. That is the slow path (~25s against the 1,079-view standards model) and it is
        // the right one here — the fast path is for the Add screen, which only needs the views
        // nobody has catalogued yet.
        var snapshot = await intake.ListDetailViewsAsync(new HashSet<long>(), bridgeTimeout);
        var rows = await catalogue.LoadCatalogueBindingAsync();

        var byViewId = snapshot.Views.ToDictionary(x => x.Id);
        var catalogued = new HashSet<long>(rows.Where(x => x.ViewElementId.HasValue).Select(x => x.ViewElementId!.Value));

        var unreachable = new List<string>();
        var renamed = new List<string>();
        var unbound = new List<string>();
        var otherDocument = new List<string>();

        foreach (var row in rows)
        {
            if (row.ViewElementId is not { } viewId)
            {
                // A detail with no occurrence at all is invisible to the palette and to publish —
                // it exists only in the register.
                unbound.Add($"{row.DetailNumber} — {row.Title}");
                continue;
            }

            // The August census recorded a different file name than AUTHORING carries today, so a
            // row naming another document is expected for the original 612 and is reported as its
            // own category rather than as a missing view.
            if (!string.Equals(row.DocumentName, snapshot.DocumentName, StringComparison.OrdinalIgnoreCase))
            {
                otherDocument.Add($"{row.DetailNumber} → {row.DocumentName}");
                continue;
            }

            if (!byViewId.TryGetValue(viewId, out var view))
            {
                unreachable.Add($"{row.DetailNumber} — {row.Title} (view {viewId} is not in the model)");
                continue;
            }

            if (!string.Equals(view.Name.Trim(), row.ViewName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                renamed.Add($"{row.DetailNumber}: catalogue says \"{row.ViewName}\", model says \"{view.Name}\"");
            }
        }

        // A view stamped with a KOR-D the catalogue has never issued. Two ways this happens: a
        // number typed by hand, or a detail added and then removed from the catalogue.
        var uncatalogued = snapshot.Views
            .Where(x => x.IsCatalogued && !catalogued.Contains(x.Id))
            .Select(x => $"{x.Prefix.ToUpperInvariant()} on view \"{x.Name}\" ({x.Id})")
            .ToList();

        return new ReconcileReport(
            snapshot.DocumentName,
            snapshot.Views.Count,
            rows.Count,
            unreachable,
            renamed,
            unbound,
            uncatalogued,
            otherDocument);
    }
}

/// <summary>
/// WHAT THIS COVERS: details whose view is missing from the open model, views renamed since the
/// catalogue last looked, details with no occurrence at all, views carrying a KOR-D the catalogue
/// never issued, and rows recorded against a different document.
///
/// WHAT IT DOES NOT COVER: whether the drawing inside a view still shows what the title claims —
/// nothing here opens the geometry. It does not check the rendered art or the PDF against the view,
/// and it only sees the ONE model that is open, so a detail living in a file nobody has open is
/// reported as unreachable rather than examined.
///
/// A same-class fault it would NOT catch: two catalogue rows bound to the SAME view. The unique
/// index on (DocumentName, ViewElementId) makes that impossible today, so it is the database's job,
/// not this report's — but if that index were ever dropped, this report would stay silent.
/// </summary>
internal sealed record ReconcileReport(
    string DocumentName,
    int ViewsInModel,
    int CatalogueRows,
    IReadOnlyList<string> ViewMissingFromModel,
    IReadOnlyList<string> ViewRenamed,
    IReadOnlyList<string> DetailWithNoView,
    IReadOnlyList<string> NumberInModelButNotCatalogued,
    IReadOnlyList<string> RecordedAgainstAnotherDocument)
{
    internal int FindingCount =>
        ViewMissingFromModel.Count + ViewRenamed.Count + DetailWithNoView.Count + NumberInModelButNotCatalogued.Count;

    /// <summary>
    /// Rows recorded against another document are deliberately NOT counted as findings: the whole
    /// original census names a file AUTHORING no longer is, so counting them would report ~1,079
    /// problems on a healthy catalogue and train everyone to ignore the number.
    /// </summary>
    internal bool IsClean => FindingCount == 0;

    internal string Headline => IsClean
        ? $"Catalogue and model agree: {CatalogueRows} rows against {ViewsInModel} views in {DocumentName}."
        : $"{FindingCount} difference(s) between the catalogue and {DocumentName}.";

    internal string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine(Headline);
        text.AppendLine();
        text.AppendLine($"Model:     {ViewsInModel} drafting views and legends in {DocumentName}");
        text.AppendLine($"Catalogue: {CatalogueRows} live detail rows");
        text.AppendLine();

        Section(text, "Detail whose view is not in this model", ViewMissingFromModel);
        Section(text, "View renamed since the catalogue last looked", ViewRenamed);
        Section(text, "Detail with no view recorded at all", DetailWithNoView);
        Section(text, "View carrying a number the catalogue never issued", NumberInModelButNotCatalogued);

        if (RecordedAgainstAnotherDocument.Count > 0)
        {
            text.AppendLine($"Recorded against another document: {RecordedAgainstAnotherDocument.Count} row(s).");
            text.AppendLine("  These are the original census rows, which name the template file the August");
            text.AppendLine("  crawl read rather than AUTHORING. Not a fault — but nothing re-checks them.");
            text.AppendLine();
        }

        if (IsClean)
        {
            text.AppendLine("Nothing to fix.");
        }
        else
        {
            text.AppendLine("Nothing has been changed. Fix a binding with Reconcile's record/remove actions,");
            text.AppendLine("or by setting View Prefix on the view by hand.");
        }

        return text.ToString();
    }

    private static void Section(StringBuilder text, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        text.AppendLine($"{heading} ({items.Count}):");
        // Ten is enough to see the shape; the count above is the population, so the reader is never
        // left inferring a total from a sample.
        foreach (var item in items.Take(10))
        {
            text.AppendLine($"  - {item}");
        }

        if (items.Count > 10)
        {
            text.AppendLine($"  ... and {items.Count - 10} more of {items.Count}.");
        }

        text.AppendLine();
    }
}
