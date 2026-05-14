#nullable enable
using System.Linq;
using Kor.Operations.App.FileSync;
using Kor.Operations.Services;
using Xunit;

namespace Kor.Operations.Tests.FileSync;

/// <summary>
/// Locks the explicit exclusion rule from feedback_filesync_excluded_from_ai.md:
/// FileSync Command Center is out of AI scope, its VM must never be
/// registered with AppAiContextBuilder, and (post-Batch 104) it must not
/// even implement <see cref="IAiContextProvider"/> — having the impl
/// dormant lets a future accidental Register call silently put FileSync
/// data into AI prompts without any code review noticing.
/// </summary>
public sealed class FileSyncExcludedFromAiTests
{
    [Fact]
    public void FileSyncCommandCenterViewModel_DoesNotImplementIAiContextProvider()
    {
        var vmType = typeof(FileSyncCommandCenterViewModel);
        var interfaces = vmType.GetInterfaces();

        Assert.DoesNotContain(typeof(IAiContextProvider), interfaces);
    }
}
