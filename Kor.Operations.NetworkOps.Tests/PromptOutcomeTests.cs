#nullable enable
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// PromptVerdict.ResolvesFinding: the rule that ties a session's verdict to the finding it was about. Accepting a session
// whose verdict RESOLVES the finding closes it in the same step (NetworkOpsStore.DecideLearnedAsync); a verdict that does
// not leaves it open. This is the one place that decides which verdicts close a finding.
//
// WHAT IT COVERS: the four outcome values a session can report (the PromptComposer vocabulary) map to close/keep exactly;
// case-insensitive; an unknown or missing verdict never closes a finding.
// WHAT IT DOES NOT: that DecideLearnedAsync actually runs the acknowledge UPDATE (that is SQL, proven against APP01), nor
// which finding the run was about. A same-class fault it would NOT catch: a verdict string that drifts (e.g. "resolved"
// instead of "solved") would silently stop closing -- the vocabulary lives in PromptComposer and must stay in step.
public sealed class PromptOutcomeTests
{
    [Theory]
    [InlineData("solved", true)]       // fixed -> close it
    [InlineData("no-action", true)]    // nothing real to fix (stale/false) -> close it
    [InlineData("partly", false)]      // still a live problem
    [InlineData("not-solved", false)]  // still a live problem
    [InlineData("SOLVED", true)]       // case-insensitive
    [InlineData("No-Action", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("something-else", false)]
    public void Only_solved_and_no_action_resolve_the_finding(string? outcome, bool resolves)
        => Assert.Equal(resolves, PromptVerdict.ResolvesFinding(outcome));
}
