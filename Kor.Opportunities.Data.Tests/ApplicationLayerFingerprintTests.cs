#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kor.Opportunities.Data.Ingestion.Discovery;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// WHAT THESE COVER: that a layer's FIELDS decide what it is, using the real
/// schemas of layers we have already wired or already been fooled by. Every
/// field list below was read off a live ArcGIS service, not written from memory
/// — which matters, because doing exactly that is what caught the missing
/// "or a date" clause: Surrey's IssuedBuildingPermits has no status and no type
/// column, and the first version of the rule called it NotApplications.
///
/// The discrimination that earns its keep is the three-way split on ONE
/// municipality's own server: Surrey publishes an applications case table, four
/// zoning overlays and an issued-permit table side by side, and a keyword sweep
/// cannot tell them apart. Ten of Kelowna's twelve keyword hits on 2026-09-10
/// were overlays.
///
/// WHAT THEY DO NOT COVER:
///   * whether a layer has rows, or is stale — this is schema only.
///   * whether the applications are in KOR's lane. Surrey's layer is
///     unambiguously applications and the relevance gate still drops 375 of
///     1,266 as sewer, road and watermain work.
///   * a jurisdiction naming columns in a scheme none of ours use; it returns
///     NotApplications, which reads identically to "no feed here".
/// A same-class fault these would NOT catch: a layer of applications closed
/// twenty years ago, which has every field a live one has.
/// </summary>
public sealed class ApplicationLayerFingerprintTests
{
    // ---- real schemas, read from live services on 2026-09-23 ----

    // services5.arcgis.com/YRpe0VKTJytZSSIB — "Development Applications"/0
    private static readonly string[] SurreyApplications =
    {
        "OBJECTID", "PROJECT_NO", "DESCRIPTION", "STATUS", "WEBLINK",
        "APPLICATION_DOCUMENTS_WEBLINK", "SHAPE__Area", "SHAPE__Length",
    };

    // The same org: "Hazard Lands Development Permit Area"/0. An overlay.
    private static readonly string[] SurreyHazardOverlay =
    {
        "OBJECTID", "DEVELOPMENT_PERMIT_AREA_NAME", "DESCRIPTION",
        "SHAPE__Area", "SHAPE__Length",
    };

    // The same org again: "IssuedBuildingPermits"/0. No status, no type.
    private static readonly string[] SurreyIssuedPermits =
    {
        "PermitNumber", "IssuedDate", "ValueOfConstruction", "ObjectId",
        "ProjectAddress", "WorkDescription", "SubDescription", "DwellingUnits",
        "ApplicantOrganization", "BuildingGeneralContractorOrganization",
    };

    // maps.kamloops.ca CityMap_PlanningDevelopment/228 — Tempest, and the column
    // names arrive fully qualified.
    private static readonly string[] KamloopsTempest =
    {
        "GIS.GIST_TEMPEST_PLANNING_APPL.GISLINK",
        "GIS.GIST_TEMPEST_PLANNING_APPL.FOLDER_NUMBER",
        "GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_SUBJECT",
        "GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_PURPOSE",
        "GIS.GIST_TEMPEST_PLANNING_APPL.CREATED_DATE",
        "GIS.GIST_TEMPEST_PLANNING_APPL.COMPLETED_DATE",
        "GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_TYPE",
        "GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_STATUS",
        "GIS.Property.ADDRESS", "SHAPE.AREA", "SHAPE.LEN",
    };

    [Fact]
    public void SurreysApplicationsLayerIsRecognisedAsApplications()
    {
        var v = ApplicationLayerFingerprint.Classify("Development Applications", SurreyApplications);

        Assert.Equal(LayerKind.Applications, v.Kind);
        Assert.Equal("PROJECT_NO", v.FileNumberField);
        Assert.Equal("STATUS", v.StatusField);
    }

    [Fact]
    public void SurreysOverlayIsNotMistakenForApplications()
    {
        // This is the false positive that cost the most time. It has
        // "Development Permit" in its name and no file number in its schema.
        var v = ApplicationLayerFingerprint.Classify(
            "Hazard_Lands_Development_Permit_Area", SurreyHazardOverlay);

        Assert.Equal(LayerKind.ZoningOverlay, v.Kind);
        Assert.Null(v.FileNumberField);
        Assert.Empty(v.SuggestedMapping);
    }

