// One-shot operator tool: capture an authenticated browser session for
// portals where Worker headless login is blocked by anti-bot challenges
// (Cloudflare Turnstile on APC, recaptcha on BC Bid, etc.).
//
// Flow: launch HEADED Chromium → operator signs in interactively + clicks
// around to the target view (e.g. APC "Closed/Awarded" listing) → press
// ENTER → tool saves the storage state JSON (cookies + localStorage) and
// copies it to KOR-APP01's sessions directory. The Worker then loads that
// state for headless scrapes without ever typing credentials.
//
// Usage:
//   dotnet run --project Kor.Opportunities.Capture --
//       --portal apc
//       --url   https://purchasing.alberta.ca/supplier-login
//       --out   apc-session.json
//
// Or run the published exe:
//   .\Kor.Opportunities.Capture.exe apc

using System.Globalization;
using Microsoft.Playwright;

internal static class Program
{
    // Known portal profiles. Add entries here when wiring new authenticated sources.
    private static readonly Dictionary<string, PortalProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apc"] = new(
            DefaultStartUrl: "https://purchasing.alberta.ca/supplier-login",
            SessionFileName: "apc-session.json",
            RemoteSessionsUnc: @"\\KOR-APP01\C$\ProgramData\KorOperations\Opportunities\sessions",
            DisplayName: "Alberta Purchasing Connection"),
        // ["bcbid"] = new(...)   // future: would let us cache a BCeID session too
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var (portalKey, startUrl, outFile, copyRemote) = ParseArgs(args);
            if (!Profiles.TryGetValue(portalKey, out var profile))
            {
                Console.Error.WriteLine($"Unknown portal '{portalKey}'. Known: {string.Join(", ", Profiles.Keys)}");
                return 2;
            }
            startUrl ??= profile.DefaultStartUrl;
            outFile ??= Path.Combine(Environment.CurrentDirectory, profile.SessionFileName);

            Console.WriteLine();
            Console.WriteLine($"=== {profile.DisplayName} session capture ===");
            Console.WriteLine($"Start URL : {startUrl}");
            Console.WriteLine($"Output    : {outFile}");
            Console.WriteLine($"Remote    : {(copyRemote ? Path.Combine(profile.RemoteSessionsUnc, profile.SessionFileName) : "(local only)")}");
            Console.WriteLine();

            using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
                Args = new[] { "--disable-blink-features=AutomationControlled" },
            }).ConfigureAwait(false);

            // Use the same UA/viewport/locale shape as PlaywrightBrowserPool so
            // the captured state is fingerprint-compatible with headless reuse.
            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "en-CA",
                TimezoneId = "America/Vancouver",
                JavaScriptEnabled = true,
            }).ConfigureAwait(false);

            var page = await context.NewPageAsync().ConfigureAwait(false);
            await page.GotoAsync(startUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000,
            }).ConfigureAwait(false);

            Console.WriteLine("Browser is open. In that window:");
            Console.WriteLine("  1) Sign in normally");
            Console.WriteLine("  2) Complete any profile/Cloudflare challenges");
            Console.WriteLine("  3) Navigate to the view you want the Worker to scrape");
            Console.WriteLine("     (e.g. APC: search → status filter 'Awarded' or 'Closed')");
            Console.WriteLine();
            Console.WriteLine("When the target page is fully loaded, come back here and press ENTER.");
            Console.WriteLine("Or press 'q' + ENTER to abort without saving.");
            Console.Write("> ");
            var key = Console.ReadLine();
            if (string.Equals(key?.Trim(), "q", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted. Nothing saved.");
                return 1;
            }

            var finalUrl = page.Url;
            Console.WriteLine();
            Console.WriteLine($"Captured final page URL: {finalUrl}");
            Console.WriteLine($"Saving storage state to: {outFile}");

            Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
            await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = outFile }).ConfigureAwait(false);

            var info = new FileInfo(outFile);
            Console.WriteLine($"Saved {info.Length:N0} bytes.");

            if (copyRemote)
            {
                var remotePath = Path.Combine(profile.RemoteSessionsUnc, profile.SessionFileName);
                Console.WriteLine();
                Console.WriteLine($"Copying to remote: {remotePath}");
                try
                {
                    Directory.CreateDirectory(profile.RemoteSessionsUnc);
                    File.Copy(outFile, remotePath, overwrite: true);
                    Console.WriteLine("Remote copy OK.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Remote copy FAILED: {ex.Message}");
                    Console.Error.WriteLine("Local file is fine — copy manually with:");
                    Console.Error.WriteLine($"  robocopy \"{Path.GetDirectoryName(outFile)}\" \"{profile.RemoteSessionsUnc}\" \"{Path.GetFileName(outFile)}\"");
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== Capture complete ===");
            Console.WriteLine($"Worker source row should set BaseUrl = {finalUrl}");
            Console.WriteLine($"And mapping  playwright.storageStateFile = {Path.Combine(profile.RemoteSessionsUnc, profile.SessionFileName)}");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Capture failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 99;
        }
    }

    private static (string Portal, string? StartUrl, string? OutFile, bool CopyRemote) ParseArgs(string[] args)
    {
        string? portal = null, startUrl = null, outFile = null;
        var copyRemote = true;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--portal" when i + 1 < args.Length: portal = args[++i]; break;
                case "--url" when i + 1 < args.Length:    startUrl = args[++i]; break;
                case "--out" when i + 1 < args.Length:    outFile = args[++i]; break;
                case "--no-remote": copyRemote = false; break;
                default:
                    // Bare positional first arg is the portal key.
                    if (portal is null && !a.StartsWith("--", StringComparison.Ordinal))
                    {
                        portal = a;
                    }
                    break;
            }
        }

        portal ??= "apc";
        return (portal, startUrl, outFile, copyRemote);
    }

    private sealed record PortalProfile(
        string DefaultStartUrl,
        string SessionFileName,
        string RemoteSessionsUnc,
        string DisplayName);
}
