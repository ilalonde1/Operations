# KOR Remote All displays aspect — source response

Date: 2026-10-15

Implemented the three pieces as source edits. No build, test run, app execution, live-machine
access, apply, publish, deployment, or commit was performed.

## What changed

1. **Bridge script.** Added `fixAspect()` and its own `aspectFixed` flag. Inside the existing
   ready-desktop block in `tick()`, the flag is set before calling the helper, once per page.
   The helper checks that `deskAspectRatio` exists, assigns the bare global to `0` when needed,
   persists that change through `putstore('deskAspectRatio', 0)` when available, and calls
   `toggleAspectRatio(0)` when available to apply MeshCentral's own Fixed-aspect sizing.
   Both function calls are `typeof`-guarded and the entire helper is wrapped in `try/catch`:
   this remains cosmetic best-effort behavior. There is no direct `deskAdjust` call.

2. **Bridge contract.** Added the documented `OptionalFunctions` list containing exactly
   `putstore` and `toggleAspectRatio`. Missing optional functions do not enter the `Missing`
   path or disable the toolbar.

3. **Gate source.** Renamed the existing scan test from
   `The_script_calls_no_page_function_it_does_not_check_for` to the brief's
   `Every_page_function_the_script_calls_is_declared`. Its allowed set now includes
   `OptionalFunctions`; the call-scanning expression and undeclared-call assertion remain.
   Added `Optional_aspect_fix_calls_are_typeof_guarded`, asserting all three requested
   `typeof` guard strings. `The_script_and_the_window_depend_on_the_same_page_functions`
   is unchanged. These tests were authored, not run or demonstrated failing on pre-fix code.

## Deliberately unchanged / not done

- `addStyle()` and its CSS, `NEED`, and `RequiredFunctions` are unchanged. No `object-fit`
  styling or new required page dependency was added.
- No service, project, version, or other application files were changed. Existing unrelated
  untracked files were left untouched. No files were deleted.
- MeshCentral, databases, and remote machines were not opened. App installation and live
  aspect-ratio/mouse-mapping verification were not attempted.

## Verification and handoff

`git diff --check` is clean. Source diff review confirms the changes are limited to the three
named source files and this response; no build or test results are claimed.

Claude's remaining checks from the brief: build the App and App test project in Debug, run
`KorRemoteViewerTests`, install locally on KOR-1001, connect to KOR-206, and verify that
All displays preserves aspect and clicks land correctly. Check single-monitor behavior too.
Publish the App only on Ian's word. Nothing was committed.
