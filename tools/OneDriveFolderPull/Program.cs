// OneDriveFolderPull -- copy one folder of a user's OneDrive to a local disk, app-only, resumable, with a
// manifest of every file. Written 2026-09-29 to rebuild KOR-206-N's failing D: from Kevin's OneDrive
// "D Drive Backup_24" straight onto FS01 (not through a laptop on Wi-Fi, not through the user's PC, whose
// OneDrive copy is cloud-only placeholders).
//
//   OneDriveFolderPull protect <secret-file>          reads a client secret on stdin, writes it DPAPI
//                                                     (LocalMachine) protected: usable on THIS machine only
//   OneDriveFolderPull pull --tenant T --client C --secret-file F --user UPN --folder "Path/In/OneDrive"
//                          --dest D:\target [--parallel 8]
//
// pull writes, beside --dest:  <dest>.manifest.csv  every file OneDrive holds (relative path, bytes, modified
// UTC, quickXorHash) -- the source list for a differential; <dest>.status.txt progress every 30 s;
// <dest>.failures.csv what could not be copied. A file already present with the same size and modified time
// is skipped, so a re-run resumes. Each download lands as .partial and is only renamed into place when its
// length matches what OneDrive reports. Exit 0 = every file present; 2 = some failed (see failures.csv).
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length >= 2 && args[0] == "protect")
{
    var secret = Console.In.ReadToEnd().Trim();
    if (secret.Length == 0) { Console.Error.WriteLine("no secret on stdin"); return 1; }
    File.WriteAllBytes(args[1], ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.LocalMachine));
    Console.WriteLine($"protected secret written to {args[1]} (usable on {Environment.MachineName} only)");
    return 0;
}
if (args.Length == 0 || args[0] != "pull") { Console.Error.WriteLine("usage: protect <file> | pull --tenant --client --secret-file --user --folder --dest [--parallel]"); return 1; }

var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 1; i + 1 < args.Length; i += 2) opt[args[i].TrimStart('-')] = args[i + 1];
string Need(string k) => opt.TryGetValue(k, out var v) && v.Length > 0 ? v : throw new ArgumentException($"--{k} is required");
var dest = Path.GetFullPath(Need("dest"));
var parallel = opt.TryGetValue("parallel", out var p) ? int.Parse(p, CultureInfo.InvariantCulture) : 8;
var auth = new Auth(Need("tenant"), Need("client"),
    Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Need("secret-file")), null, DataProtectionScope.LocalMachine)));
var user = Need("user");
var folderPath = Need("folder").Trim('/');

var statusPath = dest + ".status.txt";
void Status(string s) { var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}"; Console.WriteLine(line); File.AppendAllText(statusPath, line + Environment.NewLine); }

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxConnectionsPerServer = parallel * 2 }) { Timeout = TimeSpan.FromMinutes(30) };
var graph = "https://graph.microsoft.com/v1.0";

async Task<JsonElement> GetJson(string url)
{
    for (var attempt = 1; ; attempt++)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.TokenAsync(http));
        using var res = await http.SendAsync(req);
        if (res.IsSuccessStatusCode) return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
        if (attempt >= 6 || (int)res.StatusCode is 400 or 401 or 403 or 404) throw new InvalidOperationException($"GET {url} -> {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        await Task.Delay(Backoff(res, attempt));
    }
}

Status($"start: {user} /{folderPath} -> {dest}");
var drive = await GetJson($"{graph}/users/{Uri.EscapeDataString(user)}/drive?$select=id");
var driveId = drive.GetProperty("id").GetString()!;
var root = await GetJson($"{graph}/drives/{driveId}/root:/{string.Join('/', folderPath.Split('/').Select(Uri.EscapeDataString))}?$select=id,name,size");
Status($"folder {root.GetProperty("name").GetString()}: {root.GetProperty("size").GetInt64() / 1e9:0.00} GB by OneDrive's count");

// 1. Every file, by walking the folders (delta is root-only on OneDrive for Business).
var files = new List<RemoteFile>();
var queue = new Queue<(string Id, string Rel)>();
queue.Enqueue((root.GetProperty("id").GetString()!, ""));
var folders = 0;
while (queue.Count > 0)
{
    var (id, rel) = queue.Dequeue();
    folders++;
    string? url = $"{graph}/drives/{driveId}/items/{id}/children?$top=999&$select=id,name,size,file,folder,fileSystemInfo";
    while (url is not null)
    {
        var page = await GetJson(url);
        foreach (var it in page.GetProperty("value").EnumerateArray())
        {
            var name = it.GetProperty("name").GetString()!;
            var childRel = rel.Length == 0 ? name : rel + "\\" + name;
            if (it.TryGetProperty("folder", out _)) { queue.Enqueue((it.GetProperty("id").GetString()!, childRel)); continue; }
            var modified = it.TryGetProperty("fileSystemInfo", out var fsi) && fsi.TryGetProperty("lastModifiedDateTime", out var lm) ? lm.GetDateTime().ToUniversalTime() : DateTime.MinValue;
            var hash = it.TryGetProperty("file", out var f) && f.TryGetProperty("hashes", out var hs) && hs.TryGetProperty("quickXorHash", out var q) ? q.GetString() : null;
            files.Add(new RemoteFile(it.GetProperty("id").GetString()!, childRel, it.GetProperty("size").GetInt64(), modified, hash));
        }
        url = page.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
    }
}
Directory.CreateDirectory(dest);
using (var w = new StreamWriter(dest + ".manifest.csv", false, new UTF8Encoding(true)))
{
    w.WriteLine("RelativePath,Bytes,ModifiedUtc,QuickXorHash");
    foreach (var f in files.OrderBy(f => f.Rel, StringComparer.OrdinalIgnoreCase))
        w.WriteLine($"{Csv(f.Rel)},{f.Size},{f.Modified:yyyy-MM-ddTHH:mm:ssZ},{f.Hash}");
}
var total = files.Sum(f => f.Size);
Status($"listed {files.Count} files in {folders} folders, {total / 1e9:0.00} GB; manifest written");

