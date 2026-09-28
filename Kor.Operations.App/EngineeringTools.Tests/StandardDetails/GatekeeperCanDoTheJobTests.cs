using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// The person who runs the standard-details catalogue has to be in the groups that unlock the job.
///
/// WHY THIS IS CHECKED. Every governed control in the module is hidden or disabled by
/// StandardDetailsAccessPolicy, and a missing membership does not fail — it just renders an app with
/// the buttons gone. Lindsay was handed the gatekeeper role while being in NONE of the five groups,
/// which would have looked exactly like the feature not existing.
///
/// WHAT IT COVERS: that the App.config that ships actually lists the gatekeeper in the three groups
/// her role needs, matched the same way SecurityGroupAccess matches — full identity or the part
/// before the '@', case-insensitive, separators ; , tab or newline.
/// WHAT IT DOES NOT COVER: whether the address is the one Windows resolves for her at runtime (that
/// comes from Environment.UserName or a UPN override on her machine, not from here); whether anyone
/// else's membership is right; and anything about the KorStandards SQL logins, which are a separate
/// gate entirely — being in these groups does not grant a single database permission.
///
/// A same-class fault it would NOT catch: the group key itself being renamed in both App.config and
/// KnownRoles at once, which would leave this passing while every real user silently lost access.
/// </summary>
public sealed class GatekeeperCanDoTheJobTests
{
    private const string Gatekeeper = "lfinnigan@korstructural.com";

    [Theory]
    // Seeing the module at all, then the two that unlock the verbs.
    [InlineData("StandardDetails")]
    // Approve/reject, and from migration 102: Add detail, Edit, Retire. Also satisfies CanContribute.
    [InlineData("StandardDetailsApprovers")]
    // Releasing a detail to MASTER — without it she could approve a detail and never ship it.
    [InlineData("StandardDetailsPublishers")]
    public void The_gatekeeper_is_in_the_group_that_unlocks_her_job(string group)
    {
        var members = MembersOf($"SecurityGroup.{group}.Members");

        Assert.True(members.Length > 0, $"SecurityGroup.{group}.Members is missing or empty in App.config.");

        // SecurityGroupAccess accepts the full identity or the local part, so accept either here.
        var local = Gatekeeper[..Gatekeeper.IndexOf('@')];
        var found = members.Any(x =>
            string.Equals(x, Gatekeeper, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x, local, StringComparison.OrdinalIgnoreCase));

        Assert.True(found,
            $"{Gatekeeper} is not in SecurityGroup.{group}.Members, so the Standard Details buttons that "
            + $"group unlocks will simply be absent for her. Present: {string.Join(", ", members)}");
    }

    private static string[] MembersOf(string key)
    {
        var config = XDocument.Load(Path.Combine(AppRoot(), "App.config"));
        var raw = config.Descendants("add")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("key"), key, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("value")?.Value;

        return string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(new[] { ';', ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                 .Select(x => x.Trim())
                 .Where(x => x.Length > 0)
                 .ToArray();
    }

    /// <summary>Anchored on the csproj, not a folder name — see ButtonHeightFitsItsTemplateTests.</summary>
    private static string AppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate Kor.Operations.App.csproj from the test output directory.");
        return dir!.FullName;
    }
}
