#nullable enable
using System.Net.Mail;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.NetworkOps.Cli;

// `netops watchdog`: the dead-man switch, run every 10 minutes by a scheduled task on KOR-FS01 --
// a different machine from the service it watches. It reads the newest heartbeat, decides with
// Core.Health.Watchdog (tested there), and mails by Microsoft 365 direct send: SMTP to the tenant's
// MX on port 25, no mail credentials on FS01 at all.
//
//   netops watchdog [--to addr] [--silent-min 15] [--remind-min 60] [--db-env KOR_NETWORKOPS_WATCHDB]
//                   [--dry-run]   decide and log, print the mail instead of sending it
//                   [--test]      send a test mail now, whatever the state (proves the mail path)
//
// The database connection is a MACHINE environment variable (the task runs as SYSTEM); never an
// argument, so it is never in the task's command line. State and a daily log live in
// %ProgramData%\KorOperations\NetworkOps\watchdog.
internal static class WatchdogVerb
{
    private const string MxHost = "korstructural-com.mail.protection.outlook.com";
    private const string From = "networkops@korstructural.com";

    public static async Task<int> RunAsync(string[] args)
    {
        var to = "ilalonde@korstructural.com";
        var dbEnv = "KOR_NETWORKOPS_WATCHDB";
        var silent = TimeSpan.FromMinutes(15);   // the service beats every 60 s; 15 min rides out a restart or deploy
        var remind = TimeSpan.FromMinutes(60);
        bool dryRun = false, test = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--to" when i + 1 < args.Length: to = args[++i]; break;
                case "--db-env" when i + 1 < args.Length: dbEnv = args[++i]; break;
                case "--silent-min" when i + 1 < args.Length && int.TryParse(args[i + 1], out var s) && s > 0: silent = TimeSpan.FromMinutes(s); i++; break;
                case "--remind-min" when i + 1 < args.Length && int.TryParse(args[i + 1], out var r) && r > 0: remind = TimeSpan.FromMinutes(r); i++; break;
                case "--dry-run": dryRun = true; break;
                case "--test": test = true; break;
                default: Console.Error.WriteLine($"Unknown argument: {args[i]}"); return 2;
            }
        }

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KorOperations", "NetworkOps", "watchdog");
        Directory.CreateDirectory(dir);
        var statePath = Path.Combine(dir, "state.json");
        var logPath = Path.Combine(dir, $"watchdog-{DateTime.Now:yyyyMMdd}.log");
        void Log(string line)
        {
            var stamped = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {line}";
            Console.WriteLine(stamped);
            File.AppendAllText(logPath, stamped + Environment.NewLine);
        }

        var reading = await ReadAsync(dbEnv).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        if (test)
        {
            var body = $"This is a TEST from the NetworkOps dead-man watcher on {Environment.MachineName}. If you are reading it in your inbox (not Junk), "
                     + $"real alerts will reach you the same way.\r\n\r\nWhat it sees right now: {Describe(reading, now)}.";
            return Send("[TEST] NetworkOps dead-man watcher", body, to, dryRun, Log) ? 0 : 1;
        }

        var state = LoadState(statePath);
        var outcome = Watchdog.Decide(reading, state, now, silent, remind, Environment.MachineName);
        Log(outcome.LogLine);

        if (outcome.Action != WatchdogAction.None && !Send(outcome.Subject!, outcome.Body!, to, dryRun, Log))
            return 1;   // state NOT advanced: the next run tries the same message again
        try
        {
            SaveState(statePath, outcome.State);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Never an unhandled crash: a scheduled task that throws fails silently. Logged, and the
            // exit code shows in the task's Last Run Result; the cost is a possible repeat alert.
            Log($"STATE NOT SAVED ({ex.GetType().Name}: {ex.Message}); the next run may repeat this alert");
            return 1;
        }
        return 0;
    }

    private static async Task<HeartbeatReading> ReadAsync(string dbEnv)
    {
        var cs = Environment.GetEnvironmentVariable(dbEnv, EnvironmentVariableTarget.Machine) ?? Environment.GetEnvironmentVariable(dbEnv);
        if (string.IsNullOrWhiteSpace(cs))
            return new HeartbeatReading(null, null, null, $"{dbEnv} is not set on {Environment.MachineName}, so the watcher has nothing to read");
        try
        {
            var b = new SqlConnectionStringBuilder(cs) { ConnectTimeout = 20 };
            await using var con = new SqlConnection(b.ConnectionString);
            await con.OpenAsync().ConfigureAwait(false);
            await using var cmd = new SqlCommand("SELECT TOP (1) Host, LastBeatUtc, ServiceVersion FROM NetworkOps.ServiceHeartbeat ORDER BY LastBeatUtc DESC;", con) { CommandTimeout = 20 };
            await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await r.ReadAsync().ConfigureAwait(false)) return new HeartbeatReading(null, null, null, null);
            return new HeartbeatReading(r.GetString(0), DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc), r.IsDBNull(2) ? null : r.GetString(2), null);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            return new HeartbeatReading(null, null, null, ex.Message);
        }
    }

    private static bool Send(string subject, string body, string to, bool dryRun, Action<string> log)
    {
        if (dryRun)
        {
            log($"DRY RUN, not sent: {subject}");
            Console.WriteLine($"--- to {to}, from {From}{Environment.NewLine}{subject}{Environment.NewLine}{body}{Environment.NewLine}---");
            return true;
        }
        try
        {
            // Direct send: the tenant's own MX on 25 with STARTTLS; it accepts mail for our own domain
            // without authentication. Where it lands depends on SPF (docs/KOR-NetworkOps-Design §7).
            using var smtp = new SmtpClient(MxHost, 25) { EnableSsl = true, Timeout = 30_000, DeliveryMethod = SmtpDeliveryMethod.Network };
            using var msg = new MailMessage(new MailAddress(From, "NetworkOps watcher"), new MailAddress(to)) { Subject = subject, Body = body };
            smtp.Send(msg);
            log($"sent: {subject}");
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            log($"SEND FAILED ({subject}): {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string Describe(HeartbeatReading r, DateTime now)
        => r.ReadError ?? (r.LastBeatUtc is { } b ? $"last heartbeat {(int)(now - b).TotalSeconds} s ago from {r.ServiceHost} ({r.Version})" : "no heartbeat recorded");

    private static WatchdogState LoadState(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<WatchdogState>(File.ReadAllText(path)) ?? WatchdogState.Fresh : WatchdogState.Fresh; }
        catch (JsonException) { return WatchdogState.Fresh; }   // a damaged file must not stop the watcher; worst case one repeat alert
    }

    private static void SaveState(string path, WatchdogState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state));
        File.Move(tmp, path, overwrite: true);
    }
}
