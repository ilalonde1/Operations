using System.Reflection;
using Serilog;

namespace Kor.Operations.Mcp;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // UseWindowsService is a no-op when run from console (dev),
        // and registers the service lifetime when launched by SCM (prod).
        builder.Host.UseWindowsService(o => o.ServiceName = "Kor.Operations.Mcp");

        builder.Host.UseSerilog((ctx, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext());

        var app = builder.Build();

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        // /health returns 200 with version + timestamp so the deploy
        // runbook can verify a redeploy by `curl https://.../health`.
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "Kor.Operations.Mcp",
            version,
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
        }));

        Log.Information("Kor.Operations.Mcp {Version} starting up.", version);
        app.Run();
    }
}
