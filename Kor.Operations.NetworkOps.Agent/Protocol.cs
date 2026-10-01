using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Kor.Operations.NetworkOps.Agent;

// The whole conversation with APP01 (Service/Agents/AgentApi.cs is the other side):
//   POST /agent/v1/poll                 -> 204 nothing to do (after up to ~25 s), or 200 a Job
//   POST /agent/v1/jobs/{jobId}/result  -> 204
// Every call carries X-Kor-Agent: <this PC's name> and Authorization: KorAgent <the installer's key>.

[DataContract]
internal sealed class PollRequest
{
    [DataMember(Name = "version")] public string Version = "";
    /// <summary>The folder a job's script must publish its result into (the server builds the script).</summary>
    [DataMember(Name = "workDir")] public string WorkDir = "";
}

[DataContract]
internal sealed class Job
{
    [DataMember(Name = "jobId")] public string JobId = "";
    /// <summary>Complete: the server wraps the probe or fix so it writes {workDir}\{jobId}.json itself.</summary>
    [DataMember(Name = "script")] public string Script = "";
    [DataMember(Name = "timeoutSeconds")] public int TimeoutSeconds = 600;
    /// <summary>Read the console user's idle time and hand it to the script (the health probe wants it).</summary>
    [DataMember(Name = "wantsIdle")] public bool WantsIdle = false;
}

[DataContract]
internal sealed class JobResult
{
    /// <summary>ok | timeout | error</summary>
    [DataMember(Name = "status")] public string Status = "";
    /// <summary>The result file the script published, verbatim (the service parses it exactly as the network route does).</summary>
    [DataMember(Name = "result", EmitDefaultValue = false)] public string? Result;
    [DataMember(Name = "error", EmitDefaultValue = false)] public string? Error;
    [DataMember(Name = "elapsedMs")] public long ElapsedMs;
}

internal static class Json
{
    public static byte[] Write<T>(T value)
    {
        using var ms = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(ms, value);
        return ms.ToArray();
    }

    public static T Read<T>(byte[] utf8) where T : class
    {
        using var ms = new MemoryStream(utf8);
        return (T)(new DataContractJsonSerializer(typeof(T)).ReadObject(ms)
                   ?? throw new SerializationException("empty " + typeof(T).Name + ": " + Encoding.UTF8.GetString(utf8)));
    }
}
