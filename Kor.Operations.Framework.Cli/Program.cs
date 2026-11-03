using System.Reflection;
using Kor.Operations.Framework;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// kor.exe - verb dispatch in the netops.exe style (Kor.Operations.NetworkOps.Cli/Program.cs).
/// <c>--hook</c> mode: it prints only when it has something to say and ALWAYS exits 0 - a hook must never fail the
/// session because the command hiccupped. Without <c>--hook</c>: 0 = ok, 1 = a check failed, 2 = usage.
/// </summary>
public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var hook = args.Contains("--hook");
        var verbs = args.Where(a => a != "--hook").ToArray();
        try
        {
            return (verbs.FirstOrDefault() ?? "") switch
            {
                "--version" or "version" => Version(output),
                "ping" => PingVerb(output, hook),
                _ => hook ? 0 : Usage(error),
            };
        }
        catch (Exception e)
        {
            if (hook) return 0;
            error.WriteLine($"kor: {e.Message}");
            return 1;
        }
    }

    internal static string VersionText()
    {
        var asm = typeof(Program).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString() ?? "0";
        return $"kor {info}";
    }

    private static int Version(TextWriter output)
    {
        output.WriteLine(VersionText());
        return 0;
    }

    private static int PingVerb(TextWriter output, bool hook)
    {
        var results = Ping.Run();
        foreach (var r in results)
            output.WriteLine($"{(r.Ok ? "ok  " : "FAIL")} {r.Login}: {r.Detail}");
        return hook || results.All(r => r.Ok) ? 0 : 1;
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("usage: kor --version | kor ping [--hook]");
        return 2;
    }
}
