#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Opportunities.Data.Ingestion.Discovery;

/// <summary>
/// Decide what an ArcGIS layer IS from its FIELDS, not from its name.
///
/// WHY FIELDS. Hunting these by name does not work and has cost real time. On
/// 2026-09-10 a keyword sweep over the ArcGIS Online catalogue returned ten hits
/// for Kelowna and every one was a "Development Permit Area" — a zoning overlay
/// saying where rules apply. An overlay matches every keyword an application
/// layer matches, returns perfectly valid features, and would ingest cleanly as
/// nonsense. Burnaby's single hit was "Streamside Development Permit Area".
///
/// A schema cannot lie the same way. Across the nine ArcGIS sources already
/// wired — Victoria, Courtenay, Campbell River, Comox x2, Coquitlam, Langford,
/// Maple Ridge, Qualicum Beach, Surrey — every applications layer carries:
///
///   * a FILE NUMBER field   (FOLDER_NUMBER, PROJECT_NO, AppNumber, FileNumber,
///                            PermitNumber, ReferenceFile, PROJECT_NUMBER …)
///   * a STATUS or TYPE field
///   * an ADDRESS or SUBJECT field
///
/// and no zoning overlay carries a file number, because an overlay is not a
/// case. That triple is the fingerprint.
///
/// WHAT THIS COVERS: whether a layer is an application case table, an issued-
/// permit table, a zoning overlay, or none of those; and, for an application
/// layer, which field should fill each arcgis.* mapping key.
///
/// WHAT IT DOES NOT COVER, and these need a human or ArcGisProbe:
///   * whether the layer has any ROWS, or is stale — the fingerprint is schema
///     only. A perfectly-shaped empty layer scores the same as a live one.
///   * whether the applications are IN LANE. Surrey's layer is unambiguously an
///     applications table and 375 of its 1,266 are sewer, road and watermain
///     work the relevance gate drops.
///   * a jurisdiction that names its columns in French, or in a scheme none of
///     the nine use. It returns NotApplications, which reads the same as "no
///     feed here" — so a NotApplications verdict is weaker evidence than an
///     Applications one.
/// A same-class fault it would NOT catch: a layer of HISTORIC applications,
/// closed decades ago. It has every field an active one has.
/// </summary>
public static class ApplicationLayerFingerprint
{
    /// <summary>A file number is the thing an overlay never has.</summary>
    private static readonly string[] FileNumberTokens =
    {
        "foldernumber", "folderno", "filenumber", "fileno", "appnumber", "applicationnumber",
        "permitnumber", "permitno", "projectnumber", "projectno", "referencefile", "refno",
        "referencenumber", "casenumber", "caseno", "devappno", "applicationid", "permitid",
    };

    private static readonly string[] StatusTokens =
    {
        "status", "folderstatus", "permitstatus", "projectstatus", "appstatus", "stage",
    };

    private static readonly string[] TypeTokens =
    {
        "apptype", "applicationtype", "permittype", "foldertype", "typeofwork", "permitcategory",
        "type", "category", "foldertypesubject",
    };

    private static readonly string[] AddressTokens =
    {
        "address", "civicaddress", "fulladdress", "subject", "permitsubject", "location",
        "streetname", "street", "civic", "description", "projectdescription", "purpose",
        "permitpurpose", "name",
    };

    private static readonly string[] DateTokens =
    {
        "createddate", "created", "applicationdate", "submissiondate", "indate", "entered",
        "dateapplied", "receiveddate", "opendate", "issuedate", "issueddate", "permitnumbercreateddate",
    };

    private static readonly string[] ApplicantTokens =
    {
        "applicant", "applicantname", "owner", "ownername", "agent", "developer",
        "buildingcontractor", "consultant",
    };

    /// <summary>An overlay says where rules apply. It is not a case.</summary>
    private static readonly string[] OverlayNameTokens =
    {
        "permit area", "development permit area", "dpa", "hazard", "wildfire", "streamside",
        "riparian", "farming", "sensitive", "aquatic", "form and character", "catchment",
        "floodplain", "covenant area", "zoning", "land use designation", "oarea",
    };

    /// <summary>Issued is the wrong end of the job — the engineer is already chosen.</summary>
    private static readonly string[] IssuedNameTokens =
    {
        "issued", "issuedbuildingpermits", "building permits", "buildingpermit",
    };

