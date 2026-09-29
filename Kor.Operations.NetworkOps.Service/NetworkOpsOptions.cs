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

    // ---- the Command Center API (Api/ApiHost.cs). Off unless ApiPort AND ApiCertThumbprint are set.
    // Entra: every request needs a token for ApiAudience from ApiTenantId carrying scope
    // NetworkOps.Access and role NetworkOps.Admin; Conditional Access requires MFA to get one.

    /// <summary>HTTPS port on all interfaces; 0 = no API. The Windows firewall rule limits who can reach it.</summary>
    public int ApiPort { get; set; }

    /// <summary>SHA-1 thumbprint of the certificate in LocalMachine\My (KOR_NETWORKOPS_APICERTTHUMBPRINT: it is per machine).</summary>
    public string ApiCertThumbprint { get; set; } = "";

    public string ApiTenantId { get; set; } = "";

    /// <summary>App (client) id of "KOR NetworkOps API".</summary>
    public string ApiAudience { get; set; } = "";
}
