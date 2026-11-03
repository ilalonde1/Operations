using System.Data;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Mistake 138 - SecretGuard. Every value below is SYNTHETIC, made up for this file (none is or was a real credential).
/// Pure: each known-bad sample is found, each known-good rule text is not, Mask and ForGuardLog never carry the value.
/// LIVE (rolled back, korbrain_app): kor record, kor issue and capture-compact refuse a value with exit 1 / one line and
/// write nothing; kor start masks a value PLANTED in brain.Memory and brain.LaneState by raw SQL (the way the F7 import
/// put one there), so it never reaches a session.
/// Does NOT cover: the values SecretGuard's own summary lists as missed (short or 2-class prose values, unknown words).
/// </summary>
[Collection(LiveDb.Name)]
public class SecretGuardTests
{
    // ---- known answers -------------------------------------------------------------------------------------------------
    public static TheoryData<string, string> Bad => new()
    {
        { "Server=srv;Database=db;User Id=u;Password=Zx9vQ2mT;", "Zx9vQ2mT" },
        { "conn: ...;Pwd=Qm4tR8zL;Encrypt=true", "Qm4tR8zL" },
        { "client_secret=Ab3dEf6hIj", "Ab3dEf6hIj" },
        { "apikey=9fJk2LmN0pQr&x=1", "9fJk2LmN0pQr" },
        { "https://h/x?token=Yt5uI8oP1aSd", "Yt5uI8oP1aSd" },
        { "the actual working password for the demo box is Vt7#kLm2pQ", "Vt7#kLm2pQ" },
        { "Password: Hq3$wE9r (rotated monthly)", "Hq3$wE9r" },
        { "creds: demo-admin / Rf6!yU2i", "Rf6!yU2i" },
        { "local admin pw is Kd8%nB3x.", "Kd8%nB3x" },
        { "the secret is 7f3k9q2z8x4m1p6w5vzz", "7f3k9q2z8x4m1p6w5vzz" },
        { "the app's client secret -> 8kT~pQ2vW9xYz3LmN4bC6dF0gH1jK5sR7tU", "8kT~pQ2vW9xYz3LmN4bC6dF0gH1jK5sR7tU" },
        { "registered abc8Q~Zx7Yv6Wu5Ts4Rq3Po2Nm1Lk0Ji9Hg8Fe7 yesterday", "abc8Q~Zx7Yv6Wu5Ts4Rq3Po2Nm1Lk0Ji9Hg8Fe7" },
        { "key sk-ant-api03-ZZtestTESTtest1234567890 here", "sk-ant-api03-ZZtestTESTtest1234567890" },
        { "ghp_ABCDEFGHIJKLMNOPQRSTuvwxyz123456", "ghp_ABCDEFGHIJKLMNOPQRSTuvwxyz123456" },
        { "slack xoxb-1234567890-abcdefghij", "xoxb-1234567890-abcdefghij" },
        { "aws AKIAZ7TESTKEY0EXAMPL", "AKIAZ7TESTKEY0EXAMPL" },
        { "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9fakeFAKE", "eyJhbGciOiJIUzI1NiJ9fakeFAKE" },
        { "sqlcmd -S srv -U u -P Wq8eRt5y -Q \"SELECT 1\"", "Wq8eRt5y" },
        // review row 228: human camelCase passwords after key=, and any quoted literal, are values
        { "Password=iLoveYou", "iLoveYou" },
        { "password=\"letMeIn\"", "letMeIn" },
        { "token='summerTime'", "summerTime" },
        { "pwd=\"cancellationToken\"", "cancellationToken" },
        { "password=hunter", "hunter" },
    };

