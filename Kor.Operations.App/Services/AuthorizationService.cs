#nullable enable
using System;
using System.Collections.Generic;
using Kor.Operations.App.Options;
using Kor.Operations.App.Services;
using Kor.Operations.Core;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.Services;

internal interface ISecurityGroupAccess
{
    bool IsUserInGroup(string groupName, string? userIdentity);
}

internal sealed class SecurityGroupAccessAdapter : ISecurityGroupAccess
{
    public bool IsUserInGroup(string groupName, string? userIdentity)
        => SecurityGroupAccess.IsUserInGroup(groupName, userIdentity);
}

internal sealed class AuthorizationService : IAuthorizationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    // Preferences and TeamsPicker are personal-use windows open to all authenticated users.
    // Listed here so future access-gating can be added by moving them to OperationGroups.
    private static readonly HashSet<string> UnrestrictedOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Preferences",
        "TeamsPicker"
    };
    private static readonly Dictionary<string, string> OperationGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Financials"] = KnownRoles.Financials,
        ["PMTools"] = KnownRoles.PMTools,
        ["StandardDetails"] = KnownRoles.StandardDetails
    };

    private readonly ISecurityGroupAccess _securityGroupAccess;
    private readonly UserOptions _userOptions;
    private readonly ILogger<AuthorizationService> _logger;
    private readonly Dictionary<string, (bool Result, DateTime ExpiresAt)> _groupMembershipCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    public AuthorizationService(
        ISecurityGroupAccess securityGroupAccess,
        UserOptions userOptions,
        ILogger<AuthorizationService> logger)
    {
        _securityGroupAccess = securityGroupAccess ?? throw new ArgumentNullException(nameof(securityGroupAccess));
        _userOptions = userOptions ?? throw new ArgumentNullException(nameof(userOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsInGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return false;

        var now = DateTime.UtcNow;

        lock (_cacheLock)
        {
            if (_groupMembershipCache.TryGetValue(groupName, out var cached) && cached.ExpiresAt > now)
                return cached.Result;
        }

        var result = _securityGroupAccess.IsUserInGroup(groupName, ResolveCurrentUserIdentity());

        lock (_cacheLock)
        {
            _groupMembershipCache[groupName] = (result, now.Add(CacheTtl));
        }

        return result;
    }

    public bool IsAuthorized(string operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
            return false;

        if (UnrestrictedOperations.Contains(operation))
            return true;

        return OperationGroups.TryGetValue(operation, out var requiredGroup)
            ? IsInGroup(requiredGroup)
            : IsInGroup(operation);
    }

    public void Demand(string operation)
    {
        if (IsAuthorized(operation))
            return;

        _logger.LogWarning("Unauthorized access attempt: {Operation} by {User}", operation, Environment.UserName);
        throw new UnauthorizedAccessException($"You are not authorized to perform: {operation}");
    }

    private string ResolveCurrentUserIdentity()
    {
        var overrideUpn = _userOptions.UserUpnOverride;
        if (!string.IsNullOrWhiteSpace(overrideUpn))
            return overrideUpn.Trim();

        var user = Environment.UserName;
        if (user.Contains('\\'))
            user = user[(user.LastIndexOf('\\') + 1)..];

        return string.IsNullOrWhiteSpace(user) ? "unknown@korstructural.com" : $"{user}@korstructural.com";
    }
}
