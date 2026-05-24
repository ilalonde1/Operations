#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Kor.Operations.App.Options;
using Kor.Operations.Controls;
using Microsoft.Extensions.DependencyInjection;
using Kor.Operations.Services;
using Serilog;
namespace Kor.Operations
{
    /// <summary>
    /// Centralized header name/avatar loader with in-memory + disk caching.
    /// Thread-safe, coalesces concurrent requests per email.
    /// </summary>
    public static class HeaderLoader
    {
        // In-memory caches (live for app lifetime)
        private static readonly ConcurrentDictionary<string, Task<string?>> _displayNameTasks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Task<BitmapImage?>> _avatarTasks = new(StringComparer.OrdinalIgnoreCase);

        // Disk cache settings
        private static readonly string _cacheRoot =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR", "Headshots");

        private static readonly TimeSpan _avatarTtl = TimeSpan.FromDays(7);
        private static int _initialized;

        /// <summary>
        /// Apply user email, display name (Deltek if available), and avatar to a KorHeader.
        /// - If overrideEmail is null, we derive from Environment.UserName + domain or App.config override.
        /// - Safe to call multiple times / from multiple windows; underlying lookups are cached and coalesced.
        /// </summary>
        public static async Task ApplyAsync(KorHeader header, string? overrideEmail = null, string? fallbackSam = null)
        {
            try
            {
                await ApplyCoreAsync(header, overrideEmail, fallbackSam).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "HeaderLoader failed.");
            }
        }

        private static async Task ApplyCoreAsync(KorHeader header, string? overrideEmail, string? fallbackSam)
        {
            if (header == null) return;

            EnsureCacheFolders();

            // Determine email
            var sam = fallbackSam ?? Environment.UserName ?? "user";
            var userOptions = Kor.Operations.Services.AppServices.Get<UserOptions>();
            var upnOverride = userOptions.UserUpnOverride;

            var email = !string.IsNullOrWhiteSpace(overrideEmail)
                ? overrideEmail.Trim()
                : (!string.IsNullOrWhiteSpace(global::Kor.Operations.OperationsApp.SignedInUserUpn) ? global::Kor.Operations.OperationsApp.SignedInUserUpn!.Trim()
                    : (!string.IsNullOrWhiteSpace(upnOverride) ? upnOverride.Trim()
                        : $"{sam}@korstructural.com"));

            // Email in header immediately (no wait)
            header.UserEmail = email;

            // Display name (coalesced)
            var displayTask = _displayNameTasks.GetOrAdd(email, _ => GetDisplayNameAsync(email, sam));
            var display = await Safe(displayTask) ?? PrettifySam(sam);
            header.UserDisplayName = display;

            // Avatar (coalesced)
            var avatarTask = _avatarTasks.GetOrAdd(email, _ => GetAvatarAsync(email));
            var bmp = await Safe(avatarTask);
            if (bmp != null)
            {
                // Set through the public DP; no reflection needed
                header.AvatarImageSource = bmp;
            }
        }

        private static void EnsureCacheFolders()
        {
            if (Interlocked.Exchange(ref _initialized, 1) == 1) return;
            try { Directory.CreateDirectory(_cacheRoot); }
            catch (Exception ex) { Log.Warning(ex, "HeaderLoader: ensure cache folders failed."); }
        }

        // ---------- Display Name ----------

        private static async Task<string?> GetDisplayNameAsync(string email, string sam)
        {
            try
            {
                var provider = new DeltekHeadshotProvider(Kor.Operations.Services.AppServices.Get<DeltekOdbcOptions>());
                // If you have a dedicated method for names:
                // return await provider.TryGetEmployeeDisplayNameAsync(email);
                // Otherwise, reuse the headshot metadata call if that’s what you expose:
                var name = await provider.TryGetEmployeeDisplayNameAsync(email);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "HeaderLoader: name lookup failed.");
            }
            return PrettifySam(sam);
        }

        private static string PrettifySam(string sam)
            => (sam ?? string.Empty).Replace('.', ' ').Replace('_', ' ').Trim();

        // ---------- Avatar (with disk cache) ----------

        private static async Task<BitmapImage?> GetAvatarAsync(string email)
        {
            // 1) Try disk cache
            var path = CachedPathFor(email);
            var bmp = TryLoadDiskAvatar(path, out var freshEnough);
            if (bmp != null && freshEnough) return bmp;

            // 2) Query provider
            try
            {
                var provider = new DeltekHeadshotProvider(Kor.Operations.Services.AppServices.Get<DeltekOdbcOptions>());
                var fetched = await provider.TryGetByEmailAsync(email);
                if (fetched != null)
                {
                    // Save to disk cache (best effort)
                    _ = Task.Run(() => SaveAvatarToDisk(path, fetched));
                    return fetched;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "HeaderLoader: avatar lookup failed.");
            }

            // 3) If we had an expired cached file, still return it (better than nothing)
            if (bmp != null) return bmp;

            return null;
        }

        private static string CachedPathFor(string email)
        {
            using var sha = SHA1.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(email))).ToLowerInvariant();
            return Path.Combine(_cacheRoot, $"{hash}.png");
        }

        private static BitmapImage? TryLoadDiskAvatar(string path, out bool freshEnough)
        {
            freshEnough = false;
            try
            {
                if (!File.Exists(path)) return null;
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                freshEnough = age <= _avatarTtl;

                // Load as frozen bitmap
                var bmp = new BitmapImage();
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = fs;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex) { Log.Debug(ex, "HeaderLoader: try load disk avatar failed for {Path}.", path); return null; }
        }

        private static void SaveAvatarToDisk(string path, BitmapImage bmp)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));

                using var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;

                using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
                ms.CopyTo(fs);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "HeaderLoader: save avatar to disk failed.");
            }
        }

        // ---------- Helpers ----------

        private static async Task<T?> Safe<T>(Task<T> t)
        {
            try { return await t.ConfigureAwait(false); }
            catch (Exception ex) { Log.Debug(ex, "HeaderLoader: safe task wrapper failed."); return default; }
        }
    }
}