    public static TheoryData<string> Good => new()
    {
        "never print the sa password",
        "The password lives in KOR_DB_PASSWORD (user env), never in a file.",
        "set Password=<pw> in the connection string",
        "Password=**** and Pwd=xxxx are placeholders",
        "password=ChangeMe until first login",
        "pwd=$env:KOR_PW and token=${TOKEN} and secret=%APP_SECRET%",
        "the token is a GUID like 3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        "commit 5d0c81ba3c9e2f7a1b4d6e8f0a2c4e6b8d0f1a3c is the fix; its secret is nothing",
        @"the secret is in C:\KOR-Operations\secrets\app.json",
        "Redact refuses Password= / Pwd= / sk-ant- / AccountKey= markers",
        "the API key is stored in Key Vault kv-kor-prod",
        "creds: see the vault entry",
        "the password is rotated by Rotate-KorPassword.ps1",
        "password = args.Password",
        "SQLCMDPASSWORD carries the password, never -P on a command line",
        "sqlcmd -S srv -U u -P $PW",
        "the sa password is [REDACTED credential - see mistake 138]",
        "tokens: 25_000t per run; the token is `--hook`",
        @"creds for SQL: KOR\svc-sql on KOR-APP01\SQLEXPRESS",
        "the secret: per-user, Read-Only",
        "Password=ChangeMe; Pwd=password; token=none",
        "Bearer x and Bearer <token>",
        "sk-ant- keys are refused; AKIA alone is a prefix",
        "Run(token=cancellationToken, secret=default)",   // review row 227: code names are not values
    };

    [Theory]
    [MemberData(nameof(Bad))]
    public void EachKnownBadValueIsFoundExactly(string text, string value)
    {
        var hits = SecretGuard.Find(text);
        Assert.True(hits.Count > 0, "not found");
        Assert.Contains(hits, h => text.Substring(h.Index, h.Length) == value);
        var masked = SecretGuard.Mask(text)!;
        Assert.DoesNotContain(value, masked);
        Assert.Contains($"[REDACTED len={value.Length}]", masked);
        var e = Assert.Throws<SecretRefusedException>(() => SecretGuard.Refuse(text, "body"));
        Assert.DoesNotContain(value, e.Message);
        Assert.DoesNotContain('\n', e.Message);
    }

    [Theory]
    [MemberData(nameof(Good))]
    public void EachKnownGoodRuleTextIsAccepted(string text)
    {
        Assert.Empty(SecretGuard.Find(text));
        Assert.Equal(text, SecretGuard.Mask(text));
        SecretGuard.Refuse(text, "body");
    }

    [Fact]
    public void AJsonDetailIsMaskedValueByValueAndStaysJson()
    {
        // found 3 Nov by the full suite: a commit message holding "password in argv: False" inside JSON read the escaped
        // quote as part of a value, and a text mask cut the escape - the event's Detail was no longer JSON
        var clean = JsonSerializer.Serialize(new { message = "dry run prints \"password in argv: False\" and the NAME" });
        Assert.Equal(clean, SecretGuard.MaskJsonOrText(clean));
        var bad = JsonSerializer.Serialize(new { message = "the demo box password is \"Vt7#kLm2pQ\"", files = new[] { new { path = "a" } } });
        var masked = SecretGuard.MaskJsonOrText(bad)!;
        using var doc = JsonDocument.Parse(masked);
        Assert.DoesNotContain("Vt7#kLm2pQ", masked);
        Assert.Contains("[REDACTED len=10]", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("a", doc.RootElement.GetProperty("files")[0].GetProperty("path").GetString());
        Assert.DoesNotContain("Vt7#kLm2pQ", SecretGuard.MaskJsonOrText("plain text: the password is Vt7#kLm2pQ"));
    }

    [Fact]
    public void TheGuardLogTextPassesTheTablesNoSecretCheck()
    {
        foreach (var t in new[] { "Password= x", "sqlcmd Pwd=Zx9vQ2mT;", "sk-ant- marker", "Bearer abc", "PASSWORD=" })
        {
            var s = SecretGuard.ForGuardLog(t).ToLowerInvariant();
            // CK_GuardRefusal_NoSecret: NOT LIKE %password=[^[]% etc.
            foreach (var marker in new[] { "password=", "pwd=", "accountkey=", "sk-ant-", "bearer " })
                for (var i = s.IndexOf(marker, StringComparison.Ordinal); i >= 0; i = s.IndexOf(marker, i + 1, StringComparison.Ordinal))
                    Assert.True(i + marker.Length >= s.Length || s[i + marker.Length] == '[', $"'{t}' -> '{s}'");
            Assert.DoesNotContain("zx9vq2mt", s);
        }
        Assert.Equal(400, SecretGuard.ForGuardLog(new string('a', 900)).Length);
        // review row 227: a marker cut at the limit never makes the text longer than 400
        for (var n = 380; n <= 400; n++)
        {
            var s = SecretGuard.ForGuardLog(new string('a', n) + " password=Zx9vQ2mTq and more text after it");
            Assert.True(s.Length <= 400, $"{n}: {s.Length}");
            Assert.DoesNotMatch("(?i)password=[^\\[]", s);
            Assert.DoesNotContain("Zx9vQ2mTq", s);
        }
    }

    // ---- live, rolled back -------------------------------------------------------------------------------------------
    private static readonly string Lane = $"zz-test-k138-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
    private const string Planted = "Vt7#kLm2pQ";   // synthetic

    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("not inside the Operations checkout");
    }

