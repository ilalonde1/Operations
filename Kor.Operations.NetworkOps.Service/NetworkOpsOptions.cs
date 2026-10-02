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

    // ---- the endpoint agent (Agents/). docs/runbooks/NetworkOps.Agent.md

    /// <summary>
    /// THE KILL SWITCH. False: no job is handed to any agent -- every check and fix takes the network route, exactly as
    /// before the agent existed -- and no agent is installed. Agents keep calling in and stay idle. Takes effect on a
    /// service restart (KOR_NETWORKOPS_AGENTSENABLED=false on APP01, or appsettings.json).
    /// </summary>
    public bool AgentsEnabled { get; set; } = true;

    /// <summary>Where APP01 sends Wake-on-LAN magic packets: the limited broadcast and the office subnet's.</summary>
    public List<string> WakeBroadcasts { get; set; } = ["255.255.255.255", "192.168.1.255"];

    // ---- Windows updates (Updates/*): searched twice a working day, installed only when someone asks.

    /// <summary>Machines searched at once. A search is 30 s to a few minutes and talks to Microsoft, not to APP01.</summary>
    public int UpdateScanParallel { get; set; } = 8;

    /// <summary>Installs running at once: each downloads hundreds of MB through the office's internet line.</summary>
    public int UpdateInstallParallel { get; set; } = 4;

    /// <summary>Hosts that may only be patched in a batch of their own (the only domain controller: DNS, DHCP and sign-in go with it).</summary>
    public List<string> UpdateAloneHosts { get; set; } = ["KOR-DC01"];

    /// <summary>Hosts never restarted from here (APP01 runs NetworkOps: it cannot restart itself and watch it come back).</summary>
    public List<string> UpdateNoRestartHosts { get; set; } = ["KOR-APP01"];

    /// <summary>How many PCs one rollout run installs or upgrades, one at a time, stopping at the first failure.</summary>
    public int AgentRolloutBatch { get; set; } = 5;

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

    /// <summary>The address a Claude session (on a KOR PC) reports its Prompt Library outcome to: this API, by its certificate's name.</summary>
    public string ApiPublicUrl { get; set; } = "https://KOR-APP01.int.korstructural.com:8445";

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

    // ---- the rack (Rack/*): every piece of infrastructure as a device with facts, metrics and findings,
    // read every 5 minutes by RackSweepJob through a least-privilege channel each.

    public List<RackDevice> Rack { get; set; } = [];

    /// <summary>Veeam Backup Viewer account on KOR-BK01 (KOR_NETWORKOPS_VEEAMUSER / _VEEAMPASSWORD): read-only, cannot start a job.</summary>
    public string VeeamUser { get; set; } = "";
    public string VeeamPassword { get; set; } = "";

    /// <summary>Key for `netops@KOR-UNIFI01`, whose only permitted command prints the UniFi status (KOR_NETWORKOPS_UNIFISTATUSKEYPATH).</summary>
    public string UniFiStatusKeyPath { get; set; } = "";

    /// <summary>The office's Shaw STATIC address: traffic leaving by any other means the firewall is on the wrong WAN.</summary>
    public string ExpectedPublicIp { get; set; } = "";
    public List<string> InternetPingTargets { get; set; } = [];

    // ---- remote control: MeshCentral on KOR-MESH01 (Mesh/*). Off unless MeshUrl, MeshCertSha256 and the password are set.

    /// <summary>The MeshCentral server, e.g. https://kor-mesh01.int.korstructural.com. The Connect button opens pages on it.</summary>
    public string MeshUrl { get; set; } = "";
    /// <summary>SHA-256 (hex) of MESH01's self-signed certificate: the only thing the service will talk to as MeshCentral.</summary>
    public string MeshCertSha256 { get; set; } = "";
    /// <summary>The read-only MeshCentral account (group membership, no device rights). Password: KOR_NETWORKOPS_MESHPASSWORD.</summary>
    public string MeshUser { get; set; } = "";
    public string MeshPassword { get; set; } = "";
    /// <summary>Device group ids ("mesh//..."): which group a PC's and a server's Mesh agent is installed into.</summary>
    public string MeshPcGroup { get; set; } = "";
    public string MeshServerGroup { get; set; } = "";

    public bool MeshEnabled => MeshUrl.Length > 0 && MeshCertSha256.Length > 0 && MeshUser.Length > 0 && MeshPassword.Length > 0;
}

/// <summary>One rack device and how to read it.</summary>
public sealed class RackDevice
{
    public string Name { get; set; } = "";
    /// <summary>Core.Rack.RackKinds: Host, Storage, UPS, Backup, Network, Internet.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Esxi | Synology | Veeam | UniFi | Internet | CoreSwitch | Ups.</summary>
    public string Collector { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>Synology only: warn below this % free on a volume; 0 for boxes holding thick LUNs (their volume is full by design).</summary>
    public int VolumeFreeWarnPct { get; set; } = 10;
    /// <summary>Ups only: the Ups[] card this device shows.</summary>
    public string UpsName { get; set; } = "";
    /// <summary>SSH host-key pins for Esxi/UniFi collectors ("SHA256:...").</summary>
    public List<string> HostKeys { get; set; } = [];
    /// <summary>Veeam only: SHA-256 (hex) of BK01's REST certificate. Pinned: the password never goes to anything else.</summary>
    public string CertSha256 { get; set; } = "";
    /// <summary>Its name in MeshCentral when that is not its Address (BK01 is read at 192.168.1.18, known to Mesh as KOR-BK01).</summary>
    public string MeshName { get; set; } = "";

    /// <summary>
    /// THE answer to "can APP01 run something on this rack device" (a fix, an update install, an update search, a Claude
    /// session's read, a remote-control install): a Windows server it reaches through Windows' service manager or the agent.
    /// Decided here once. On 2026-10-01 five places decided it with two different rules, so updates could be searched on
    /// FS01 and RDS01 but the Fix button refused to install them ("not a Windows machine"). RackRunnableTests holds it.
    /// </summary>
    public bool AppCanRunOn => Collector is "WindowsServer" or "Mesh";

    /// <summary>Its state is read from MeshCentral (RackCollector.RemoteOnly / MeshServer), so it cannot be judged before the
    /// service's first MeshCentral read.</summary>
    public bool JudgedFromMesh => Collector is "Mesh" or "MeshServer";
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
