#nullable enable
using System;
using System.Globalization;
using System.Linq;

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
    public const string PursuitKind = "pursuit";
    public const string DirectorKind = "director";
    public const string MoveKind = "move";
    private static readonly string[] MoveResults = { "Done", "NotToday", "NotDoing" };

    /// <summary>
    /// Records what Ian did with one move of an imported hour, from the 6 a.m. email: kor://move/{hourId}/{rank}/{result}.
    /// Path segments, not a query string, so the address survives the shell's protocol handler intact.
    /// </summary>
    public static string Move(long hourId, int rank, string result) =>
        $"kor://{MoveKind}/{hourId.ToString(CultureInfo.InvariantCulture)}/{rank.ToString(CultureInfo.InvariantCulture)}/{result}";

    /// <summary>Parses a move link. Defensive like <see cref="TryParse"/>: anything malformed returns false.</summary>
    public static bool TryParseMove(string? uri, out long hourId, out int rank, out string result)
    {
        hourId = 0; rank = 0; result = string.Empty;
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith("kor://", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !string.Equals(parsed.Host, MoveKind, StringComparison.OrdinalIgnoreCase))
            return false;
        var parts = parsed.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 3
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h) || h <= 0
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var r) || r is < 1 or > 12)
            return false;
        var match = MoveResults.FirstOrDefault(m => string.Equals(m, parts[2], StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;
        (hourId, rank, result) = (h, r, match);
        return true;
    }

    /// <summary>The BD Director's views, by the id a kor://director/{id} link carries.</summary>
    public const long DirectorToday = 1, DirectorActions = 2, DirectorMeetings = 3, DirectorReferences = 4;

    /// <summary>A CRM pursuit (CrmEngagements id), opened in the BD workspace. Null id yields no link.</summary>
    public static string? Pursuit(long? engagementId) =>
        engagementId is { } id ? $"kor://{PursuitKind}/{id.ToString(CultureInfo.InvariantCulture)}" : null;

    /// <summary>A BD Director view: DirectorToday, DirectorActions, DirectorMeetings or DirectorReferences.</summary>
    public static string Director(long view) => $"kor://{DirectorKind}/{view.ToString(CultureInfo.InvariantCulture)}";

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
        if (host is not (MpiKind or OrgKind or PersonKind or OpportunityKind or PursuitKind or DirectorKind))
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
