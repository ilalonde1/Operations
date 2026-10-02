KOR's office network, as built (kept beside NetworkOps' code; change it in the same commit as what it describes).

- **LAN** 192.168.1.0/24, domain `int.korstructural.com`. DHCP hands out .51-.199; .1-.50 are static. Remote staff reach it over the VPN (10.0.254.0/24).
- **Firewall** Netgate 4200 at 192.168.1.1. Office public IP 184.71.160.54 (check it from a LAN-only box, never from a PC that also has home Wi-Fi).
- **KOR-DC01** 192.168.1.30: domain controller, DNS, and Windows DHCP (scope 192.168.1.0). A DHCP lease marked Active means "not expired", NOT "the device is on".
- **KOR-APP01** 192.168.1.32: NetworkOps (service, API :8445, SQL `KOR-APP01\SQLEXPRESS`, databases KorNetworkOps and KorStandards), the MCP /ask service (:5500), Deltek integrations. It is the machine every read and fix runs FROM.
- **KOR-FS01**: file server. `\\Kor-fs01\Projects\Projects` = `E:\Projects\Projects` on the server; home folders under `E:\Home`. Never walk the share recursively from outside: run on FS01.
- **KOR-RDS01**: remote desktop server. **KOR-BK01** 192.168.1.18: Veeam (workgroup, not domain). **KOR-MESH01** 192.168.1.27: MeshCentral remote control ("KOR Remote"). **KOR-UNIFI01** 192.168.1.26: the UniFi controller.
- **Virtualisation**: ESXi hosts 192.168.1.10 and .16, vCenter .9; every VM's disks are on the Synology UC3200 SAN (.12). Backup NASes: NAS01 (.15) and Synology02 (.105).
- **Switching**: core EdgeSwitch ES-16-XG (.11); UniFi switches BMZ-SW01 (.106), BMZ-SW02 (.57, the hub most things hang off), BMZ-SW03 (.99), plus small Flex switches. BMZ is KOR's old name.
- **Power**: two UPSes (APC .101, Eaton .44); NetworkOps watches both and holds a shutdown chain (dry run only, not armed).
- **PCs**: ~40 Windows 10/11 workstations named KOR-*, most with the NetworkOps agent and a Mesh agent. Engineers run Revit, AutoCAD, ETABS/SAFE (CSI), Bluebeam, Tekla. Two Perform boxes (KOR-PERFORM2/3) do analysis runs.
- **Who manages what**: KOR runs its own IT (Ian). The former MSP (Tenacious / T-Net) does no patching or monitoring; it still holds Veeam off-site copies and AV licensing (Webroot).
