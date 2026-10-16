# KOR Remote — "All displays" squeeze: put MeshCentral in Fixed-aspect, not Scale

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-NETWORKOPS-KORREMOTE-ALLDISPLAYS-ASPECT-RESPONSE.md`, in the
same format the other CODEX responses use (what changed, what was deliberately not done,
`git diff --check` clean, do not commit).

## Why this exists

In KOR Remote, choosing **"All displays"** stretches the picture — vertically tall / horizontally
squeezed (seen on a SAFE/ETABS installer dialog). Root cause, read live from MeshCentral 1.2.5 on
KOR-MESH01 (`/opt/meshcentral/node_modules/meshcentral/views/default.handlebars`):

- "All" is `deskSetDisplay(0)` — one combined, very wide framebuffer in the single `#Desk` canvas.
- `deskAdjust()` (default.handlebars ~line 10238) sizes `#Desk` by a global **`deskAspectRatio`**:
  - `== 2` **Scale mode** → `Desk.width='100%'; Desk.height='100%'` → **non-uniform stretch = the squeeze.**
  - `== 1` Zoomed; `== 0` **Fixed aspect** → computes width/height to preserve aspect (letterbox).
- `deskAspectRatio` is **persisted in localStorage** (`var deskAspectRatio = parseInt(getstore('deskAspectRatio','0'))`,
  ~line 1679-1680) and cycled only by `toggleAspectRatio(1)` (~line 2198), whose button
  (`&#8690;`, ~line 772) sits in the desktop toolbar **our bridge hides** — so once it got toggled
  to 2, there is no way to set it back from our viewer. It is stuck on stretch.

On a single monitor the aspect ≈ the window so it barely shows; the wide "All" framebuffer makes it
glaring. Fix = put MeshCentral into its **own Fixed-aspect mode (0)**, which keeps the canvas *box*
aspect-correct so MeshCentral's mouse mapping stays right. (An `object-fit: contain` CSS hack would
letterbox the image but leave the clicks mapped to the un-letterboxed box — do NOT do that.)

## The decisions, so you build the one thing

- **Set MeshCentral's own global, the supported way.** On the connected desktop page, once:
  `deskAspectRatio = 0;` then `putstore('deskAspectRatio', 0);` (persists it, matching `getstore`'s
  key scheme) then `toggleAspectRatio(0);` (its `toggle !== 1` branch changes nothing but re-runs
  `deskAdjust()`, applying it immediately). All **typeof-guarded and wrapped in try/catch** — this is
  a cosmetic best-effort, never a hard dependency.
- **These page functions are OPTIONAL, not `NEED`.** A MeshCentral update that renames `deskAdjust`,
  `putstore` or `toggleAspectRatio` must NOT turn the toolbar off (they are not in the `Missing`
  path). So they do NOT go in `RequiredFunctions`; they go in a new, separate `OptionalFunctions`
  list that the script-scan test allows.
- **`deskAspectRatio` stays a bare variable** (an assignment, no parens) so the script-scan test does
  not treat it as a called page function. Only the three functions are "calls".
- **Nothing else changes.** No CSS change to `addStyle()`. No new `NEED` entry. No object-fit.

## Pieces

### Piece 1 — `Kor.Operations.App/NetworkOps/KorRemoteBridge.js`

Add a best-effort helper and call it ONCE when the desktop page is ready (reuse the `autoConnected`
one-shot pattern in `tick()`, with its own flag e.g. `aspectFixed`). Shape:

