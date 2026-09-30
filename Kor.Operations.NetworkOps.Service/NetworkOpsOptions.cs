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

    // ---- UniFi controller backups (Jobs/UniFiBackupJob.cs): pulled nightly from KOR-UNIFI01's read-only
    // SFTP drop to FS01. Off unless UniFiBackupHost is set. The key file is on APP01 only.
    public string UniFiBackupHost { get; set; } = "";
    public string UniFiBackupUser { get; set; } = "korbackup";
    /// <summary>OpenSSH private key (KOR_NETWORKOPS_UNIFIBACKUPKEYPATH): readable by the service account only.</summary>
    public string UniFiBackupKeyPath { get; set; } = "";
    /// <summary>The controller's ED25519 host key, "SHA256:..." as ssh-keygen -lf prints it: pinned, never learned.</summary>
    public string UniFiBackupHostKeySha256 { get; set; } = "";
    public string UniFiBackupDestination { get; set; } = "";
    public int UniFiBackupKeepDays { get; set; } = 90;
    public int UniFiBackupStaleHours { get; set; } = 48;

    // ---- rack power (Power/*): watch both UPS cards over SNMPv3; on a real outage run the shutdown
    // chain. Off unless Ups is configured and the SNMP credentials are set.

    public List<UpsCard> Ups { get; set; } = [];

    /// <summary>SNMPv3 read-only user for every device: KOR_NETWORKOPS_SNMPUSER / _SNMPAUTHPASSWORD / _SNMPPRIVPASSWORD.</summary>
    public string SnmpUser { get; set; } = "";
    public string SnmpAuthPassword { get; set; } = "";
    public string SnmpPrivPassword { get; set; } = "";

    public int PowerPollSeconds { get; set; } = 10;
    /// <summary>A reading is stored this often, and on every change of source or reachability.</summary>
    public int PowerRecordSeconds { get; set; } = 60;
    public int PowerOnBatteryMinutes { get; set; } = 5;
    public int PowerRuntimeFloorMinutes { get; set; } = 15;
    public int PowerBlindAfterSeconds { get; set; } = 120;

    /// <summary>
    /// FALSE until the chain has been proven by pulling the plug in a maintenance window. While false, a
    /// real outage runs the chain as a DRY RUN: every step is checked and logged, nothing is shut down.
    /// </summary>
    public bool PowerChainArmed { get; set; }

    /// <summary>OpenSSH private key for root on the ESXi hosts (KOR_NETWORKOPS_ESXIKEYPATH): readable by the service account only.</summary>
    public string EsxiKeyPath { get; set; } = "";

    /// <summary>Host address -> its pinned host-key fingerprints ("SHA256:..."). A host that presents anything else is refused.</summary>
    public Dictionary<string, List<string>> EsxiHostKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Kor.Operations.NetworkOps.Core.Power.ShutdownPlanOptions PowerChain { get; set; } = new();
}

public sealed class UpsCard
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>UpsMib (RFC 1628, the Eaton) or PowerNet (the APC).</summary>
    public string Mib { get; set; } = Kor.Operations.NetworkOps.Core.Power.UpsMibs.UpsMib;
    /// <summary>SHA-256 authentication (the Eaton); false = SHA-1, all the APC offers.</summary>
    public bool AuthSha256 { get; set; }
}
