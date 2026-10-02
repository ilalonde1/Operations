#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Bios;
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The BIOS update: "A newer BIOS is available" and the two fixes behind it (check, update). Built 2026-10-02 for the PCs
// behind Lenovo's catalog. Fixtures = Lenovo's catalogs and BIOS descriptors as served that day for the P340 (30DK), P3
// (30GS), P360 (30FM) and the ThinkPad T16 (21QE), each checked against its catalog's SHA-256 when saved.
//
// The first version flattened Lenovo's rules into "any listed level" and the dry run on KOR-1001 caught it: the ThinkPad
// package EXCLUDES levels (Not) and detects "installed" by a UEFI firmware version, so a current ThinkPad read as behind.
// The rules are now evaluated three-valued, in C# (the finding) and in PowerShell (the fix, on the PC) -- and the
// differential below runs BOTH over every fixture and many BIOS levels and requires the same verdict.
//
// WHAT IT COVERS: Lenovo's rule language as KOR's packages use it (And/Or/Not/_Bios/_OS/_CPUAddressWidth; anything else
// Unknown); the BIOS ID taken from SMBIOSBIOSVersion; the verdict across a catalog's packages; the finding; that an
// unread catalog leaves the finding as it was; that the C# and the PowerShell agree; that the update script verifies
// everything BEFORE anything changes, suspends BitLocker for exactly one restart and puts it back when the flash fails,
// and runs only Lenovo's own commands from the package folder; that the check is the same script stopping early.
// WHAT IT DOES NOT: a real flash -- that happens only on a PC Ian picks; "Check the BIOS update" runs the rest for real
// (done 2026-10-02 on 217, 206-N, 1001). Device and firmware checks (_PnPID, _Firmware) are never evaluated: a package
// that needs them is Unknown, so no finding and no flash. A SAME-CLASS FAULT IT WOULD NOT CATCH: a construct BOTH sides
// get wrong the same way (the differential only proves they agree) -- the spot checks against Lenovo's intent below are
// the guard there, and they cover the four types KOR has, not every Lenovo package.
public sealed class BiosTests
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "bios");
    private static LenovoBiosPackage Pkg(string file) => LenovoBiosPackage.Parse(File.ReadAllText(Path.Combine(Dir, file)), "https://download.lenovo.com/x/" + file);
    private static readonly string[] Descriptors = ["s08jy62usa_2_.xml", "s0ijy7eusa_2_.xml", "s0ejy64usa_2_.xml", "n4juj04w_2_.xml", "n4juj13w_2_.xml"];

    private static InventoryInfo Inv(string? bios) => new("LENOVO", "ThinkStation P340", "30DKS0QN00", "S1", "LENOVO", "1046", bios, "2024-01-01",
        "Intel", 8, 32, null, null, null, null);

    [Fact]
    public void Lenovos_descriptor_is_read_as_Lenovo_wrote_it()
    {
        var p = Pkg("s08jy62usa_2_.xml");
        Assert.Equal(("s08jy62usa", "S08KT62A", "s08jy62usa.exe"), (p.Id, p.Version, p.Installer));
        Assert.Equal("E647323F63D5F022E16F76845EE68EF963D4C4D25771D4F990C2E65CA2E239E4", p.Sha256);
        Assert.Equal(new DateTime(2026, 7, 10), p.Released);
    }

    [Fact]
    public void The_catalog_names_the_BIOS_package_and_nothing_else()
        => Assert.Equal(["https://download.lenovo.com/pccbbs/thinkcentre_bios/s08jy62usa_2_.xml"], LenovoCatalog.BiosLocations(File.ReadAllText(Path.Combine(Dir, "30DK_Win11.xml"))));

    [Theory]
    [InlineData("S08KT62A (1.62 )", "S08KT62A")]
    [InlineData("N4JET28W (1.18 )", "N4JET28W")]
    [InlineData("  S0IKT7CA ", "S0IKT7CA")]
    public void The_BIOS_ID_is_the_first_word(string smbios, string id) => Assert.Equal(id, BiosFacts.IdOf(smbios));

    [Theory]
    // P340: Lenovo installs S08KT62A over S08KT1*..S08KT61*
    [InlineData("s08jy62usa_2_.xml", "S08KT5EA", true, BiosVerdict.Behind)]     // KOR-101/104N/206/217/308 on 2026-10-01
    [InlineData("s08jy62usa_2_.xml", "S08KT62A", true, BiosVerdict.Current)]
    [InlineData("s08jy62usa_2_.xml", "S08KT70A", true, BiosVerdict.NotApplicable)]   // newer than Lenovo lists: never "behind"
    [InlineData("s08jy62usa_2_.xml", "M1AKT55A", true, BiosVerdict.NotApplicable)]   // another family
    // P3: S0IKT7EA over S0IKT7CA (KOR-206-N, 208-N, 218N)
    [InlineData("s0ijy7eusa_2_.xml", "S0IKT7CA", true, BiosVerdict.Behind)]
    [InlineData("s0ijy7eusa_2_.xml", "S0IKT7EA", true, BiosVerdict.Current)]
    // ThinkPad T16 (KOR-1001): the older package excludes nothing it does not list; the newer one is firmware-detected
    [InlineData("n4juj04w_2_.xml", "N4JET17W", true, BiosVerdict.Behind)]
    [InlineData("n4juj04w_2_.xml", "N4JET28W", true, BiosVerdict.NotApplicable)]
    [InlineData("n4juj13w_2_.xml", "N4JET28W", true, BiosVerdict.Unknown)]      // the bug: was "behind"
    public void The_verdict_is_Lenovos(string descriptor, string biosId, bool win11, BiosVerdict expected)
        => Assert.Equal(expected, Pkg(descriptor).Judge(new BiosFacts(biosId, win11)));

    [Fact]
    public void KOR_1001s_catalog_says_nothing_rather_than_a_wrong_behind()
    {
        var packages = LenovoCatalog.BiosLocations(File.ReadAllText(Path.Combine(Dir, "21QE_Win11.xml"))).Select(u => Pkg(u[(u.LastIndexOf('/') + 1)..])).ToList();
        Assert.Equal(BiosVerdict.Unknown, LenovoCatalog.Judge(packages, new BiosFacts("N4JET28W", true)).Verdict);
        Assert.Empty(BiosRules.Evaluate(Inv("N4JET28W (1.18 )"), windows11: true, packages));
    }

    [Fact]
    public void A_PC_behind_gets_one_finding_saying_from_what_to_what()
    {
        var p340 = Pkg("s08jy62usa_2_.xml");
        var f = Assert.Single(BiosRules.Evaluate(Inv("S08KT5EA"), true, [p340]));
        Assert.Equal((BiosRules.Rule, Severity.Info), (f.RuleKey, f.Severity));   // Lenovo rates it "recommended" (2)
        Assert.StartsWith("S08KT5EA -> S08KT62A (Lenovo, 2026-07-10)", f.Evidence);
        Assert.Empty(BiosRules.Evaluate(Inv("S08KT62A"), true, [p340]));
        Assert.Empty(BiosRules.Evaluate(Inv(null), true, [p340]));
        Assert.Empty(BiosRules.Evaluate(null, true, [p340]));
        Assert.Equal(Severity.Warning, Assert.Single(BiosRules.Evaluate(Inv("S08KT5EA"), true, [p340 with { Severity = 1 }])).Severity);
    }

    [Theory]
    [InlineData("LENOVO", "30DKS0QN00", "30DK")]
    [InlineData("Lenovo", "21QES1AB00", "21QE")]
    [InlineData("System manufacturer", "System Product Name", null)]
    [InlineData("LENOVO", "30", null)]
    [InlineData(null, "30DKS0QN00", null)]
    public void A_PC_is_judged_by_its_Lenovo_machine_type(string? maker, string? product, string? expected)
        => Assert.Equal(expected, LenovoCatalog.MachineTypeOf(maker, product));

    /// <summary>The differential: the PC's PowerShell and the service's C# give the same verdict on every case.</summary>
    [Fact]
    public void The_PC_and_the_service_give_Lenovos_rules_the_same_answer()
    {
        var levels = new[] { "S08KT1AA", "S08KT5EA", "S08KT61A", "S08KT62A", "S08KT70A", "S0IKT7CA", "S0IKT7EA", "S0IKT80A", "S0EKT63A", "S0EKT64A",
            "N4JET05W", "N4JET17W", "N4JET18W", "N4JET19W", "N4JET28W", "M1AKT55A", "" };
        var cases = (from d in Descriptors from b in levels from w in new[] { true, false } select new { File = Path.Combine(Dir, d), BiosId = b, Windows11 = w }).ToList();

        var script = FixCatalog.Script(FixCatalog.Get(FixCatalog.UpdateBios)!, null);
        var rules = script[..script.IndexOf("# ==== end of Lenovo's rules ====", StringComparison.Ordinal)];
        var work = Path.Combine(Path.GetTempPath(), "kor-bios-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            File.WriteAllText(Path.Combine(work, "cases.json"), JsonSerializer.Serialize(cases));
            File.WriteAllText(Path.Combine(work, "run.ps1"), rules + """

                $ErrorActionPreference = 'Stop'
                $cases = Get-Content -Raw (Join-Path $PSScriptRoot 'cases.json') | ConvertFrom-Json
                $out = @(foreach ($c in $cases) {
                    $x = [xml]([IO.File]::ReadAllText($c.File).TrimStart([char]0xFEFF))
                    $f = [pscustomobject]@{ BiosId = $c.BiosId; Windows11 = [bool]$c.Windows11; AddressWidth = 64 }
                    # Each rule's own value as well as the verdict: a verdict can hide a wrong sub-rule (see the test).
                    $di = $x.Package.SelectSingleNode('DetectInstall'); $dep = $x.Package.SelectSingleNode('Dependencies')
                    "$(if ($di) { Test-LenovoNode $di $f } else { '-' })|$(if ($dep) { Test-LenovoNode $dep $f } else { '-' })|$(Get-LenovoVerdict $x.Package $f)"
                })
                [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'out.json'), (ConvertTo-Json -InputObject $out -Compress))
                """);
            var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            using var proc = Process.Start(new ProcessStartInfo(ps, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(work, "run.ps1")}\"")
                { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false })!;
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120_000);
            Assert.True(proc.ExitCode == 0 && File.Exists(Path.Combine(work, "out.json")), "the PowerShell rules failed: " + err);

            var fromPc = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(work, "out.json")))!;
            Assert.Equal(cases.Count, fromPc.Length);
            string CSharp(string file, BiosFacts f)
            {
                var p = Pkg(file);
                return $"{(p.DetectInstall is { } di ? LenovoRules.Eval(di, f).ToString() : "-")}|{(p.Dependencies is { } dep ? LenovoRules.Eval(dep, f).ToString() : "-")}|{p.Judge(f)}";
            }
            var differ = cases.Select((c, i) => (c, cs: CSharp(Path.GetFileName(c.File), new BiosFacts(c.BiosId, c.Windows11)), ps: fromPc[i]))
                .Where(x => x.cs != x.ps).Select(x => $"{Path.GetFileName(x.c.File)} {x.c.BiosId} win11={x.c.Windows11}: C# {x.cs}, PC {x.ps}").ToList();
            Assert.True(differ.Count == 0, $"{differ.Count} of {cases.Count} cases differ (detect|dependencies|verdict):\n" + string.Join("\n", differ));
            // Not a vacuous agreement: the cases reach every verdict, and every value of each rule.
            Assert.Equal(4, fromPc.Select(s => s.Split('|')[2]).Distinct().Count());
            Assert.Equal(3, fromPc.Select(s => s.Split('|')[1]).Distinct().Count());
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } }
    }

    [Fact]
    public void An_unread_catalog_leaves_the_BIOS_finding_exactly_as_it_was()
    {
        var sweep = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.NetworkOps.Service", "Sweep", "HealthSweeper.cs"));
        Assert.Contains("biosPackages is null ? [] : Core.Bios.BiosRules.Evaluate(", sweep);
        Assert.Contains("(biosPackages is not null || f.RuleKey != Core.Bios.BiosRules.Rule)", sweep);
    }

    [Fact]
    public void Nothing_changes_on_the_PC_until_everything_is_verified()
    {
        var s = FixCatalog.Script(FixCatalog.Get(FixCatalog.UpdateBios)!, null);
        int At(string text) { var i = s.IndexOf(text, StringComparison.Ordinal); Assert.True(i >= 0, $"update-bios.ps1 no longer contains: {text}"); return i; }

        var verified = new[]
        {
            At("does not match the SHA-256 its catalog gives"),        // catalog -> descriptor
            At("is not the one Lenovo's descriptor gives"),            // descriptor -> installer
            At("is not validly signed by Lenovo"),                     // Authenticode
            At("by a device or firmware check this fix does not read"),
            At("On battery"),
            At("A BIOS password is set"),
        };
        var dryRunStop = At("if ($DryRun) { return");
        var suspend = At("-protectors -disable $env:SystemDrive -RebootCount 1");
        var flash = At("Start-Process -FilePath $in[0]");
        var restart = At("shutdown.exe /r /t 300");
        Assert.All(verified, v => Assert.True(v < dryRunStop, "a check comes after the dry-run stop: the check would not prove it"));
        Assert.True(dryRunStop < suspend && suspend < flash && flash < restart, "the order is verify, stop-if-dry-run, suspend BitLocker, flash, restart");
        Assert.True(At("-protectors -enable $env:SystemDrive") > flash, "BitLocker is not put back when the flash fails");
        At("runs something outside the package folder");
        At("uses a variable this fix does not know");
    }

    [Fact]
    public void The_check_is_the_update_stopping_before_any_change()
    {
        var update = FixCatalog.Script(FixCatalog.Get(FixCatalog.UpdateBios)!, null);
        Assert.Equal("$DryRun = $true\n" + update, FixCatalog.Script(FixCatalog.Get(FixCatalog.CheckBiosUpdate)!, null));
        Assert.False(FixCatalog.Get(FixCatalog.CheckBiosUpdate)!.Disruptive);
        Assert.True(FixCatalog.Get(FixCatalog.UpdateBios)!.Disruptive);
        Assert.Equal([FixCatalog.CheckBiosUpdate, FixCatalog.UpdateBios], FixCatalog.For(BiosRules.Rule).Take(2).Select(f => f.Id));
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
