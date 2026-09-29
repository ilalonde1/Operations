#nullable enable
namespace Kor.Operations.NetworkOps.Core.Probes;

// The probe scripts that run ON workstations, embedded in this assembly (see the csproj), so
// whatever runs a probe -- the netops CLI, the service -- runs exactly the text the rules were
// tested against.
public static class ProbeLibrary
{
    public const string Health = "health";
    public const string Hardware = "hardware";

    public static string Get(string name)
    {
        using var s = typeof(ProbeLibrary).Assembly.GetManifestResourceStream($"Probes.{name}.ps1")
            ?? throw new ArgumentException($"No embedded probe '{name}'.", nameof(name));
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
