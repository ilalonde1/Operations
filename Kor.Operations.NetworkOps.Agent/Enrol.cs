using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Security.Principal;

namespace Kor.Operations.NetworkOps.Agent;

[DataContract]
internal sealed class EnrolRequest
{
    [DataMember(Name = "device")] public string Device = "";
    [DataMember(Name = "code")] public string Code = "";
}

[DataContract]
internal sealed class EnrolAnswer
{
    [DataMember(Name = "key")] public string Key = "";
}

/// <summary>
/// A PC that is not in the domain installs its own agent (Ian, 2026-10-02: the Boardroom PC -- "It's not domain joined"):
///   Kor.Operations.NetworkOps.Agent.exe --enrol CODE        run once, as an administrator, from the downloaded package
/// The code is the one-time enrolment code the Command Center issued for this PC (good for an hour, once). The agent
/// trades it for the PC's key at APP01 (the same pinned certificate it always talks to), then does on this PC exactly
/// what APP01 does over the network for a domain PC (Transport/RemoteAgentInstall): stop any agent, copy the files to
/// C:\Program Files\KorOperations\Agent, lock the data folder, write the key as a new file, create the auto-start service
/// as LocalSystem, start it. No password is asked for or kept anywhere: the code is the only secret, and it is spent.
/// </summary>
internal static class Enrol
{
    public const string InstallDir = @"C:\Program Files\KorOperations\Agent";
    private const string DisplayName = "KOR NetworkOps Agent";
    private const string Description = "Runs KOR NetworkOps health checks and approved fixes on this PC for the NetworkOps service on KOR-APP01.";

    public static int Run(AgentSettings s, string code)
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Console.Error.WriteLine("Run this as an administrator (right-click PowerShell: Run as administrator).");
            return 2;
        }

        Console.WriteLine($"Enrolling {s.Device} with NetworkOps at {s.ServerUrl} ...");
        string key;
        try { key = TradeCodeForKey(s, code.Trim()); }
        catch (Exception ex) { Console.Error.WriteLine("Not enrolled: " + ex.Message); return 1; }

        var from = AppDomain.CurrentDomain.BaseDirectory;
        Sc($"stop {Program.ServiceName}", allowFail: true);
        System.Threading.Thread.Sleep(2000);
        Directory.CreateDirectory(InstallDir);
        if (!string.Equals(Path.GetFullPath(from).TrimEnd('\\'), Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            foreach (var f in Directory.GetFiles(from))
                File.Copy(f, Path.Combine(InstallDir, Path.GetFileName(f)), overwrite: true);

        var data = Path.Combine(InstallDir, "data");
        DataFolder.Secure(data);
        var keyFile = Path.Combine(data, "agent.key");
        if (File.Exists(keyFile)) File.Delete(keyFile);   // a new file inherits the locked folder; an old one keeps its own
        File.WriteAllText(keyFile, key);

        var bin = $"\"{Path.Combine(InstallDir, "Kor.Operations.NetworkOps.Agent.exe")}\"";
        if (Sc($"query {Program.ServiceName}", allowFail: true) != 0)
            Sc($"create {Program.ServiceName} binPath= {bin} start= auto DisplayName= \"{DisplayName}\"");
        else
            Sc($"config {Program.ServiceName} binPath= {bin} start= auto DisplayName= \"{DisplayName}\"");
        Sc($"description {Program.ServiceName} \"{Description}\"", allowFail: true);
        Sc($"start {Program.ServiceName}");
        Console.WriteLine($"Enrolled. The agent is running; {s.Device} appears in NetworkOps within a minute.");
        return 0;
    }

    private static string TradeCodeForKey(AgentSettings s, string code)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && AgentLoop.Sha256Hex(cert.RawData) == s.ServerCertSha256,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(s.ServerUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
        using var content = new ByteArrayContent(Json.Write(new EnrolRequest { Device = s.Device, Code = code }));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = http.PostAsync("agent/v1/enrol", content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode == HttpStatusCode.Forbidden
                ? "the code is wrong, used, expired, or was issued for another PC name -- issue a new one in the Command Center"
                : $"APP01 answered {(int)response.StatusCode} {response.ReasonPhrase}");
        var answer = Json.Read<EnrolAnswer>(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
        if (answer is null || answer.Key.Length < 20) throw new InvalidOperationException("APP01 sent no key");
        return answer.Key;
    }

    private static int Sc(string args, bool allowFail = false)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 && !allowFail) throw new InvalidOperationException($"sc {args} failed ({p.ExitCode})");
        return p.ExitCode;
    }
}
