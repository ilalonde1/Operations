using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Deploy;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Keeps the three places that name a fleet package in step: the deploy payload's $PkgGen and $AppVersion, the catalogue's
// V<N>.zip path, and the app's FileVersion in Kor.Operations.App/AssemblyInfo.cs. Text comparison only: it does not open the
// zip on the share, so a V<N>.zip built from other code than the stamp says would still pass here.
public sealed class DeployPackageVersionTests
{
    private static string Payload()
    {
        var asm = typeof(DeployCatalog).Assembly;
        using var s = asm.GetManifestResourceStream("Deploy.migrate-to-korops.ps1")!;
        return new StreamReader(s).ReadToEnd();
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(DeployPackageVersionTests).Assembly.Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    [Fact]
    public void The_payload_the_catalogue_and_the_app_name_the_same_package()
    {
        var payload = Payload();
        var gen = Regex.Match(payload, @"^\$PkgGen\s*=\s*(\d+)", RegexOptions.Multiline).Groups[1].Value;
        var appVersion = Regex.Match(payload, @"^\$AppVersion\s*=\s*'([^']+)'", RegexOptions.Multiline).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(gen));
        Assert.False(string.IsNullOrEmpty(appVersion));

        var op = DeployCatalog.All.Single(o => o.Key == DeployCatalog.MigrateToKorOps);
        Assert.EndsWith($@"\V{gen}.zip", op.PackageSharePath);

        var assemblyInfo = RepoFile(Path.Combine("Kor.Operations.App", "AssemblyInfo.cs"));
        Assert.Contains($"[assembly: AssemblyFileVersion(\"{appVersion}\")]", assemblyInfo);
    }
}