    public static LayerVerdict Classify(string? layerName, IEnumerable<string>? fieldNames)
    {
        var name = (layerName ?? "").Trim();
        var lowerName = name.ToLowerInvariant();
        var fields = (fieldNames ?? Enumerable.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.Trim())
            .ToArray();

        // Qualified names arrive as GIS.GIST_TEMPEST_PLANNING_APPL.FOLDER_NUMBER.
        // Only the last segment is the column.
        var leaf = fields
            .Select(f => f.Contains('.') ? f[(f.LastIndexOf('.') + 1)..] : f)
            .Select(Normalize)
            .Where(f => f.Length > 0)
            .ToArray();

        var fileNumber = FirstMatch(fields, leaf, FileNumberTokens);
        var status = FirstMatch(fields, leaf, StatusTokens);
        var type = FirstMatch(fields, leaf, TypeTokens);
        var address = FirstMatch(fields, leaf, AddressTokens);
        var date = FirstMatch(fields, leaf, DateTokens);
        var applicant = FirstMatch(fields, leaf, ApplicantTokens);

        var looksOverlay = OverlayNameTokens.Any(t => lowerName.Contains(t, StringComparison.Ordinal));
        var looksIssued = IssuedNameTokens.Any(t => lowerName.Contains(t, StringComparison.Ordinal));

        // The fingerprint. A file number, something a human can read, and at
        // least one thing that moves — a status, a type, or a date.
        //
        // ⚠ The "or a date" clause is not decoration. Surrey's own
        //   IssuedBuildingPermits layer carries PermitNumber, ProjectAddress and
        //   IssuedDate and NO status or type column at all; requiring
        //   status-or-type classified a real permit table as NotApplications.
        //   Found by running this against three live Surrey layers rather than
        //   against schemas written from memory.
        var hasFingerprint = fileNumber is not null
                             && address is not null
                             && (status is not null || type is not null || date is not null);

        LayerKind kind;
        string why;

        if (!hasFingerprint)
        {
            // No file number and an overlay-shaped name is the common false
            // positive; say so explicitly so the caller can report it.
            kind = looksOverlay ? LayerKind.ZoningOverlay : LayerKind.NotApplications;
            why = fileNumber is null
                ? "no file-number field — an overlay or a non-case layer"
                : address is null
                    ? "has a file number but nothing human-readable to title a lead with"
                    : "has a file number but nothing that moves — no status, type or date";
        }
        else if (looksIssued)
        {
            kind = LayerKind.IssuedPermits;
            why = "fingerprint matches, but the name says ISSUED — late signal, the engineer is already chosen";
        }
        else if (looksOverlay)
        {
            // Both signals present: trust the schema, but flag it loudly.
            kind = LayerKind.Applications;
            why = "fingerprint matches although the NAME reads like an overlay — open this one before wiring it";
        }
        else
        {
            kind = LayerKind.Applications;
            why = "file number + status/type + address";
        }

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (kind is LayerKind.Applications or LayerKind.IssuedPermits)
        {
            if (fileNumber is not null) mapping["arcgis.externalRefField"] = fileNumber;
            if (address is not null) mapping["arcgis.titleField"] = address;
            if (status is not null) mapping["arcgis.statusField"] = status;
            if (type is not null) mapping["arcgis.typeField"] = type;
            if (date is not null) mapping["arcgis.postedDateField"] = date;
            if (applicant is not null) mapping["arcgis.applicantField"] = applicant;
        }

        return new LayerVerdict(name, kind, why, mapping, fileNumber, status, type, address, date, applicant);
    }

    private static string Normalize(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                buf[n++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buf[..n]);
    }

    /// <summary>
    /// Exact normalised match first, then a contains fallback. Exact-first
    /// matters: "PermitType" must map to typeField, not be swallowed by the
    /// address token "type" appearing inside some other column.
    /// </summary>
    private static string? FirstMatch(string[] original, string[] leaf, string[] tokens)
    {
        for (var i = 0; i < leaf.Length; i++)
        {
            if (tokens.Contains(leaf[i], StringComparer.Ordinal))
            {
                return original[i];
            }
        }

        for (var i = 0; i < leaf.Length; i++)
        {
            foreach (var t in tokens)
            {
                if (t.Length >= 5 && leaf[i].Contains(t, StringComparison.Ordinal))
                {
                    return original[i];
                }
            }
        }

        return null;
    }
}

public enum LayerKind
{
    /// <summary>A case table of live planning applications. What we want.</summary>
    Applications,

    /// <summary>Issued permits. Real, but late — construction has been let.</summary>
    IssuedPermits,

    /// <summary>A zoning overlay. The false positive that wastes the most time.</summary>
    ZoningOverlay,

    /// <summary>Anything else.</summary>
    NotApplications,
}

/// <param name="SuggestedMapping">
/// The arcgis.* config a migration would carry. Emitted so a new city is a
/// generated config row rather than a hand-guess — which is how Surrey ended up
/// with a client-side filter and a DEGRADED first run.
/// </param>
public sealed record LayerVerdict(
    string LayerName,
    LayerKind Kind,
    string Why,
    IReadOnlyDictionary<string, string> SuggestedMapping,
    string? FileNumberField,
    string? StatusField,
    string? TypeField,
    string? AddressField,
    string? DateField,
    string? ApplicantField);
