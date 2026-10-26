# KorConcrete 0.134.4 — Security Review

**Subject:** KorConcrete 0.134.4, Griffin Dow's concrete column and wall design app, as published to `\\Kor-fs01\Library\03 Programs\KorConcrete` on 2026-10-19

**For:** Griffin Dow

**Prepared by:** Ian Lalonde, Operations/IT — KOR Structural

**Date:** 2026-10-20

---

## 1. Summary

KorConcrete is well built from a security point of view. The update chain, the local engine's protection and the
installer all hold up, including under live attack on a test install. There are **two things to fix before the
team uses it**, both small:

1. **Lock down the install folder on FS01 and sign the installer.** Today 23 staff accounts can replace the
   installer, and the installer is what carries your update-signing key.
2. **Keep it off the terminal server (KOR-RDS01) until the app's front page stops serving the token.** On a shared
   server one user could drive another user's engine.

Everything else is either sound or minor housekeeping, listed below.

## 2. What was reviewed, and how

Every part of the product was reviewed. Nothing was sampled or skipped.

| Part | How it was reviewed |
|---|---|
| Installer (`Install KorConcrete.exe`) | Unpacked (Inno Setup 6.7.0); the setup script read in full; every installed file hashed and compared with the signed 0.134.4 update package |
| Launcher (`KorConcrete.ps1`, `Splash.cs`, `KorConcrete.vbs`) | Read in full |
| Splash program (`KorConcreteSplash.exe`) | Every string in the program traced back to `Splash.cs` |
| Engine (`KorConcrete-backend.exe`) | Unpacked; all 42 of your own modules disassembled and searched for risky calls (subprocess, pickle, eval, file and registry writes, network); the security module and every file-handling route read; all 38 routes listed |
| Bundled libraries | Version of every web-facing and file-parsing library read from the package |
| User interface | Both script bundles searched for outside calls and raw-HTML insertion |
| Live behaviour | Engine run on a test PC and attacked: missing and wrong tokens, forged Host headers, cross-site requests, path tricks, folder-name escapes |
| Install folder on FS01 | Permissions on the folder and the installer read; group membership counted |

## 3. Finding 1 — staff can write to the install folder (fix before rollout)

**What:** `\\Kor-fs01\Library\03 Programs\KorConcrete` gives *Full Control* to `BMZ_FS_Library_RW`. That group
contains `BMZ_ROL_Staff`, which is **23 enabled user accounts**. `Install KorConcrete.exe` itself is unsigned.

**Why it matters:** your update chain trusts one public key, and that key arrives inside the installer. Anyone who
can replace the installer can ship their own key, and from then on their own "signed" updates. The signature
protects updates only as well as the folder protects the installer.

**Fix:**

- Give write access to that folder to you and IT only. Everyone else reads. (IT can make this change.)
- Sign the installer with a code-signing certificate, so Windows shows *KOR Structural* as the publisher and refuses
  a tampered copy.

## 4. Finding 2 — terminal servers (your question)

**What:** the app's front page (`GET /`) hands the engine's token to anyone who asks for it. That is by design, so
the app window can use it.

**On a single-user PC this is harmless.** Only that user's own programs can reach 127.0.0.1, and they already run as
that user.

**On a terminal server it is not.** Every logged-in user shares the same 127.0.0.1. One user could read another
user's token and drive their engine, including `/section/import-path`, which accepts a network (UNC) path. Pointing
it at a network path makes the victim's session sign in to that address.

**Fix:** have the launcher pass the token to the app window directly, for example in the address after a `#`, which
the browser never sends to the server. The engine then never serves the token. **Until that is done, do not install
it on KOR-RDS01.** The answer to your question is yes: KOR has a terminal server, KOR-RDS01.

## 5. What is sound (tested)

**Update chain**

- Each update carries an RSA-3072 signature over its version, SHA-256 and size.
- Size and hash are checked before anything is unpacked.
- File names inside the zip are checked before extraction.
- The app never downgrades, and it rolls back an update that fails to start.

**Engine protection** — tested live on 0.134.4:

| Test | Result |
|---|---|
| Request with no token, or a wrong one | Refused, 401 |
| Forged Host header (DNS-rebinding attack) | Refused, 421 |
| Cross-site request from another website | No site is allowed. Your security layer runs first, so the browser never gets a readable answer |
| Path tricks to read files (`..`, `%2f`, `%5c`) | Refused; no file reached |
| Folder-name escapes in a design id (`..\..\`, `C:\`) | Refused by your name check; nothing written outside the data folder |
| Network listening | 127.0.0.1 only; not reachable from other machines |

Also confirmed:

- the token is 64 random hex characters, new on every start, and compared in constant time;
- security headers (CSP, no-sniff, no framing, no referrer) are on every response;
- exports come back to the window as data; the engine never writes to a path it is given.

**Installer**

- It requests no admin rights and installs to the user's own profile (`%LOCALAPPDATA%\Programs\KorConcrete`).
- Its only registry change is the per-user `.korcol` file association.
- Its only run steps are opening the app after install and stopping the engine on uninstall.
- All 179 of 179 files it installs are byte-identical to the signed 0.134.4 update package. The splash program and
  the icon are the two additions, and the splash program matches its source.

**Libraries** — all current, and past their known security fixes:

| Library | Version |
|---|---|
| FastAPI | 0.141.1 |
| Starlette | 1.6.0 |
| uvicorn | 0.52.1 |
| h11 | 0.16.0 |
| python-multipart | 0.0.32 |
| pydantic | 2.13.4 |
| numpy | 2.5.2 |
| openpyxl | 3.1.5 |

**User interface**

- Neither script bundle calls an outside address. The only web addresses in them are XML namespace names and React's
  error-help page.
- The code-clause navigator writes raw HTML only from text built into the app.
- Your CSP blocks any injected script regardless.

**Other code**

- `pickle` is used only to build a hash, never to load data.
- The only program the engine starts is a copy of itself, for the compiled-kernel self-check.

## 6. Minor housekeeping

- **Development setting:** the engine still allows cross-site requests from your development server
  (`localhost:5173`). It does nothing today, because the token check runs first, but it should be removed from the
  packaged build.
- **Spreadsheet parsing:** openpyxl reads Excel files with Python's built-in XML parser, which does not fetch external
  entities. Adding the `defusedxml` package, which openpyxl uses automatically when present, hardens it further.
- **Signing passphrase:** your email says Ian also knows it; your security report says only you do. Worth settling
  which is true, and writing down who holds it and where the private key is backed up.

## 7. Found while testing with a real model (not security)

We fed an ETABS model built by Ian's drawing tools straight into your engine, through the same calls your window
makes. The model was Omar's 143 E 3rd Hotel: 41 storeys, units LB/MM, walls carrying pier labels. Your engine
read all 40 storeys that carry walls, and our pier labels on each of them. Two things go wrong with the units:

1. **Lengths are read as feet.** A 3,239 × 305 mm wall on L19 (KPIER28) assembles as 987,177 × 92,903, which is
   304.8 times too large. The `.e2k` importer assumes feet whatever the file's `UNITS` line says. Most KOR models are
   in inches, and the drawing-tool models are in millimetres.
2. **Concrete strength.** The model's `FC 6.2000124` is in lb/mm², which is 27.6 MPa. The engine reads it as
   "6.2 MPa" and then sets it to 5 MPa.

Both are fixed by reading the `UNITS` line and converting. With that done, the two tools connect end to end: a
marked-up PDF becomes an ETABS model, the engineer runs the analysis, and KorConcrete designs the walls from the
pier forces.