```js
var aspectFixed = false;
// MeshCentral persists deskAspectRatio in localStorage; a stray toggle of its (now-hidden) view-mode
// button can leave it at 2 = Scale, which STRETCHES the canvas to 100%x100% -- squeezing the wide
// combined "All displays" image (and distorting any screen whose aspect != the window). Put it back to
// 0 = Fixed aspect (MeshCentral's own letterbox mode; keeps mouse mapping correct). Best-effort.
function fixAspect() {
    try {
        if (typeof deskAspectRatio === 'undefined') return;
        if (deskAspectRatio !== 0) { deskAspectRatio = 0; if (typeof putstore === 'function') putstore('deskAspectRatio', 0); }
        if (typeof toggleAspectRatio === 'function') toggleAspectRatio(0); // toggle!==1: just re-runs deskAdjust()
    } catch (e) { }
}
```

Call `fixAspect()` inside `tick()` in the same `if (s.page === 'desktop' && s.node && s.missing.length === 0)`
block, guarded by `aspectFixed` so it runs once per page (set `aspectFixed = true` when it runs).
Do not call it every tick. Keep `addStyle()` exactly as it is.

### Piece 2 — `Kor.Operations.App/NetworkOps/KorRemoteBridge.cs`

Add, beside `RequiredFunctions`, an optional list and a short doc comment:

```csharp
/// <summary>Page functions the script uses BEST-EFFORT (the aspect-ratio fix). Unlike RequiredFunctions, a
/// rename here does NOT turn the toolbar off -- the calls are typeof-guarded and purely cosmetic.</summary>
public static readonly IReadOnlyList<string> OptionalFunctions = ["putstore", "toggleAspectRatio"];
```

(`deskAdjust` is reached only inside `toggleAspectRatio`, not called by name in the script, so it need
not be listed — but if your final script calls `deskAdjust(` directly, add it here too. List exactly
the page functions the script calls by name.)

### Piece 3 — gate: `Kor.Operations.App/Kor.Transmittals.App.Tests/NetworkOps/KorRemoteViewerTests.cs`

- In `Every_page_function_the_script_calls_is_declared`, add `KorRemoteBridge.OptionalFunctions` to the
  `allowed` set (beside `own`, `browser`, `RequiredFunctions`). This keeps the "no undeclared page
  function" guard intact while admitting the aspect-fix calls.
- `The_script_and_the_window_depend_on_the_same_page_functions` stays as-is (NEED unchanged).
- Add one test: the optional calls are typeof-guarded — assert the script contains
  `"typeof putstore === 'function'"` and `"typeof toggleAspectRatio === 'function'"` (and
  `"typeof deskAspectRatio === 'undefined'"`), so the best-effort contract can't silently regress into
  a hard dependency.

## Claude's run after apply (not yours)

Build the App + App test project; run `KorRemoteViewerTests`; install the app locally on KOR-1001;
open a Connect to **KOR-206** (two monitors), switch to **All**, and LOOK: the image keeps its aspect
(no stretch; letterboxed if the window differs) AND a click lands where it is aimed; confirm a single
monitor is unaffected (still correct). Then publish on Ian's word (app only — no service change).

## Read bound

About 20 KB:
- `Kor.Operations.App/NetworkOps/KorRemoteBridge.js` (whole — ~110 lines) and
  `Kor.Operations.App/NetworkOps/KorRemoteBridge.cs` (whole — ~65 lines).
- `Kor.Operations.App/Kor.Transmittals.App.Tests/NetworkOps/KorRemoteViewerTests.cs` — the two tests
  named above (the class is otherwise unchanged).

Do not open MeshCentral, the live database, or any machine. This is a client-only (App) change — no
service, no csproj, no version bump needed (a cosmetic viewer fix; bump the App version only if you
touch anything versioned, otherwise leave it — Claude decides at install).

## Files

Edits only: `KorRemoteBridge.js`, `KorRemoteBridge.cs`, `KorRemoteViewerTests.cs`; the RESPONSE.
No new files, no deletes.

## Response

What changed per piece; confirm `addStyle()`/`NEED`/`RequiredFunctions` are unchanged and the fix is
typeof-guarded best-effort; the gate test now allows the optional calls; `git diff --check` clean. Do
not build, run, apply, deploy, or commit.
