#nullable enable
namespace Kor.Operations.NetworkOps.Service;

// Non-secret settings come from appsettings.json; secrets ONLY from KOR_NETWORKOPS_* machine
// environment variables on KOR-APP01 (house rule: committed config is secret-free).
//   KOR_NETWORKOPS_DB            connection string to KorNetworkOps (SQL login networkops_app)
//   KOR_NETWORKOPS_TENANTID / KOR_NETWORKOPS_CLIENTID / KOR_NETWORKOPS_CLIENTSECRET
//                                 Graph app registration used to send the alert digest
public sealed class NetworkOpsOptions
{
    public string Db { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Off on first deploy: digests are written to ProgramData instead of mailed, so a week of
    /// them can be read before anyone gets an email. Flip to true once they read right.
    /// </summary>
    public bool AlertsEnabled { get; set; }
    public string AlertFromAddress { get; set; } = "ilalonde@korstructural.com";
    public string AlertRecipient { get; set; } = "ilalonde@korstructural.com";

    public int HeartbeatSeconds { get; set; } = 60;
    public int ParallelProbes { get; set; } = 16;
    public int ProbeTimeoutSeconds { get; set; } = 300;
    public int ObservationRetentionDays { get; set; } = 90;

    /// <summary>A directory machine not reachable for this long is itself a finding: silence is a finding.</summary>
    public int SilentAfterDays { get; set; } = 7;
}
