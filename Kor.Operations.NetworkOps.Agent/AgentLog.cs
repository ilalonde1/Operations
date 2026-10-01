using System;
using System.IO;

namespace Kor.Operations.NetworkOps.Agent;

/// <summary>A small text log in the agent's data folder: agent.log, rolled to agent.log.1 at 1 MB. Two files, never more.</summary>
internal sealed class AgentLog
{
    private const long MaxBytes = 1024 * 1024;
    private readonly string _path;
    private readonly bool _echo;
    private readonly object _gate = new();

    public AgentLog(string dataDir, bool echo)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "agent.log");
        _echo = echo;
    }

    public void Info(string message) => Write("INF", message);
    public void Warn(string message) => Write("WRN", message);
    public void Error(string message) => Write("ERR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        if (_echo) Console.WriteLine(line);
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                {
                    var old = _path + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(_path, old);
                }
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (IOException) { /* a log that cannot be written must never stop the agent */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
