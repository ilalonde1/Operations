#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Graph;
using Kor.Operations.Services;

namespace Kor.Operations
{
    internal static class AppAuthBootstrapper
    {
        /// <summary>
        /// Microsoft Graph permission scopes required by KOR Operations.
        /// - User.Read: establishes delegated sign-in context and resolves the signed-in user's account identity for Graph-backed operations.
        /// - Mail.Send: sends transmittal and quick transfer emails through Microsoft Graph.
        /// - Files.ReadWrite.All: creates folders, uploads files, and generates SharePoint/OneDrive links for transmittal workflows.
        /// </summary>
        private static readonly string[] GraphScopes =
        {
            "User.Read",
            "Mail.Send",
            "Files.ReadWrite.All"
        };

        internal static string ResolveUserUpn(UserOptions userOptions)
        {
            var overrideUpn = userOptions.UserUpnOverride;
            return !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : $"{Environment.UserName}@korstructural.com";
        }

        internal static async Task EnsureGraphInitializedForDelegatedAuthAsync(GraphOptions graphOptions, UserOptions userOptions, GraphAuthenticationState graphAuthenticationState)
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

            var loginHint = userOptions.UserUpnOverride;

            var provider = await MsalGraphAuthenticationProvider
                .CreateAsync(tenantId, clientId, GraphScopes, loginHintUpn: loginHint)
                .ConfigureAwait(true);

            await provider.EnsureSignedInAsync(loginHintUpn: loginHint).ConfigureAwait(true);

            OperationsApp.SignedInUserUpn = provider.SignedInUpn ?? (string.IsNullOrWhiteSpace(loginHint) ? null : loginHint.Trim());
            graphAuthenticationState.AuthenticationProvider = provider;
        }
    }
}
