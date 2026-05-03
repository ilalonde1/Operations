#nullable enable
using Serilog;
using Serilog.Events;

namespace Kor.Opportunities.Worker.Logging;

internal static class SerilogBootstrap
{
    public static Serilog.Core.Logger CreateLogger()
    {
        // ProgramData (machine-scope) — service runs as a service account that does NOT
        // have a user profile loaded, so %AppData% is unavailable. Mirrors FileSync.Service.
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var logDir = Path.Combine(programData, "KorOperations", "Opportunities", "logs");
        Directory.CreateDirectory(logDir);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Quartz", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logDir, "opportunities-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10L * 1024 * 1024)
            .CreateLogger();
    }
}
