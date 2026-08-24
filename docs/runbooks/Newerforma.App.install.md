# NewerForma (Kor.Operations.App) — workstation install runbook

Companion to `Newerforma.App.deploy.md` (which covers **publishing** a `V<N>.zip`).
This covers **installing** that package on a workstation. Install is manual, per machine.

## Source

`\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\V<N>.zip` (drive `L:` = `\KOR-FS01\Library`;
`L:` is not mapped in headless sessions — use the UNC).

## Steps

1. **Close the app AND Outlook.** Both, every time — not just when the add-in changes.
   Outlook loads the EmailFilerv2 VSTO add-in *out of `C:\Newerforma`* and holds its DLLs open,
   which blocks step 2. See the Outlook lock gotcha below.
2. **Delete `C:\Newerforma`** on the local PC. This is a full replace, not an overlay —
   leftovers from a previous version cause mismatched-DLL failures.
3. **Copy `V<N>.zip`** to the local PC.
4. **Unzip so the package contents land at `C:\Newerforma\`.**

> ⚠️ **The zip wraps everything in a `V<N>\` folder.** Extracting the zip *into*
> `C:\Newerforma` produces `C:\Newerforma\V16\Kor.Operations.App.exe`, which is **wrong** —
> the desktop shortcut points at `C:\Newerforma\Kor.Operations.App.exe` and will fail.
> Extract, then move the **contents** of `V<N>\` up so the exe sits at the root.

Correct end state:

```
C:\Newerforma\Kor.Operations.App.exe          <- shortcut target
C:\Newerforma\Application Files\EmailFilerv2_1_0_0_49\
C:\Newerforma\setup.exe                        <- VSTO add-in installer
C:\Newerforma\Assets\  Brochures\  LatoFont\  cs\ da\ de\ ... (localisation)
```

## First time on a machine only

- **Machine environment variables** — run `SetEnvironmentVariables.ps1` from the share
  **elevated**, once. Sets `Machine`-scope vars (SQL, Graph, AzureAd, Deltek ODBC, API keys).
  Not needed for subsequent version upgrades unless the values change.
- **Desktop shortcut** — `KOR OPs.lnk` → target `C:\Newerforma\Kor.Operations.App.exe`,
  working directory `C:\Newerforma`.
- **EmailFilerv2 Outlook add-in** — run `setup.exe` from `C:\Newerforma\`. The add-in is a
  separate product carried forward unchanged in each package; it only needs reinstalling
  when the add-in itself changes, not on every app upgrade.

> 🔴 `SetEnvironmentVariables.ps1` holds **live secrets in cleartext** (two Entra client
> secrets, the Deltek ODBC password, SQL password, API keys) on a share readable by
> `BMZ_ROL_Staff` — i.e. all staff. Treat as an open security item; do not copy it around.

## Verify after installing

Launch the shortcut and drive **BD → Make a Brief → single-click an org → Generate**.
A build that compiles can still fail to launch, and this path exercises the single-click
typeahead commit plus a DB-backed brief.

## Rollback

The previous `V<N-1>.zip` is retained on the share. Roll back by repeating the steps above
with the older package.

## Version history on the share

`V8` (Jun 15) · `V12` (Jun 18) · `V13` (Jun 20) · `V14` (Jun 29) · `V15` (Jul 11) · `V16` (Aug 24)

**V16 (2026-08-24)** — first fleet-wide automated rollout: installed and verified on all 24
workstations. Prior to this the fleet was drifting (machines found on V14 and on June builds).

`V8`/`V12` are ~301 MB (pre-`.playwright` cull); `V13` onward ~146–154 MB.

## Automated fleet rollout

WinRM and RPC are blocked to workstations, so the rollout runs by staging a script over `c$` and
launching it through a transient service created with `sc.exe` (works over SMB where RPC does not).
Each machine then **pulls `V<N>.zip` from the share itself over the LAN** rather than the package
being pushed through an admin's VPN link — 24 machines completed in roughly 25 minutes.

The install script must, in this order:

1. Verify the share is readable and the zip's **SHA256 matches the published hash**
2. Extract to `C:\Newerforma_new` — *nothing is deleted until a good payload is on disk*
3. Stop `Kor.Operations.App`, `OUTLOOK`, `msedgewebview2`
4. Remove `C:\Newerforma` with retries (handles release lazily)
5. Move `C:\Newerforma_new\V<N>` to `C:\Newerforma`
6. **Refuse to report success** unless the exe exists and the file count is >= 900

Verify afterwards on the folder **date**, not the file count.

## Gotchas

- **Outlook locks the install folder.** Proven on KOR-204 during the V16 rollout: the delete
  partially succeeded, the move failed, and the machine was left with **760 of 941 files** — a
  broken install. Outlook (and its 38 live `msedgewebview2` child processes) held DLLs under
  `Application Files\`. **Stop `OUTLOOK` and `msedgewebview2` as well as `Kor.Operations.App`
  before deleting.**
- **File count does NOT identify the version.** V14 and V16 both contain **941 files**. The only
  reliable local indicator is the `C:\Newerforma` folder timestamp. This is why machines sat on
  V14 unnoticed — they looked complete.
- **Always stage before deleting.** Verify the hash and extract while the existing install is still
  intact. If anything fails after that point the payload is already on disk and the repair is a
  retry, not a re-download. This is what made KOR-204 recoverable.

- **Installs are not tracked.** Machines drift — KOR-206-N was still on V14 in Aug 2026,
  two versions behind. There is no inventory of which PC runs which version.
- The `V<N>` number is a **package counter**, not the assembly version — `Kor.Operations.App.exe`
  reports FileVersion `0.0.0.0`, so you cannot tell the installed version from the exe.
  Use the install date on `C:\Newerforma` as a proxy.
