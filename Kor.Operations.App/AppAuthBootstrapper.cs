#nullable enable
using System;
using System.Configuration;
using System.Threading.Tasks;
using Kor.Operations.Graph;
using Kor.Operations.Services;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Kor.Operations
{
    internal static class AppAuthBootstrapper
    {
        internal static string ResolveUserUpn()
        {
            var overrideUpn = ConfigurationManager.AppSettings[AppConfigKeys.UserUpnOverride];
            return !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : $"{Environment.UserName}@korstructural.com";
        }

        internal static async Task EnsureGraphInitializedForDelegatedAuthAsync()
        {
            string tenantId = (ConfigurationManager.AppSettings[AppConfigKeys.GraphTenantId] ?? string.Empty).Trim();
            string clientId = (ConfigurationManager.AppSettings[AppConfigKeys.GraphClientId] ?? string.Empty).Trim();
            string driveId = (ConfigurationManager.AppSettings[AppConfigKeys.GraphDriveId] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(driveId))
            {
                throw new InvalidOperationException(
                    "Microsoft Graph configuration missing. App.config must define Graph.TenantId, Graph.ClientId, and Graph.DriveId.");
            }

            var scopes = new[]
            {
                "User.Read",
                "Mail.Send",
                "Files.ReadWrite.All"
            };

            var loginHint = ConfigurationManager.AppSettings[AppConfigKeys.UserUpnOverride];

            var provider = await MsalGraphAuthenticationProvider
                .CreateAsync(tenantId, clientId, scopes, loginHintUpn: loginHint)
                .ConfigureAwait(true);

            await provider.EnsureSignedInAsync(loginHintUpn: loginHint).ConfigureAwait(true);

            App.SignedInUserUpn = provider.SignedInUpn ?? (string.IsNullOrWhiteSpace(loginHint) ? null : loginHint.Trim());

            GraphFacade.Initialize((IAuthenticationProvider)provider, driveId);
        }
    }
}
