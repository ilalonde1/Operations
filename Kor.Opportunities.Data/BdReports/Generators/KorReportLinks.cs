#nullable enable
using System;
using System.Globalization;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// The kor:// drill-down link vocabulary shared by generators (who mint
/// links), HtmlPreviewBuilder (who renders them as anchors), and
/// BdReportsWindow (who intercepts WebView2 navigation and routes to the
/// existing detail surfaces). Links are a PREVIEW affordance only —
/// DocxBuilder ignores them by design, so preview and DOCX content stay
/// identical.
/// </summary>
public static class KorReportLinks
{
    public const string MpiKind = "mpi";
    public const string OrgKind = "org";
    public const string PersonKind = "person";
    public const string OpportunityKind = "opp";

    /// <summary>Pursuit/project drill-down: MajorProjectsInventory id.</summary>
    public static string Mpi(long mpiId) => $"kor://{MpiKind}/{mpiId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Live-tender drill-down: opportunities.Opportunities id. Lands on the
    /// Opportunities hub's "All tenders" registry with the row pre-selected —
    /// there is no standalone opportunity window, the registry IS the listing.
    /// Null id yields no link.
    /// </summary>
    public static string? Opportunity(long? opportunityId) =>
        opportunityId is { } id ? $"kor://{OpportunityKind}/{id.ToString(CultureInfo.InvariantCulture)}" : null;

    /// <summary>Org dossier drill-down: CanonicalOrg id. Null id (unresolved edge) yields no link.</summary>
    public static string? Org(long? canonicalOrgId) =>
        canonicalOrgId is { } id ? $"kor://{OrgKind}/{id.ToString(CultureInfo.InvariantCulture)}" : null;

    /// <summary>Person dossier drill-down: IntelPerson id. Null id yields no link.</summary>
    public static string? Person(long? intelPersonId) =>
        intelPersonId is { } id ? $"kor://{PersonKind}/{id.ToString(CultureInfo.InvariantCulture)}" : null;

    /// <summary>
    /// Parses a kor:// uri into (kind, id). Defensive: anything malformed —
    /// wrong scheme, unknown kind, non-numeric or non-positive id — returns
    /// false rather than throwing, because the input ultimately comes from
    /// rendered HTML.
    /// </summary>
    public static bool TryParse(string? uri, out string kind, out long id)
    {
        kind = string.Empty;
        id = 0;

        if (string.IsNullOrEmpty(uri)
            || !uri.StartsWith("kor://", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        var host = parsed.Host.ToLowerInvariant();
        if (host is not (MpiKind or OrgKind or PersonKind or OpportunityKind))
        {
            return false;
        }

        var path = parsed.AbsolutePath.Trim('/');
        if (!long.TryParse(path, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0)
        {
            return false;
        }

        kind = host;
        id = parsedId;
        return true;
    }
}