// 2. Download.
long doneBytes = 0; int done = 0, skipped = 0;
var failures = new ConcurrentBag<(RemoteFile F, string Why)>();
using var ticker = new Timer(_ => Status($"progress: {done + skipped} of {files.Count} files ({skipped} already present), {Interlocked.Read(ref doneBytes) / 1e9:0.0} of {total / 1e9:0.0} GB, {failures.Count} failed"),
    null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
await Parallel.ForEachAsync(files.OrderBy(f => f.Size), new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (f, ct) =>
{
    var target = Path.Combine(dest, f.Rel);
    var fi = new FileInfo(target);
    if (fi.Exists && fi.Length == f.Size && (f.Modified == DateTime.MinValue || Math.Abs((fi.LastWriteTimeUtc - f.Modified).TotalSeconds) < 2))
    {
        Interlocked.Increment(ref skipped); Interlocked.Add(ref doneBytes, f.Size); return;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    var partial = target + ".partial";
    for (var attempt = 1; attempt <= 6; attempt++)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{graph}/drives/{driveId}/items/{f.Id}/content");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.TokenAsync(http));
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode)
            {
                if ((int)res.StatusCode is 401 or 403 or 404 || attempt == 6) throw new InvalidOperationException($"HTTP {(int)res.StatusCode}");
                await Task.Delay(Backoff(res, attempt), ct); continue;
            }
            // HttpClient.Timeout stops at the headers (ResponseHeadersRead): a body that goes silent would hang
            // forever -- it did, all 8 slots, for 3.5 h on 2026-09-29. Every read gets its own 2-minute limit.
            await using (var body = await res.Content.ReadAsStreamAsync(ct))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                var buffer = new byte[1 << 20];
                while (true)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(TimeSpan.FromMinutes(2));
                    int n;
                    try { n = await body.ReadAsync(buffer, idle.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("no data for 2 minutes"); }
                    if (n == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            var got = new FileInfo(partial).Length;
            if (got != f.Size) throw new InvalidOperationException($"got {got} bytes, OneDrive says {f.Size}");
            File.Move(partial, target, overwrite: true);
            if (f.Modified != DateTime.MinValue) File.SetLastWriteTimeUtc(target, f.Modified);
            Interlocked.Increment(ref done); Interlocked.Add(ref doneBytes, f.Size);
            return;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            if (attempt == 6 || ex.Message.StartsWith("HTTP 40", StringComparison.Ordinal))
            {
                failures.Add((f, ex.Message)); try { File.Delete(partial); } catch (IOException) { }
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
        }
    }
});
using (var w = new StreamWriter(dest + ".failures.csv", false, new UTF8Encoding(true)))
{
    w.WriteLine("RelativePath,Bytes,Why");
    foreach (var (f, why) in failures.OrderBy(x => x.F.Rel)) w.WriteLine($"{Csv(f.Rel)},{f.Size},{Csv(why)}");
}
Status($"DONE: {done} downloaded, {skipped} already present, {failures.Count} failed of {files.Count} files; {total / 1e9:0.00} GB listed");
return failures.IsEmpty ? 0 : 2;

static TimeSpan Backoff(HttpResponseMessage res, int attempt)
    => res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(120, 5 * Math.Pow(2, attempt)));

static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

internal sealed record RemoteFile(string Id, string Rel, long Size, DateTime Modified, string? Hash);

// App-only token (client credentials), refreshed 5 minutes before it expires; one refresh at a time.
internal sealed class Auth(string tenant, string client, string secret)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTime _expires;

    public async Task<string> TokenAsync(HttpClient http)
    {
        if (_token is not null && DateTime.UtcNow < _expires) return _token;
        await _gate.WaitAsync();
        try
        {
            if (_token is not null && DateTime.UtcNow < _expires) return _token;
            using var res = await http.PostAsync($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = client, ["client_secret"] = secret, ["grant_type"] = "client_credentials", ["scope"] = "https://graph.microsoft.com/.default",
            }));
            var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"token request failed: {(int)res.StatusCode} {json}");
            _token = json.GetProperty("access_token").GetString();
            _expires = DateTime.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32() - 300);
            return _token!;
        }
        finally { _gate.Release(); }
    }
}