    [Fact]
    public void SurreysIssuedPermitsAreRecognisedButMarkedLate()
    {
        var v = ApplicationLayerFingerprint.Classify("IssuedBuildingPermits", SurreyIssuedPermits);

        Assert.Equal(LayerKind.IssuedPermits, v.Kind);
        Assert.Equal("PermitNumber", v.FileNumberField);
        Assert.Equal("IssuedDate", v.DateField);
        Assert.Contains("ISSUED", v.Why, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OneMunicipalityThreeLayersThreeVerdicts()
    {
        // The whole point, on one server, in one assertion: a keyword sweep
        // returns all three of these and cannot rank them.
        Assert.Equal(
            new[] { LayerKind.Applications, LayerKind.ZoningOverlay, LayerKind.IssuedPermits },
            new[]
            {
                ApplicationLayerFingerprint.Classify("Development Applications", SurreyApplications).Kind,
                ApplicationLayerFingerprint.Classify("Hazard_Lands_Development_Permit_Area", SurreyHazardOverlay).Kind,
                ApplicationLayerFingerprint.Classify("IssuedBuildingPermits", SurreyIssuedPermits).Kind,
            });
    }

    [Fact]
    public void FullyQualifiedTempestColumnsResolveToTheirLeafName()
    {
        var v = ApplicationLayerFingerprint.Classify(
            "Planning Applications (Active Only)", KamloopsTempest);

        Assert.Equal(LayerKind.Applications, v.Kind);
        Assert.Equal("GIS.GIST_TEMPEST_PLANNING_APPL.FOLDER_NUMBER", v.FileNumberField);
        Assert.Equal("GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_STATUS", v.StatusField);
        Assert.Equal("GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_TYPE", v.TypeField);

        // The suggested mapping must carry the QUALIFIED name, because that is
        // what the layer answers to in an outFields query.
        Assert.Equal(
            "GIS.GIST_TEMPEST_PLANNING_APPL.FOLDER_NUMBER",
            v.SuggestedMapping["arcgis.externalRefField"]);
    }

    /// <summary>
    /// The nine ArcGIS sources already wired, by the field names their live
    /// config maps. If the fingerprint cannot recognise a layer we are already
    /// ingesting from, it is not a fingerprint.
    /// </summary>
    public static IEnumerable<object[]> AlreadyWiredSources() => new List<object[]>
    {
        new object[] { "Victoria", new[] { "FOLDER_NUMBER", "SUBJECT", "STATUS", "AppType", "CREATED_DATE", "HOUSE", "STREET" } },
        new object[] { "Courtenay", new[] { "FOLDER_NUMBER", "SUBJECT", "FolderStatus", "FolderTypeSubject", "CREATED_DATE" } },
        new object[] { "CampbellRiver", new[] { "AppNumber", "Address", "Status", "Type", "Applicant" } },
        new object[] { "ComoxPlanning", new[] { "AppNumber", "Address", "Status", "PermitType" } },
        new object[] { "Coquitlam", new[] { "PROJECT_NUMBER", "ADDRESS", "PROJECT_STATUS", "APPLICANT", "SUBMISSION_DATE" } },
        new object[] { "Langford", new[] { "PermitNumber", "Full_Address", "Type", "Entered" } },
        new object[] { "MapleRidge", new[] { "ReferenceFile", "Name", "ApplicationType", "InDate" } },
        new object[] { "QualicumBeach", new[] { "FileNumber", "CivicAddress", "Status", "ApplicationType", "ApplicationDate" } },
        new object[] { "Surrey", SurreyApplications },
    };

    [Theory]
    [MemberData(nameof(AlreadyWiredSources))]
    public void EveryLayerWeAlreadyIngestFromIsRecognised(string label, string[] fields)
    {
        var v = ApplicationLayerFingerprint.Classify(label + " Development Applications", fields);

        Assert.True(
            v.Kind == LayerKind.Applications,
            $"{label} is a source we already ingest from, but the fingerprint said {v.Kind}: {v.Why}");
        Assert.NotNull(v.FileNumberField);
        Assert.True(
            v.SuggestedMapping.ContainsKey("arcgis.externalRefField")
            && v.SuggestedMapping.ContainsKey("arcgis.titleField"),
            $"{label} produced no usable mapping.");
    }

    [Fact]
    public void ALayerWithNoFieldsAtAllIsNotApplications()
    {
        var v = ApplicationLayerFingerprint.Classify("Development Applications", new string[0]);
        Assert.Equal(LayerKind.NotApplications, v.Kind);
    }

    [Fact]
    public void AnOverlayNamedLikeAnOverlayWithAFileNumberIsStillFlaggedForAHuman()
    {
        // Schema wins over name, but the caller is told the name disagrees so a
        // person opens it before it is wired.
        var v = ApplicationLayerFingerprint.Classify(
            "Streamside Development Permit Area",
            new[] { "FileNumber", "CivicAddress", "Status" });

        Assert.Equal(LayerKind.Applications, v.Kind);
        Assert.Contains("overlay", v.Why, System.StringComparison.OrdinalIgnoreCase);
    }
}
