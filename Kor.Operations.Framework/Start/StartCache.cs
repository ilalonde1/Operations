using System.Globalization;
using System.Text;

namespace Kor.Operations.Framework.Start;

/// <summary>
/// K6: every good render of a start part is kept at %LOCALAPPDATA%\KOR\start_&lt;module&gt;_&lt;part&gt;[_text].txt. When the
/// database cannot be read (or the part is not back by the deadline) the session is TOLD, loudly - "!! UNREADABLE:
/// &lt;reason&gt; !!" or "!! PART LATE !!" - and given the cached render with its age; never silent, never a guess.
/// </summary>
public static class StartCache
{
    public static readonly string DefaultDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR");

    /// <summary>A hook render (cut to the budget) and a --text render are kept apart: one is never served for the other.</summary>
    public static string PathFor(string dir, string module, string part, bool hook) =>
        Path.Combine(dir, $"start_{Safe(module)}_{part}{(hook ? "" : "_text")}.txt");

    private static string Safe(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());

    public static void Save(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        catch (IOException) { /* a cache that cannot be written loses nothing the session needs now */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The loud text when the live render failed: the banner, then the cached render and its age (or that there is none).
    /// Cut to <paramref name="budget"/> from the bottom, so the banner always arrives.
    /// </summary>
    public static string Fallback(string banner, string path, DateTime now, int? budget, string byHand)
    {
        string text;
        try
        {
            if (File.Exists(path))
            {
                var at = File.GetLastWriteTime(path);
                var age = now - at;
                text = $"{banner} The cached render from {at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} " +
                       $"({Age(age)} old) follows - it may be out of date; {byHand}.\n" + File.ReadAllText(path, Encoding.UTF8);
            }
            else text = $"{banner} No cached render on this machine - {byHand} before any work.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            text = $"{banner} The cached render could not be read ({e.Message}) - {byHand} before any work.";
        }
        return budget is not null && text.Length > budget ? text[..budget.Value] : text;
    }

    public static string Age(TimeSpan t) =>
        t.TotalMinutes < 1 ? "under a minute"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min"
        : t.TotalDays < 1 ? $"{(int)t.TotalHours} h {t.Minutes} min"
        : $"{(int)t.TotalDays} d {t.Hours} h";
}

/// <summary>
/// Where a start part spends its time, on stderr, only when KOR_START_TRACE is set (the hook's stdout is never touched).
/// </summary>
public static class StartTrace
{
    private static readonly bool On = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KOR_START_TRACE"));
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    public static void Mark(string what)
    {
        if (On) Console.Error.WriteLine($"kor start trace: {Clock.Elapsed.TotalMilliseconds,7:0} ms  {what}");
    }
}

/// <summary>
/// Runs one start part against a hard deadline (spec: 8 s inside kor): the live render, saved to the cache; or, when the
/// load throws or is late, the cache with the loud banner. The load runs on its own task: a late one is not waited for
/// (the process ends and its uncommitted transaction rolls back).
/// </summary>
public static class StartPart
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);

    public sealed record Result(string Text, bool Live, IReadOnlyList<string> WatchPaths);

    public static Result Run(Func<(string Text, IReadOnlyList<string> Watch)> live, string cachePath, int? budget, TimeSpan deadline,
                             string byHand, Func<DateTime> now, Action<Exception>? log = null)
    {
        var task = Task.Run(live);
        try
        {
            if (!task.Wait(deadline))
                return new Result(StartCache.Fallback($"!! PART LATE: not back in {Records.Freshness.Secs(deadline)} s !!", cachePath, now(), budget, byHand),
                                  false, Array.Empty<string>());
            var (text, watch) = task.Result;
            StartCache.Save(cachePath, text);
            return new Result(text, true, watch);
        }
        catch (AggregateException x)
        {
            var e = x.InnerException ?? x;
            log?.Invoke(e);
            return new Result(StartCache.Fallback($"!! UNREADABLE: {OneLine(e)} !!", cachePath, now(), budget, byHand), false, Array.Empty<string>());
        }
    }

    private static string OneLine(Exception e)
    {
        var m = Capture.Redact.Text(string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message)!.ReplaceLineEndings(" ").Trim();
        return m.Length <= 300 ? m : m[..297] + "...";
    }
}
