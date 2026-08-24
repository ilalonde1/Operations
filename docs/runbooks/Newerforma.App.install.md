# NewerForma (Kor.Operations.App) — workstation install runbook

Companion to `Newerforma.App.deploy.md` (which covers **publishing** a `V<N>.zip`).
This covers **installing** that package on a workstation. Install is manual, per machine.

## Source

`\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\V<N>.zip` (drive `L:` = `\KOR-FS01\Library`;
`L:` is not mapped in headless sessions — use the UNC).

## Steps

1. **Close the app** if running (and Outlook, if the EmailFilerv2 add-in is being updated).
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

`V8`/`V12` are ~301 MB (pre-`.playwright` cull); `V13` onward ~146–154 MB.

## Gotchas

- **Installs are not tracked.** Machines drift — KOR-206-N was still on V14 in Aug 2026,
  two versions behind. There is no inventory of which PC runs which version.
- The `V<N>` number is a **package counter**, not the assembly version — `Kor.Operations.App.exe`
  reports FileVersion `0.0.0.0`, so you cannot tell the installed version from the exe.
  Use the install date on `C:\Newerforma` as a proxy.
