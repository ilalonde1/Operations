## What remote control is
MeshCentral on KOR-MESH01 (https://kor-mesh01.int.korstructural.com, "KOR Remote"): shared screen with a notice and
toolbar for the user, RDP through the same page for unattended PCs, terminal and files. The Command Center's Connect
button opens a device's desktop there. Build kit and every setting: `infra/vms/kor-mesh01/README.md`.

## Where it lives
- `Transport/MeshCentralClient.cs` -- reads the device list as the read-only `networkops` account (no device rights),
  over MeshCentral's control channel, certificate pinned.
- `Service/Mesh` -- MeshSweepJob (every 5 min), MeshState (live), MeshMap (node -> device), MeshInstaller + the
  embedded `install-mesh.ps1` (action kind `install-mesh`, through the agent or the network route, Done only when
  MeshCentral lists the device connected). Stored in `NetworkOps.MeshNodes` (migration 006).
- Findings `mesh-missing`, `mesh-silent` (Core/Health/MeshRules.cs); rack collectors `Mesh` (FS01/RDS01, remote only)
  and `MeshServer` (KOR-MESH01 itself).

## Things learned the hard way
- `conn` is a set of flags: 1 = agent connected, 4 = Intel AMT. 5 is connected. Counting only 1 once read 12 PCs as down.
- `force2factor` does not block the password sign-in; it blocks every feature until an authenticator is enrolled.
- A PC whose monitors are DisplayPort and switched off has NO display to Windows (1024x768 fallback): the shared
  screen is black. Use RDP from the same page. 4 of 29 PCs behave like this (208-N, 210, 302N, 304).
- The VM's static MAC must be read back after the deploy: a vpx-type MAC is dropped by the vSwitch (hit twice).
