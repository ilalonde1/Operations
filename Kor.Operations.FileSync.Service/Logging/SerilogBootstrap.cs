#nullable enable
using Serilog;
using Serilog.Events;

namespace Kor.Operations.FileSync.Service.Logging;

internal static class SerilogBootstrap
{
    public static Serilog.Core.Logger CreateLogger()
    {
        // Service runs under a service account, so per-user %AppData% is wrong.
        // CommonApplicationData (ProgramData) is shared across users on the host.
        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var logDirectory = Path.Combine(commonAppData, "KorOperations", "FileSync", "logs");
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Quartz", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Destructure.With<CredentialRedactingPolicy>()
            .Enrich.With<CredentialRedactingEnricher>()
            .WriteTo.Console(
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logDirectory, "filesync-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{SanitizedExceptionMessage}{NewLine}{Exception}",
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10L * 1024 * 1024)
            .CreateLogger();
    }
}
