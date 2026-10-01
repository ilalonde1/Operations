using System;
using System.Configuration;
using System.IO;
using System.Reflection;

namespace Kor.Operations.NetworkOps.Agent;

/// <summary>
/// Where the server is, how to recognise it, and where the agent keeps its key and its work. From the
/// exe's .config, which the installer writes; tests override any of it on the command line.
/// </summary>
internal sealed class AgentSettings
{
    public string ServerUrl { get; private set; } = "";
    public string ServerCertSha256 { get; private set; } = "";
    public string Device { get; private set; } = Environment.MachineName;
    /// <summary>
    /// Holds the key, the work folder and the log: "data" beside the exe, i.e. C:\Program Files\KorOperations\Agent\data.
    /// Under Program Files, not ProgramData, on purpose: an ordinary user can create folders in ProgramData, so could
    /// pre-create this one before the install and own it (Codex audit 2026-09-30, finding 1). Under Program Files only
    /// an administrator can create anything. Readable by SYSTEM and Administrators only.
    /// </summary>
    public string DataDir { get; private set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
    public string KeyPath => Path.Combine(DataDir, "agent.key");
    public string WorkDir => Path.Combine(DataDir, "work");

    public static string Version { get; } =
        typeof(AgentSettings).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static AgentSettings Load(string[] args)
    {
        var s = new AgentSettings
        {
            ServerUrl = ConfigurationManager.AppSettings["ServerUrl"] ?? "",
            ServerCertSha256 = ConfigurationManager.AppSettings["ServerCertSha256"] ?? "",
        };
        for (var i = 0; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--server": s.ServerUrl = args[++i]; break;
                case "--pin": s.ServerCertSha256 = args[++i]; break;
                case "--device": s.Device = args[++i]; break;
                case "--data": s.DataDir = args[++i]; break;
            }
        }
        s.ServerUrl = s.ServerUrl.TrimEnd('/');
        s.ServerCertSha256 = s.ServerCertSha256.Replace(" ", "").Replace(":", "").ToUpperInvariant();
        if (!s.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new ConfigurationErrorsException("ServerUrl must be https://");
        if (s.ServerCertSha256.Length != 64) throw new ConfigurationErrorsException("ServerCertSha256 must be the 64-character SHA-256 of the server's certificate");
        return s;
    }
}
