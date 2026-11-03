using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Every test class that writes to the live KorStandards inside a rolled-back transaction runs in THIS collection, one
/// class at a time. Migration 171's function CHECKs (fn_OneOpenIssueOk, fn_IssueCloseOk ...) read brain.Memory, so two
/// open test transactions inserting there can deadlock each other - seen 3 Nov: the restate test was chosen as the victim
/// while another class held its uncommitted rows. Production writers hold a transaction for one verb; tests hold one for
/// a whole test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveDb
{
    public const string Name = "live KorStandards (rolled back)";
}
