using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Kor.Operations.NetworkOps.Agent;

/// <summary>
/// The agent's data folder holds its key and the scripts it is about to run as SYSTEM. Under ProgramData a new
/// folder lets ordinary users create files, which here would let anyone at the keyboard swap a script between
/// write and run, or read the key. So the folder is SYSTEM and Administrators only, inheritance cut. The
/// installer sets this from APP01; the agent sets it again at every start, so it cannot drift.
/// </summary>
internal static class DataFolder
{
    public static void Secure(string path)
    {
        var dir = Directory.CreateDirectory(path);
        dir.SetAccessControl(Locked());
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
}
