# Newerforma — auto-updating workstation install

Replaces the manual "copy `V<N>.zip` to `C:\Newerforma` and unzip" step. Workstations pull the newest
published version themselves on each launch.

## The two pieces
- **`Install-Newerforma.ps1`** — run once per workstation (or push via login script / RMM). Drops the
  launcher, makes Start Menu + Desktop shortcuts, and does the first install pull.
- **`Launch-Newerforma.ps1`** — the launcher the shortcut runs. Each launch: finds the newest
  `V<N>.zip` on the share, and if `C:\Newerforma\V<N>` isn't already there, extracts it, prunes old
  versions (keeps the newest 2), then starts the app. Offline-tolerant and lock-tolerant.

## Install on a workstation
```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Newerforma.ps1
# locked-down C:\ ?  ->  -InstallRoot "$env:LOCALAPPDATA\Newerforma"
```

## Keeping everyone current (the whole point)
1. Build + publish a new version as usual: `tools\deploy-newerforma-app.ps1 -Version <N>` →
   `V<N>.zip` on the share.
2. That's it. Every workstation self-updates to `V<N>` on its next launch. No per-machine copy/unzip.

## Notes
- **Location / permissions:** default install root is `C:\Newerforma` (your current convention). The
  launcher runs as the logged-in user, so that user needs *Modify* on `C:\Newerforma`. If it's
  admin-only, point `-InstallRoot` at `%LOCALAPPDATA%\Newerforma` (always user-writable).
- **Offline:** if the share is unreachable, it launches whatever version is already installed.
- **In use during an update:** a running exe is never overwritten — a newer version installs into its
  own `V<N>` folder and takes effect next launch.
- **Share path:** `\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New` (override with `-Share`).