    private static (int Code, string Out, string Err) Run(BrainDb db, string? stdin, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = Program.Run(args, () => stdin, o, e, Path.Combine(Path.GetTempPath(), $"kor-k138-{Guid.NewGuid():N}.log"), db);
        return (code, o.ToString(), e.ToString());
    }

    private static int Count(BrainDb db, string sql)
    {
        using var c = db.Command(sql);
        c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = Lane;
        return Convert.ToInt32(c.ExecuteScalar());
    }

    [Fact]
    public void LiveKorRecordRefusesAValueInOneLineAndWritesNothing()
    {
        using var db = BrainDb.OpenRolledBack();
        var before = Count(db, "SELECT COUNT(*) FROM brain.Memory WHERE Lane = @l");
        var events = Count(db, "SELECT COUNT(*) FROM brain.Event WHERE Module = @l");
        var (code, o, e) = Run(db, null, "record", "--module", Lane, "--kind", "finding", "--name", "k138-bad",
                               "--says", $"the demo box password is {Planted}", "--source", "git:docs/KNOWLEDGE-IDE.md",
                               "--dir", RepoLocator.Find("Framework", RepoDir())!);
        Assert.Equal(1, code);
        Assert.Equal("", o);
        Assert.Single(e.Trim().Split('\n'));
        Assert.Contains("credential value", e);
        Assert.DoesNotContain(Planted, e);
        Assert.Equal(before, Count(db, "SELECT COUNT(*) FROM brain.Memory WHERE Lane = @l"));
        Assert.Equal(events, Count(db, "SELECT COUNT(*) FROM brain.Event WHERE Module = @l"));
    }

    [Fact]
    public void LiveKorIssueRefusesAValue()
    {
        using var db = BrainDb.OpenRolledBack();
        var before = Count(db, "SELECT COUNT(*) FROM brain.Memory WHERE Lane = @l");
        var (code, _, e) = Run(db, null, "issue", "--module", Lane, "--name", "k138-issue",
                               "--description", $"login works with Password={Planted}", "--check", "query",
                               "--check-ref", "brain:SELECT 1 AS ok");
        Assert.Equal(1, code);
        Assert.DoesNotContain(Planted, e);
        Assert.Equal(before, Count(db, "SELECT COUNT(*) FROM brain.Memory WHERE Lane = @l"));
    }

