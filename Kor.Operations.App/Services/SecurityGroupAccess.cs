#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace Kor.Operations.Services
{
    internal static class SecurityGroupAccess
    {
        public static bool IsUserInGroup(string groupName, string? userIdentity)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return true;

            var key = $"SecurityGroup.{groupName}.Members";
            var raw = ConfigurationManager.AppSettings[key];

            // No configured members means "unrestricted" to preserve existing behavior.
            if (string.IsNullOrWhiteSpace(raw))
                return true;

            var members = raw
                .Split(new[] { ';', ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (members.Count == 0)
                return true;

            if (members.Contains("*"))
                return true;

            var normalizedUser = Normalize(userIdentity);
            if (normalizedUser.Length == 0)
                return false;

            if (members.Contains(normalizedUser))
                return true;

            var userPart = GetUserPart(normalizedUser);
            return userPart.Length > 0 && members.Contains(userPart);
        }

        private static string Normalize(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string GetUserPart(string identity)
        {
            var at = identity.IndexOf('@');
            if (at > 0)
                return identity[..at];

            var slash = identity.LastIndexOf('\\');
            if (slash >= 0 && slash < identity.Length - 1)
                return identity[(slash + 1)..];

            return identity;
        }
    }
}
