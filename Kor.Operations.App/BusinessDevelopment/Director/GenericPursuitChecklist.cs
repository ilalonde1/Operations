#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public static class GenericPursuitChecklist
{
    public const string Source = "Generic (Leduc 2026-RFP-IP-061)";
    public const string Note = "verify against this RFP";

    // Hand-off §4, deliberately Manual: these are prompts to verify, not inferred RFP rules.
    public static IReadOnlyList<PursuitRequirementInput> Items { get; } = new[]
    {
        "Cover: RFP title/number · proponent name and contact · date of submission.",
        "Sections in the RFP's order: Executive Summary · Narrative · Team · References · Methodology · Fee (appendix) · Other.",
        "Page limit and its exclusions; count body pages from the rendered PDF, not Word's total.",
        "Executive summary: services · advantages · preparer (name, title, address) · signer empowered to finalise (name, signature, title, address) · assumptions · AI disclosure — about the proposal's preparation, not design practice. Ian's wording: ‘Kor Structural operates a proprietary, in-house operations platform (KOR-Operations) that incorporates AI language-model services. It was used in a supporting role in preparing this proposal: …’ Keep ‘incorporates AI’; without it there is no disclosure.",
        "Team: a resume per member · an organizational chart · the five positions named (SMT, PMT, Design Lead, BIM Lead, Construction Contract Administration) · succession for SMT and PMT · the team leader's $65M collaborative-delivery project mapped to IPD mechanics.",
        "References ≥ 3, each clearing a bar: ≥ $65M · a publicly operated organisation · a recreation centre · ≤ 10 years · none from the buyer · name/address/phone · named contact with title · scope, date, duration, relevance · uninflated values with years.",
        "Fee per §3 item 3: base × (1+burden) = excl. OH; × (1+OH%) = incl. OH; × (1+profit%) = incl. OH & profit. One overhead % and one profit % stated above the table. Rates for everyone; evaluation on the five positions only.",
        "Addenda acknowledged on the portal."
    }.Select(text => new PursuitRequirementInput(text, Source, "Manual", null)).ToList().AsReadOnly();
}