    [Fact]
    public void LiveCaptureCompactRefusesASummaryHoldingAValue()
    {
        using var db = BrainDb.OpenRolledBack();
        var before = Count(db, "SELECT COUNT(*) FROM brain.LaneState WHERE Lane = @l");
        var stdin = JsonSerializer.Serialize(new
        {
            session_id = "zz-k138", transcript_path = "C:/x/k138.jsonl", cwd = RepoDir(), hook_event_name = "PostCompact",
            trigger = "manual", compact_summary = $"We logged in; the admin creds: demo / {Planted}. Next: K7.",
        });
        var (code, o, e) = Run(db, stdin, "capture-compact", "--module", Lane);
        Assert.Equal(1, code);
        Assert.DoesNotContain(Planted, o + e);
        Assert.Equal(before, Count(db, "SELECT COUNT(*) FROM brain.LaneState WHERE Lane = @l"));
        // hook mode: exit 0 (a hook never fails the session), still nothing written
        var (hc, ho, _) = Run(db, stdin, "capture-compact", "--module", Lane, "--hook");
        Assert.Equal(0, hc);
        Assert.DoesNotContain(Planted, ho);
        Assert.Equal(before, Count(db, "SELECT COUNT(*) FROM brain.LaneState WHERE Lane = @l"));
    }

    [Fact]
    public void LiveKorStartMasksAValuePlantedByRawSqlInARecordAndAResume()
    {
        using var db = BrainDb.OpenRolledBack();
        var g = RecordStore.AtHead(RepoLocator.Find("Framework", RepoDir())!, "docs/KNOWLEDGE-IDE.md");
        // planted the way the F7 import did: straight into the table, past every kor check
        using (var c = db.Command(
                   "INSERT brain.Memory (Lane, Name, Kind, Description, Body, CreatedBy, SourceKind, SourceRepo, SourceCommit, SourcePath, SourceBlob) " +
                   "VALUES (@l, N'k138-planted', N'finding', @d, N'b', N'k138-test', N'git', @r, @c, @p, @b); " +
                   "INSERT brain.LaneState (Lane, ResumeBlock, WrittenBy) VALUES (@l, @rb, N'k138-test');"))
        {
            c.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = Lane;
            c.Parameters.Add("@d", SqlDbType.NVarChar, 400).Value = $"the actual working password for the demo box is {Planted}";
            c.Parameters.Add("@r", SqlDbType.NVarChar, 80).Value = g.Repo;
            c.Parameters.Add("@c", SqlDbType.Char, 40).Value = g.Commit;
            c.Parameters.Add("@p", SqlDbType.NVarChar, 400).Value = g.Path;
            c.Parameters.Add("@b", SqlDbType.Char, 40).Value = g.Blob;
            c.Parameters.Add("@rb", SqlDbType.NVarChar, -1).Value = $"STATE: resumed. local admin pw is {Planted} on the demo box.";
            c.ExecuteNonQuery();
        }
        Assert.Equal(1, Count(db, $"SELECT COUNT(*) FROM brain.Memory WHERE Lane = @l AND Description LIKE N'%{Planted}%'"));
        var (code, text, err) = Run(db, null, "start", "--module", Lane, "--part", "record", "--text", "--cache-dir",
                                    Path.Combine(Path.GetTempPath(), $"kor-k138-cache-{Guid.NewGuid():N}"));
        Assert.True(code == 0, err);
        Assert.Contains("k138-planted", text);
        Assert.DoesNotContain(Planted, text);
        Assert.True(text.Split($"[REDACTED len={Planted.Length}]").Length - 1 >= 2, "both the record and the resume are masked");
        // the hook's JSON too
        var (hc, json, _) = Run(db, "{}", "start", "--module", Lane, "--part", "record", "--hook", "--cache-dir",
                                Path.Combine(Path.GetTempPath(), $"kor-k138-cache-{Guid.NewGuid():N}"));
        Assert.Equal(0, hc);
        Assert.DoesNotContain(Planted, json);
        // and kor memory's read-back
        var (_, mem, _) = Run(db, null, "memory", "--module", Lane);
        Assert.Contains("k138-planted", mem);
        Assert.DoesNotContain(Planted, mem);
    }

    [Fact]
    public void LiveAnEventIsMaskedNeverRefused()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = BrainStore.Insert(new BrainEvent(Lane, "build", null, null, "dotnet build", "pass",
                                                  $"output: the demo box password is {Planted}", "k138-test"), db);
        var ev = BrainStore.EventById(id, db)!;
        Assert.DoesNotContain(Planted, ev.Detail);
        Assert.Contains($"[REDACTED len={Planted.Length}]", ev.Detail);
    }
}
