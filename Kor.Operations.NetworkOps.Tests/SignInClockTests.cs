using Kor.Operations.NetworkOps.Core.Actions;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The early warning Ian chose on 28 Oct (13:4x) after the sign-in lapsed mid-afternoon and blocked the fleet.
// Known-bad first: an MFA 11 h ago is warned about NOW (1 h left), not discovered as exit 4 mid-run.
// COVERS: the judge at every boundary, the file round trip, a missing / corrupt file, and that both interactive
// sign-in roads (CLI AppServer, the app's NetworkOpsClient) record the clock. DOES NOT COVER: an MFA done outside those
// two roads (the clock is then early, never late -- stated in SignInClock), or Entra ending a session early.
public class SignInClockTests
{
    private static readonly DateTimeOffset Mfa = new(2026, 10, 28, 13, 2, 51, TimeSpan.FromHours(-7));
    private static SignInClock.Mark Mark => new(Mfa, "netops cli");

    [Fact]
    public void Known_bad_an_mfa_eleven_hours_ago_is_warned_about_now()
    {
        var r = SignInClock.Judge(Mark, Mfa.AddHours(11), TimeSpan.FromHours(12), TimeSpan.FromHours(2));
        Assert.Equal(SignInClock.State.Soon, r.State);
        Assert.Contains("lapses at", r.Line);
        Assert.Contains("in 1h 00m", r.Line);
        Assert.Contains("netops token", r.Line);
        Assert.Equal(Mfa.AddHours(12), r.LapsesAt);
    }

    [Theory]
    [InlineData(0, SignInClock.State.Ok)]
    [InlineData(9.99, SignInClock.State.Ok)]
    [InlineData(10, SignInClock.State.Soon)]       // exactly 2 h left: warn
    [InlineData(11.99, SignInClock.State.Soon)]
    [InlineData(12, SignInClock.State.Lapsed)]     // at the lapse: gone
    [InlineData(30, SignInClock.State.Lapsed)]
    public void The_state_at_each_boundary(double hoursAfterMfa, SignInClock.State expected)
        => Assert.Equal(expected, SignInClock.Judge(Mark, Mfa.AddHours(hoursAfterMfa), TimeSpan.FromHours(12), TimeSpan.FromHours(2)).State);

    [Fact]
    public void Nothing_recorded_is_unknown_and_says_so()
    {
        var r = SignInClock.Judge(null, Mfa);
        Assert.Equal(SignInClock.State.Unknown, r.State);
        Assert.Null(r.LapsesAt);
        Assert.Contains("not known", r.Line);
    }

    [Fact]
    public void The_file_round_trips_and_a_missing_or_corrupt_file_reads_as_nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "signin-clock-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var path = Path.Combine(dir, "last-mfa.json");
            Assert.Null(SignInClock.Read(path));
            SignInClock.Record("KOR Operations app", Mfa, path);
            var m = SignInClock.Read(path);
            Assert.NotNull(m);
            Assert.Equal(Mfa, m!.At);
            Assert.Equal("KOR Operations app", m.Via);
            Assert.DoesNotContain("token", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(path, "{not json");
            Assert.Null(SignInClock.Read(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    // Source check (the app project is not referenced by these tests): each interactive NetworkOps sign-in records the
    // clock. It would NOT catch a third interactive road added elsewhere.
    [Fact]
    public void Both_interactive_sign_in_roads_record_the_clock()
    {
        var root = Repo.Root;
        var cli = File.ReadAllText(Path.Combine(root, "Kor.Operations.NetworkOps.Cli", "AppServer.cs"));
        Assert.Contains("SignInClock.Record(\"netops cli\")", cli);
        var app = File.ReadAllText(Path.Combine(root, "Kor.Operations.App", "NetworkOps", "NetworkOpsClient.cs"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(app, @"return Signed\(\(await [^;]*AcquireTokenInteractive|return Signed\(\(await interactive\.ExecuteAsync").Count);
        Assert.Contains("SignInClock.Record(\"KOR Operations app\")", app);
    }

    private static class Repo
    {
        public static string Root
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Agent"))) dir = dir.Parent;
                return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
            }
        }
    }
}
