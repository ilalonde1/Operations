using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Kor.Operations.NetworkOps.Agent;

/// <summary>
/// The agent's data folder holds its key and the scripts it is about to run as SYSTEM, so nobody but SYSTEM and
/// Administrators may read or write anything in it. Securing only the folder is not enough: a descendant that already
/// exists keeps its own owner and its own protected permissions (Codex audit 2026-09-30, finding 1). So at every start
/// the agent makes the WHOLE tree what it should be, and nothing else:
///   data\              owner Administrators, SYSTEM + Administrators only, inheritance cut
///   data\agent.key     inherits that (owner Administrators, no permissions of its own)
///   data\agent.log(.1) the same
///   data\work\         deleted and made again, empty, inheriting
/// Anything else found there is deleted; a link (junction, symlink) is deleted as a link, never followed. A data folder
/// that is itself a link is refused: the agent will not start on it.
/// </summary>
internal static class DataFolder
{
    private static readonly string[] Kept = ["agent.key", "agent.log", "agent.log.1"];

    public static void Secure(string path)
    {
        var dir = new DirectoryInfo(path);
        if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException($"{path} is a link, not a folder: refusing to keep a key or run scripts there");
        dir.Create();
        dir.SetAccessControl(Locked());

        foreach (var entry in dir.EnumerateFileSystemInfos())
        {
            var isLink = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
            if (entry is FileInfo f && !isLink && Kept.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
            {
                f.SetAccessControl(Inherited<FileSecurity>());
                continue;
            }
            Remove(entry, isLink);   // the work folder too: it is made again, empty, below
        }
        Directory.CreateDirectory(Path.Combine(path, "work"));
    }

    /// <summary>A link is removed as a link (its target is never touched); a real folder with everything in it.</summary>
    private static void Remove(FileSystemInfo entry, bool isLink)
    {
        if (entry is DirectoryInfo d)
        {
            if (isLink) { d.Delete(); return; }
            foreach (var child in d.EnumerateFileSystemInfos()) Remove(child, child.Attributes.HasFlag(FileAttributes.ReparsePoint));
            d.Attributes = FileAttributes.Directory;
            d.Delete();
        }
        else
        {
            entry.Attributes = FileAttributes.Normal;
            entry.Delete();
        }
    }

    public static DirectorySecurity Locked()
    {
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var who in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(who, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        return acl;
    }

    /// <summary>No permissions of its own, protection off: it takes exactly what the locked folder passes down.</summary>
    private static T Inherited<T>() where T : FileSystemSecurity, new()
    {
        var acl = new T();
        acl.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
        acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        return acl;
    }
}
