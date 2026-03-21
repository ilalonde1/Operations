#nullable enable
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    /// <summary>
    /// MSAL delegated auth provider for Microsoft Graph (Graph SDK v5 / Kiota).
    /// - Uses IPublicClientApplication (no secrets).
    /// - Attempts silent token acquisition; falls back to interactive (UI).
    /// - Persists MSAL token cache to disk.
    /// </summary>
    internal sealed class MsalGraphAuthenticationProvider : IAuthenticationProvider
    {
        private readonly IPublicClientApplication _pca;
        private readonly string[] _scopes;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private IAccount? _account;

        internal string? SignedInUpn => _account?.Username;

        private MsalGraphAuthenticationProvider(IPublicClientApplication pca, IEnumerable<string> scopes)
        {
            _pca = pca ?? throw new ArgumentNullException(nameof(pca));
            _scopes = scopes?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToArray()
                      ?? throw new ArgumentNullException(nameof(scopes));
            if (_scopes.Length == 0)
                throw new ArgumentException("At least one scope is required.", nameof(scopes));
        }

        public static async Task<MsalGraphAuthenticationProvider> CreateAsync(
            string tenantId,
            string clientId,
            IEnumerable<string> scopes,
            string? loginHintUpn = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentNullException(nameof(clientId));

            var authority = $"https://login.microsoftonline.com/{tenantId}";

            var pca = PublicClientApplicationBuilder
                .Create(clientId)
                .WithAuthority(authority)
                .WithDefaultRedirectUri()
                .Build();

            await ConfigureTokenCacheAsync(pca).ConfigureAwait(false);

            var provider = new MsalGraphAuthenticationProvider(pca, scopes);

            // Best-effort preselect an account to keep later calls silent.
            try
            {
                var accounts = await pca.GetAccountsAsync().ConfigureAwait(false);
                provider._account = PickAccount(accounts, loginHintUpn);
            }
            catch (Exception ex)
            {
                // Non-fatal: account will be resolved at first token acquisition.
                Log.ForContext<MsalGraphAuthenticationProvider>()
                   .Warning(ex, "Failed to pre-select MSAL account from cache; interactive auth will be required. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message);
            }

            return provider;
        }

        public async Task EnsureSignedInAsync(string? loginHintUpn = null, CancellationToken ct = default)
        {
            // Force one token acquisition on the UI thread early, so later Graph calls can stay silent.
            _ = await GetAccessTokenAsync(loginHintUpn, ct).ConfigureAwait(false);
        }

        public async Task AuthenticateRequestAsync(
            RequestInformation requestInfo,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
        {
            if (requestInfo == null) throw new ArgumentNullException(nameof(requestInfo));

            var token = await GetAccessTokenAsync(loginHintUpn: null, cancellationToken).ConfigureAwait(false);
            requestInfo.Headers.TryAdd("Authorization", "Bearer " + token);
        }

        private async Task<string> GetAccessTokenAsync(string? loginHintUpn, CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_account == null)
                {
                    var accounts = await _pca.GetAccountsAsync().ConfigureAwait(false);
                    _account = PickAccount(accounts, loginHintUpn);
                }

                try
                {
                    if (_account != null)
                    {
                        var silent = await _pca.AcquireTokenSilent(_scopes, _account)
                            .ExecuteAsync(ct)
                            .ConfigureAwait(false);
                        return silent.AccessToken;
                    }
                }
                catch (MsalUiRequiredException)
                {
                    // fall through to interactive
                }

                var interactive = _pca.AcquireTokenInteractive(_scopes);

                if (_account != null)
                    interactive = interactive.WithAccount(_account);

                if (!string.IsNullOrWhiteSpace(loginHintUpn))
                    interactive = interactive.WithLoginHint(loginHintUpn.Trim());

                // Default behavior uses system browser on Windows for .NET 8.
                var result = await interactive.ExecuteAsync(ct).ConfigureAwait(false);
                _account = result.Account;
                return result.AccessToken;
            }
            finally
            {
                _gate.Release();
            }
        }

        private static IAccount? PickAccount(IEnumerable<IAccount> accounts, string? loginHintUpn)
        {
            var list = accounts?.ToList() ?? new List<IAccount>();
            if (list.Count == 0) return null;

            if (!string.IsNullOrWhiteSpace(loginHintUpn))
            {
                var hint = loginHintUpn.Trim();
                var match = list.FirstOrDefault(a => string.Equals(a.Username, hint, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            // Fall back to "first" account in cache.
            return list[0];
        }

        private static async Task ConfigureTokenCacheAsync(IPublicClientApplication pca)
        {
            // Persist the MSAL cache to disk (per-user). This is critical for silent refresh across app restarts.
            var baseDir =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KOR", "KorTransmittals");
            Directory.CreateDirectory(baseDir);

            var cacheFile = Path.Combine(baseDir, "msal_token_cache.dat");

            var props = new StorageCreationPropertiesBuilder(cacheFile, baseDir)
                // WPF is Windows-only; DPAPI is fine here. This encrypts the cache at rest for the current user.
                .WithCacheChangedEvent("Kor.Transmittals.MsalTokenCache")
                .Build();

            var helper = await MsalCacheHelper.CreateAsync(props).ConfigureAwait(false);
            helper.RegisterCache(pca.UserTokenCache);
        }
    }
}
