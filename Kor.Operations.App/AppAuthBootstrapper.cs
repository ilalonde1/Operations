#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Graph;
using Kor.Operations.Services;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Kor.Operations
{
    internal static class AppAuthBootstrapper
    {
        internal static string ResolveUserUpn(UserOptions userOptions)
        {
            var overrideUpn = userOptions.UserUpnOverride;
            return !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : $"{Environment.UserName}@korstructural.com";
        }

        internal static async Task EnsureGraphInitializedForDelegatedAuthAsync(GraphOptions graphOptions, UserOptions userOptions)
        {
            string tenantId = graphOptions.TenantId.Trim();
            string clientId = graphOptions.ClientId.Trim();
            string driveId = graphOptions.DriveId.Trim();

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

            var loginHint = userOptions.UserUpnOverride;

            var provider = await MsalGraphAuthenticationProvider
                .CreateAsync(tenantId, clientId, scopes, loginHintUpn: loginHint)
                .ConfigureAwait(true);

            await provider.EnsureSignedInAsync(loginHintUpn: loginHint).ConfigureAwait(true);

            OperationsApp.SignedInUserUpn = provider.SignedInUpn ?? (string.IsNullOrWhiteSpace(loginHint) ? null : loginHint.Trim());

            GraphFacade.Initialize((IAuthenticationProvider)provider, driveId);
        }
    }
}
