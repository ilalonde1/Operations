#nullable enable
using System.IO;
using System.Linq;
using Kor.Operations.App.NetworkOps;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// KOR Remote opens from the app in the same page-only Edge window as Ian's "KOR Remote" shortcut (2026-10-02), not as a
/// tab in the default browser.
///
/// WHAT IT COVERS: the command line built for a URL (Edge app mode, URL intact, no shell) and the fallback when Edge is
/// absent; that no NetworkOps window opens a URL any other way.
/// WHAT IT DOES NOT: that Edge actually draws an app window, or that the page signs in. A SAME-CLASS FAULT IT WOULD NOT
/// CATCH: a URL opened from outside NetworkOps/ (e.g. another feature linking to KOR Remote) still opens a browser tab.
/// </summary>
public sealed class KorRemoteWindowTests
{
    private const string Url = "https://kor-mesh01.int.korstructural.com/?gotonode=abc%2Bdef&viewmode=11";

    [Fact]
    public void WithEdge_OpensAnAppWindowOnTheExactUrl()
    {
        var psi = KorRemoteWindow.StartInfo(Url, @"C:\Edge\msedge.exe");
        Assert.Equal(@"C:\Edge\msedge.exe", psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.Contains("--app=" + Url, psi.ArgumentList);
    }

    [Fact]
    public void WithoutEdge_FallsBackToTheDefaultBrowser()
    {
        var psi = KorRemoteWindow.StartInfo(Url, null);
        Assert.Equal(Url, psi.FileName);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void NoNetworkOpsWindowShellOpensAUrlItself()
    {
        var dir = Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(), "Kor.Operations.App", "NetworkOps");
        var offenders = Directory.GetFiles(dir, "*Window.xaml.cs")
            .Where(f => File.ReadAllText(f).Contains("UseShellExecute = true"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }
}
