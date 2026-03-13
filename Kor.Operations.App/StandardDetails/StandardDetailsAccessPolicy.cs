#nullable enable
using System;
using System.Configuration;
using Kor.Operations.App.Services;
using Kor.Operations.Services;

namespace Kor.Operations.StandardDetails;

internal sealed class StandardDetailsAccessPolicy
{
    private readonly string _userIdentity;

    internal StandardDetailsAccessPolicy(string userIdentity)
    {
        _userIdentity = userIdentity ?? string.Empty;
    }

    internal static string ResolveCurrentUserIdentity(string? headerUserEmail = null)
    {
        if (!string.IsNullOrWhiteSpace(headerUserEmail))
            return headerUserEmail.Trim();

        var overrideUpn = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.UserUpnOverride];
        if (!string.IsNullOrWhiteSpace(overrideUpn))
            return overrideUpn.Trim();

        var user = Environment.UserName;
        if (user.Contains('\\'))
            user = user[(user.LastIndexOf('\\') + 1)..];

        return string.IsNullOrWhiteSpace(user) ? "unknown@korstructural.com" : $"{user}@korstructural.com";
    }

    internal bool IsInRoleGroup(string roleName)
        => SecurityGroupAccess.IsUserInGroup(roleName, _userIdentity);

    internal bool CanManageGroups() => IsInRoleGroup(KnownRoles.StandardDetailsAdmins);

    internal bool CanContribute()
        => IsInRoleGroup(KnownRoles.StandardDetailsContributors)
           || IsInRoleGroup(KnownRoles.StandardDetailsApprovers)
           || IsInRoleGroup(KnownRoles.StandardDetailsPublishers)
           || IsInRoleGroup(KnownRoles.StandardDetailsAdmins);

    internal bool CanApproveOrReject()
        => IsInRoleGroup(KnownRoles.StandardDetailsApprovers)
           || IsInRoleGroup(KnownRoles.StandardDetailsPublishers)
           || IsInRoleGroup(KnownRoles.StandardDetailsAdmins);

    internal bool CanPublish()
        => IsInRoleGroup(KnownRoles.StandardDetailsPublishers)
           || IsInRoleGroup(KnownRoles.StandardDetailsAdmins);
}
