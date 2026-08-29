using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// EVERY TEST THAT READS OR WRITES <c>PlanSheetNaming.Vocabulary</c>, IN ONE COLLECTION, SO THEY DO
/// NOT RUN AT THE SAME TIME.
///
/// That property is a public mutable STATIC — the words this office uses for a storey, a building, a
/// mezzanine — and production sets it once per run from the banked rules
/// (<c>DxfToEtabsService: PlanSheetNaming.Vocabulary = ApplyRules(...)</c>). In one process that is
/// fine. In a test run it is shared global state.
///
/// xUnit runs test CLASSES in parallel. `AnotherOfficesWordsTests` sets the vocabulary to a
/// different practice's words — BUILDING instead of BLDG, B instead of P — and restores it in
/// Dispose; `PlanSheetNamingTests` reads it expecting this office's. Run side by side, the second
/// occasionally reads the first's words mid-test and fails on a sheet name that is perfectly
/// correct.
///
/// It presents as an intermittent that passes on its own and fails in a full run, and it cost two
/// unexplained full-suite failures before anyone caught what they had in common — 29 August:
/// `ATaggedSheetOnlyMatchesItsOwnBuilding` failed in a 731-test run and passed alone immediately
/// after.
///
/// Naming the collection is the whole fix: same collection, no parallelism between them.
/// ⚠ Any NEW test class that touches that static belongs here too.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SheetNamingVocabularyCollection
{
    public const string Name = "sheet-naming vocabulary (a mutable static)";
}
