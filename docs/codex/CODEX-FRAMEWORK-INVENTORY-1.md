# CODEX increment 1: list the Claude Code hooks (small, facts only)

Rewritten 16 Nov 2026, 13:2x. The first version asked for six classes across all the code and was stopped as too big.

Write ONE file: `docs/codex/CODEX-FRAMEWORK-INVENTORY-1-RESPONSE.md`. Edit nothing else, and commit nothing.

## The task

List every hook entry in these three files, and nothing else:

- `C:\VIsual Studio Projects\Framework\.claude\settings.json`
- `C:\VIsual Studio Projects\Operations\.claude\settings.json`
- `C:\VIsual Studio Projects\KOR.Drafter\.claude\settings.json`

`docs/codex/CODEX-FRAMEWORK-INVENTORY-1-work/settings-entries.json` was already extracted from them by your stopped run. Use it if it matches the files; check that it does.

Expected counts, read 16 Nov 13:0x:

| Repo | Entries | SessionStart | PreToolUse | Stop | UserPromptSubmit | PostToolUse | FileChanged, PostToolUseFailure, PostCompact, SessionEnd |
|---|---|---|---|---|---|---|---|
| Framework | 55 | 7 | 29 | 7 | 4 | 4 | 1 each |
| Operations | 51 | 6 | 26 | 7 | 5 | 3 | 1 each |
| KOR.Drafter | 47 | 6 | 22 | 7 | 5 | 3 | 1 each |

## One row per entry

| Field | What goes in it |
|---|---|
| repo | which settings file |
| event | the hook event |
| matcher | the tool matcher, if any |
| command | the command, verbatim |
| script | the script or exe it runs |
| purpose | one line, read from that script's own header or docstring. Only open the first 40 lines of the script. If the header does not say, write "not stated". |

Then two counts:
- the same script wired in all three repos, versus in some only;
- entries per script.

## Limits

- Do NOT read beyond the first 40 lines of any script.
- Do NOT search the codebase, build call graphs, or look at product code.
- Budget: about 15 minutes. If you are going past that, stop and write what you have.
