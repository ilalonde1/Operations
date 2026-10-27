#nullable enable
namespace Kor.Operations.NetworkOps.Cli;

// `netops run --direct` goes from this PC straight to the machine: APP01 never sees it, so it has no Actions row. This is
// its audit: one line per direct run in %LOCALAPPDATA%\KOR\netops\direct-runs.log -- when, who, which machines, which
// script (with its SHA-256, since the file may change later) and the purpose with any approval. Best effort: a run is
// never stopped because the log could not be written; it says so on stderr instead.
internal static class DirectRunLog
{
    public static string PathOf => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR", "netops", "direct-runs.log");

    public static void Write(IEnumerable<string> hosts, string? script, string? purpose)
    {
        try
        {
            var sha = script is not null && File.Exists(script)
                ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(script)))[..16] : "-";
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf)!);
            File.AppendAllText(PathOf, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{Environment.UserDomainName}\\{Environment.UserName}\t" +
                $"{string.Join(",", hosts)}\t{script}\tsha256:{sha}\t{purpose ?? "(no purpose)"}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"netops: could not write the direct-run log ({ex.Message}); running anyway");
        }
    }
}
