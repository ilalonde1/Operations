#nullable enable

namespace Kor.Operations.Architecture;

public sealed record ScopedView(
    string Name,
    string Title,
    string Subtitle,
    string Header,
    double PageWidth,
    double PageHeight,
    IReadOnlyList<ScopedDerivedNode> DerivedNodes,
    IReadOnlyList<ScopedAuthoredNode> AuthoredNodes,
    IReadOnlyList<ScopedOverlayEdge> Edges);

public sealed record ScopedDerivedNode(
    string Id,
    string Label,
    string Detail,
    string Group,
    string Fill,
    string State,
    double X,
    double Y,
    double W,
    double H,
    string? Cluster,
    IReadOnlyList<string> IncludeIds);

public sealed record ScopedAuthoredNode(
    string Id,
    string Title,
    string Detail,
    string Fill,
    string State,
    double X,
    double Y,
    double W,
    double H);

public sealed record ScopedOverlayEdge(
    string From,
    string To,
    string Label,
    string State);

public static class ScopedViews
{
    private static readonly IReadOnlyList<ScopedView> Views = new[]
    {
        StandardsEstate(),
    };

    public static IReadOnlyList<ScopedView> All => Views;

    public static ScopedView? Find(string name)
        => Views.SingleOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    private static ScopedView StandardsEstate()
        => new(
            "standards-estate",
            "Standard Details estate",
            "MASTER = AUTHORING minus un-approved. Solid edges are live on the 302N pilot; the dashed edge is the fleet deployment still left.",
            "KOR STANDARD DETAILS - THE ESTATE\nsolid = LIVE on the 302N pilot · dashed = deploy to the fleet, the one step left · 2026-09-02",
            12.2,
            8.6,
            new[]
            {
                new ScopedDerivedNode(
                    "operations-app",
                    "OPERATIONS APP - STD DETAILS",
                    "author -> approve -> PUBLISH · Publish-to-Master BUILT + audited",
                    "desktop app",
                    "RGB(226,240,226)",
                    "live",
                    4.05,
                    6.15,
                    2.50,
                    1.30,
                    Cluster: null,
                    IncludeIds: new[]
                    {
                        "Kor.Operations.App:Kor.Operations.StandardDetails.MasterPublisher",
                        "Kor.Operations.App:Kor.Operations.StandardDetails.DrafterBridgeClient",
                        "Kor.Operations.App:Kor.Operations.StandardDetails.KorStandardsReadRepository",
                        "Kor.Operations.App:Kor.Operations.StandardDetails.StandardDetailsWindow",
                    }),
            },
            new[]
            {
                new ScopedAuthoredNode(
                    "details-palette",
                    "DETAILS PALETTE",
                    "LIVE on the 302N pilot · SQL-direct, approved-only · serves 604 placeable · (was dormant, 0 placeable)",
                    "RGB(252,236,219)",
                    "live",
                    7.55,
                    4.10,
                    2.20,
                    1.50),
                new ScopedAuthoredNode(
                    "drafters-revit",
                    "DRAFTERS' REVIT",
                    "copy the MASTER to start · loader auto-update · hash-verified publish",
                    "RGB(244,244,244)",
                    "live",
                    10.05,
                    1.70,
                    1.85,
                    1.15),
                new ScopedAuthoredNode(
                    "drafter-bridge",
                    "BRIDGE (KOR-302N)",
                    "drives Revit headless · census / renders / id writes · + Publish-to-Master derive",
                    "RGB(222,235,247)",
                    "live",
                    0.6,
                    1.70,
                    2.35,
                    1.15),
                new ScopedAuthoredNode(
                    "gatekeeper",
                    "THE GATEKEEPER",
                    "Champion: Serban (Jim, 2024) · approval = promotion",
                    "RGB(255,255,255)",
                    "live",
                    0.6,
                    6.15,
                    2.35,
                    1.15),
                new ScopedAuthoredNode(
                    "korstandards-sql",
                    "KORSTANDARDS (SQL)",
                    "identity / provenance / rulings · confidence ladder (placeable = content-verified & up) · 604 details + 288 parts",
                    "RGB(238,238,238)",
                    "live",
                    4.05,
                    4.10,
                    2.50,
                    1.50),
                new ScopedAuthoredNode(
                    "kor-tools",
                    "KOR TOOLS RIBBON",
                    "live fleet, additive law · + Quick Insert governed (SQL) · 288 parts, unit-aware",
                    "RGB(238,230,246)",
                    "live",
                    10.05,
                    4.10,
                    1.85,
                    1.50),
                new ScopedAuthoredNode(
                    "templates",
                    "AUTHORING + MASTER",
                    "AUTHORING: all details / ~1,079 views · MASTER: curated to 604 approved · KOR-D identity, de-branded · conformance 8/8 GREEN",
                    "RGB(255,244,214)",
                    "live",
                    0.6,
                    4.10,
                    2.35,
                    1.50),
            },
            new[]
            {
                new ScopedOverlayEdge("details-palette", "kor-tools", "additive tab (branch)", "live"),
                new ScopedOverlayEdge("drafter-bridge", "templates", "runs the model", "live"),
                new ScopedOverlayEdge("gatekeeper", "operations-app", "approves in the app", "live"),
                new ScopedOverlayEdge("kor-tools", "drafters-revit", "publish.ps1 -> share", "live"),
                new ScopedOverlayEdge("korstandards-sql", "details-palette", "catalog: placeable only", "live"),
                new ScopedOverlayEdge("korstandards-sql", "kor-tools", "288 parts", "live"),
                new ScopedOverlayEdge("operations-app", "drafter-bridge", "Publish to Master -> rebuild", "live"),
                new ScopedOverlayEdge("operations-app", "drafters-revit", "deploy app to fleet (last step)", "built"),
                new ScopedOverlayEdge("operations-app", "korstandards-sql", "approval -> placeable", "live"),
                new ScopedOverlayEdge("templates", "korstandards-sql", "census + conformance", "live"),
            });
}
