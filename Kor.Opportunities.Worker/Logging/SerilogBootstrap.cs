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
                // Roll to opportunities-yyyyMMdd_001.log at the cap instead of stopping: without it every day's log went silent
                // once it reached 10 MB (18:49 on 2026-10-19, 8 of 8 days checked), blind through the nightly award imports.
                // The count limit now counts those extra files too, so it is raised to keep about a month.
                retainedFileCountLimit: 90,
                fileSizeLimitBytes: 10L * 1024 * 1024,
                rollOnFileSizeLimit: true)
            .CreateLogger();
    }
}
