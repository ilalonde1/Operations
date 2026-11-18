Checked 16 November 2026. The saved `CODEX-FRAMEWORK-INVENTORY-1-work/settings-entries.json` matches all 153 current hook entries: event, group/hook position, matcher and every hook field.

Repo names identify these settings files:

- `Framework`: `C:/VIsual Studio Projects/Framework/.claude/settings.json`
- `Operations`: `C:/VIsual Studio Projects/Operations/.claude/settings.json`
- `KOR.Drafter`: `C:/VIsual Studio Projects/KOR.Drafter/.claude/settings.json`

One entry means one object in a hook group's `hooks` array. Entries retain settings order within each repo; commands are verbatim JSON string values. `(no matcher)` means the matcher is omitted.

Script paths below are relative to `C:/VIsual Studio Projects/` unless absolute. `$CLAUDE_PROJECT_DIR` is resolved only in the script column and counts. Purposes paraphrase the script's own header/docstring within its first 40 lines. Executables have `not stated`: no script header was available, and their implementation was not inspected.

| Repo | Entries | SessionStart | PreToolUse | Stop | UserPromptSubmit | PostToolUse | FileChanged | PostToolUseFailure | PostCompact | SessionEnd |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Framework | 55 | 7 | 29 | 7 | 4 | 4 | 1 | 1 | 1 | 1 |
| Operations | 51 | 6 | 26 | 7 | 5 | 3 | 1 | 1 | 1 | 1 |
| KOR.Drafter | 47 | 6 | 22 | 7 | 5 | 3 | 1 | 1 | 1 | 1 |

| repo | event | matcher | command | script | purpose |
|---|---|---|---|---|---|
| Framework | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module Framework --part record --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module Framework --part state --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module Framework --part memory --page 1 --pages 1 --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module Framework --part board --owner Framework --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-start --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe enroll-check --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `SessionStart` | `(no matcher)` | `python -I "C:/VIsual Studio Projects/Framework/tools/framework_selftest.py" check-version` | `Framework/tools/framework_selftest.py` | Warn at session start when the Claude Code version changes. |
| Framework | `FileChanged` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe fresh --file-changed --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `PreToolUse` | `Write\|Edit\|MultiEdit\|NotebookEdit\|Bash\|PowerShell` | `python -I "C:/VIsual Studio Projects/Operations/tools/claude_run_sample_guard.py"` | `Operations/tools/claude_run_sample_guard.py` | Prevent removal or bypass of the sample-first stop without Ian's authorization. |
| Framework | `PreToolUse` | `Bash\|PowerShell` | `python -I "C:/VIsual Studio Projects/Framework/tools/commit_scope_guard.py" --hook` | `Framework/tools/commit_scope_guard.py` | Refuse commits carrying paths this session has not named. |
| Framework | `PreToolUse` | `Bash\|PowerShell` | `python -I "C:/VIsual Studio Projects/Framework/tools/commit_proof_guard.py" --hook` | `Framework/tools/commit_proof_guard.py` | Guard test-failure proof, review-sized commits, failure-dependent steps and explicit review SHAs. |
| Framework | `PreToolUse` | `Bash\|PowerShell\|SendMessage` | `python -I "C:/VIsual Studio Projects/Framework/tools/board_time_guard.py" --hook` | `Framework/tools/board_time_guard.py` | Refuse board evidence containing a time that has not happened yet. |
| Framework | `PreToolUse` | `Bash\|PowerShell\|Write\|Edit\|MultiEdit\|NotebookEdit` | `C:/KOR-Operations/kor/kor.exe enforce --module "Framework" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `PreToolUse` | `Bash\|PowerShell\|WebFetch\|mcp__.*` | `C:/KOR-Operations/kor/kor.exe stall --module "Framework" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_bash_guard.py"` | `Operations/tools/claude_bash_guard.py` | Block command mistakes this repository has already encountered and recorded in CLAUDE.md. |
| Framework | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_asked_guard.py"` | `Operations/tools/claude_asked_guard.py` | Refuse action classes the user did not ask for. |
| Framework | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_sql_schema_guard.py"` | `Operations/tools/claude_sql_schema_guard.py` | Refuse writes to SQL tables whose columns have not been inspected. |
| Framework | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Framework | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| Framework | `PreToolUse` | `Agent\|Bash\|PowerShell\|SendMessage` | `python -I "C:/VIsual Studio Projects/Framework/tools/plan_review_guard.py" --hook` | `Framework/tools/plan_review_guard.py` | Require a GO review for covered helper briefs, plan-changing messages and board-row insert files. |
| Framework | `PreToolUse` | `Write` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py" --content-search` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Framework | `PreToolUse` | `Write` | `python "C:/VIsual Studio Projects/Operations/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| Framework | `PreToolUse` | `Edit` | `python "C:/VIsual Studio Projects/Operations/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| Framework | `PreToolUse` | `Grep` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Framework | `PreToolUse` | `Glob` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Framework | `PreToolUse` | `Read` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Framework | `PreToolUse` | `SendMessage` | `python -I "C:/VIsual Studio Projects/Framework/tools/kor_handover.py" guard --hook` | `Framework/tools/kor_handover.py` | After handover, restrict the old session's messages to its successor or a HANDED OVER line. |
| Framework | `PreToolUse` | `SendMessage` | `python "C:/VIsual Studio Projects/Operations/tools/claude_look_guard.py"` | `Operations/tools/claude_look_guard.py` | Require a complete look at changes before claiming they are done. |
| Framework | `PreToolUse` | `SendMessage` | `python "C:/VIsual Studio Projects/Operations/tools/claude_instrument_guard.py"` | `Operations/tools/claude_instrument_guard.py` | Require a known-answer result from an instrument before reporting its zero result. |
| Framework | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_bash_guard.py"` | `Operations/tools/claude_bash_guard.py` | Block command mistakes this repository has already encountered and recorded in CLAUDE.md. |
| Framework | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_asked_guard.py"` | `Operations/tools/claude_asked_guard.py` | Refuse action classes the user did not ask for. |
| Framework | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_sql_schema_guard.py"` | `Operations/tools/claude_sql_schema_guard.py` | Refuse writes to SQL tables whose columns have not been inspected. |
| Framework | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Framework | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| Framework | `PreToolUse` | `*` | `C:/KOR-Operations/kor/kor.exe context --module "Framework" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `PreToolUse` | `Bash\|PowerShell\|Write\|Agent\|SendMessage` | `C:/KOR-Operations/kor/kor.exe recall --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `PreToolUse` | `Bash\|PowerShell\|Write\|Agent\|SendMessage` | `C:/KOR-Operations/kor/kor.exe repeat --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `Stop` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-check --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `Stop` | `(no matcher)` | `python "C:/VIsual Studio Projects/Operations/tools/claude_absence_guard.py"` | `Operations/tools/claude_absence_guard.py` | Require a reply claiming something is absent to show the check that proves it. |
| Framework | `Stop` | `(no matcher)` | `python "C:/VIsual Studio Projects/Operations/tools/claude_look_guard.py"` | `Operations/tools/claude_look_guard.py` | Require a complete look at changes before claiming they are done. |
| Framework | `Stop` | `(no matcher)` | `python "C:/VIsual Studio Projects/Operations/tools/claude_instrument_guard.py"` | `Operations/tools/claude_instrument_guard.py` | Require a known-answer result from an instrument before reporting its zero result. |
| Framework | `Stop` | `(no matcher)` | `python -I "C:/VIsual Studio Projects/Framework/tools/claude_board_guard.py"` | `Framework/tools/claude_board_guard.py` | Block final replies whose claimed board-row state contradicts the live board. |
| Framework | `Stop` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe context --module "Framework" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `Stop` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe recall --hook --push-back` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `UserPromptSubmit` | `(no matcher)` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prompt_recorder.py"` | `Operations/tools/claude_prompt_recorder.py` | Record user prompts for later authorization checks by the asked guard. |
| Framework | `UserPromptSubmit` | `(no matcher)` | `C:/KOR-Operations/cli/netops.exe signin-status --hook` | `C:/KOR-Operations/cli/netops.exe` | not stated |
| Framework | `UserPromptSubmit` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe context --module "Framework" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `UserPromptSubmit` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe recall --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `PostToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_bash_sample_guard.py"` | `Operations/tools/claude_bash_sample_guard.py` | Mark truncated command output as a sample that cannot support claims about the whole. |
| Framework | `PostToolUse` | `Bash` | `C:/KOR-Operations/kor/kor.exe capture-tool --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `PostToolUse` | `PowerShell` | `C:/KOR-Operations/kor/kor.exe capture-tool --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `PostToolUse` | `Grep` | `python "C:/VIsual Studio Projects/Operations/tools/claude_grep_sample_guard.py"` | `Operations/tools/claude_grep_sample_guard.py` | Mark truncated Grep results as samples, with shown and total counts when available. |
| Framework | `PostToolUseFailure` | `Bash\|PowerShell` | `C:/KOR-Operations/kor/kor.exe capture-tool --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `PostCompact` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-compact --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Framework | `SessionEnd` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-end --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module "Operations" --part record --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module "Operations" --part state --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module "Operations" --part memory --page 1 --pages 1 --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module "Operations" --part board --owner Operations --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-start --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe enroll-check --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `FileChanged` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe fresh --file-changed --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `PreToolUse` | `Write\|Edit\|MultiEdit\|NotebookEdit\|Bash\|PowerShell` | `python -I "$CLAUDE_PROJECT_DIR/tools/claude_run_sample_guard.py"` | `Operations/tools/claude_run_sample_guard.py` | Prevent removal or bypass of the sample-first stop without Ian's authorization. |
| Operations | `PreToolUse` | `Bash\|PowerShell` | `python -I "C:/VIsual Studio Projects/Framework/tools/commit_scope_guard.py" --hook` | `Framework/tools/commit_scope_guard.py` | Refuse commits carrying paths this session has not named. |
| Operations | `PreToolUse` | `Bash\|PowerShell\|SendMessage` | `python -I "C:/VIsual Studio Projects/Framework/tools/board_time_guard.py" --hook` | `Framework/tools/board_time_guard.py` | Refuse board evidence containing a time that has not happened yet. |
| Operations | `PreToolUse` | `Bash\|PowerShell\|Write\|Edit\|MultiEdit\|NotebookEdit` | `C:/KOR-Operations/kor/kor.exe enforce --module "Operations" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `PreToolUse` | `Bash\|PowerShell\|WebFetch\|mcp__.*` | `C:/KOR-Operations/kor/kor.exe stall --module "Operations" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `PreToolUse` | `Bash` | `python "$CLAUDE_PROJECT_DIR/tools/claude_bash_guard.py"` | `Operations/tools/claude_bash_guard.py` | Block command mistakes this repository has already encountered and recorded in CLAUDE.md. |
| Operations | `PreToolUse` | `Bash` | `python "$CLAUDE_PROJECT_DIR/tools/claude_asked_guard.py"` | `Operations/tools/claude_asked_guard.py` | Refuse action classes the user did not ask for. |
| Operations | `PreToolUse` | `Bash` | `python "$CLAUDE_PROJECT_DIR/tools/claude_sql_schema_guard.py"` | `Operations/tools/claude_sql_schema_guard.py` | Refuse writes to SQL tables whose columns have not been inspected. |
| Operations | `PreToolUse` | `Bash` | `python "$CLAUDE_PROJECT_DIR/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Operations | `PreToolUse` | `Bash` | `python "$CLAUDE_PROJECT_DIR/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| Operations | `PreToolUse` | `Write` | `python "$CLAUDE_PROJECT_DIR/tools/claude_prior_art_guard.py" --content-search` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Operations | `PreToolUse` | `Write` | `python "$CLAUDE_PROJECT_DIR/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| Operations | `PreToolUse` | `Edit` | `python "$CLAUDE_PROJECT_DIR/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| Operations | `PreToolUse` | `Grep` | `python "$CLAUDE_PROJECT_DIR/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Operations | `PreToolUse` | `Glob` | `python "$CLAUDE_PROJECT_DIR/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Operations | `PreToolUse` | `Read` | `python "$CLAUDE_PROJECT_DIR/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Operations | `PreToolUse` | `SendMessage` | `python "$CLAUDE_PROJECT_DIR/tools/claude_look_guard.py"` | `Operations/tools/claude_look_guard.py` | Require a complete look at changes before claiming they are done. |
| Operations | `PreToolUse` | `SendMessage` | `python "$CLAUDE_PROJECT_DIR/tools/claude_instrument_guard.py"` | `Operations/tools/claude_instrument_guard.py` | Require a known-answer result from an instrument before reporting its zero result. |
| Operations | `PreToolUse` | `PowerShell` | `python "$CLAUDE_PROJECT_DIR/tools/claude_bash_guard.py"` | `Operations/tools/claude_bash_guard.py` | Block command mistakes this repository has already encountered and recorded in CLAUDE.md. |
| Operations | `PreToolUse` | `PowerShell` | `python "$CLAUDE_PROJECT_DIR/tools/claude_asked_guard.py"` | `Operations/tools/claude_asked_guard.py` | Refuse action classes the user did not ask for. |
| Operations | `PreToolUse` | `PowerShell` | `python "$CLAUDE_PROJECT_DIR/tools/claude_sql_schema_guard.py"` | `Operations/tools/claude_sql_schema_guard.py` | Refuse writes to SQL tables whose columns have not been inspected. |
| Operations | `PreToolUse` | `PowerShell` | `python "$CLAUDE_PROJECT_DIR/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| Operations | `PreToolUse` | `PowerShell` | `python "$CLAUDE_PROJECT_DIR/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| Operations | `PreToolUse` | `*` | `C:/KOR-Operations/kor/kor.exe context --module "Operations" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `PreToolUse` | `Bash\|PowerShell\|Write\|Agent\|SendMessage` | `C:/KOR-Operations/kor/kor.exe recall --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `PreToolUse` | `Bash\|PowerShell\|Write\|Agent\|SendMessage` | `C:/KOR-Operations/kor/kor.exe repeat --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `Stop` | `(no matcher)` | `python "$CLAUDE_PROJECT_DIR/tools/claude_absence_guard.py"` | `Operations/tools/claude_absence_guard.py` | Require a reply claiming something is absent to show the check that proves it. |
| Operations | `Stop` | `(no matcher)` | `python "$CLAUDE_PROJECT_DIR/tools/claude_look_guard.py"` | `Operations/tools/claude_look_guard.py` | Require a complete look at changes before claiming they are done. |
| Operations | `Stop` | `(no matcher)` | `python "$CLAUDE_PROJECT_DIR/tools/claude_instrument_guard.py"` | `Operations/tools/claude_instrument_guard.py` | Require a known-answer result from an instrument before reporting its zero result. |
| Operations | `Stop` | `(no matcher)` | `python -I "C:/VIsual Studio Projects/Framework/tools/claude_board_guard.py"` | `Framework/tools/claude_board_guard.py` | Block final replies whose claimed board-row state contradicts the live board. |
| Operations | `Stop` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-check --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `Stop` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe context --module "Operations" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `Stop` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe recall --hook --push-back` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `UserPromptSubmit` | `(no matcher)` | `python "$CLAUDE_PROJECT_DIR/tools/claude_prompt_recorder.py"` | `Operations/tools/claude_prompt_recorder.py` | Record user prompts for later authorization checks by the asked guard. |
| Operations | `UserPromptSubmit` | `(no matcher)` | `python "$CLAUDE_PROJECT_DIR/tools/PlanDocCheck/check_plan_doc_current.py" owed --hook --ledger "C:/VIsual Studio Projects/KOR.Drafter/work/*/doc-owed.jsonl"` | `Operations/tools/PlanDocCheck/check_plan_doc_current.py` | Read the milestone/update ledger and report what the plan document owes. |
| Operations | `UserPromptSubmit` | `(no matcher)` | `C:/KOR-Operations/cli/netops.exe signin-status --hook` | `C:/KOR-Operations/cli/netops.exe` | not stated |
| Operations | `UserPromptSubmit` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe context --module "Operations" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `UserPromptSubmit` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe recall --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `PostToolUse` | `Bash` | `python "$CLAUDE_PROJECT_DIR/tools/claude_bash_sample_guard.py"` | `Operations/tools/claude_bash_sample_guard.py` | Mark truncated command output as a sample that cannot support claims about the whole. |
| Operations | `PostToolUse` | `Grep` | `python "$CLAUDE_PROJECT_DIR/tools/claude_grep_sample_guard.py"` | `Operations/tools/claude_grep_sample_guard.py` | Mark truncated Grep results as samples, with shown and total counts when available. |
| Operations | `PostToolUse` | `Bash\|PowerShell` | `C:/KOR-Operations/kor/kor.exe capture-tool --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `PostToolUseFailure` | `Bash\|PowerShell` | `C:/KOR-Operations/kor/kor.exe capture-tool --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `PostCompact` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-compact --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| Operations | `SessionEnd` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-end --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module "KOR.Drafter" --part record --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module "KOR.Drafter" --part state --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module "KOR.Drafter" --part memory --page 1 --pages 1 --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe start --module "KOR.Drafter" --part board --owner "Virtual Drafter" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-start --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `SessionStart` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe enroll-check --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `FileChanged` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe fresh --file-changed --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `PreToolUse` | `Write\|Edit\|MultiEdit\|NotebookEdit\|Bash\|PowerShell` | `python -I "C:/VIsual Studio Projects/Operations/tools/claude_run_sample_guard.py"` | `Operations/tools/claude_run_sample_guard.py` | Prevent removal or bypass of the sample-first stop without Ian's authorization. |
| KOR.Drafter | `PreToolUse` | `Bash\|PowerShell\|Write\|Edit\|MultiEdit\|NotebookEdit` | `C:/KOR-Operations/kor/kor.exe enforce --module "KOR.Drafter" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_bash_guard.py"` | `Operations/tools/claude_bash_guard.py` | Block command mistakes this repository has already encountered and recorded in CLAUDE.md. |
| KOR.Drafter | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_asked_guard.py"` | `Operations/tools/claude_asked_guard.py` | Refuse action classes the user did not ask for. |
| KOR.Drafter | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_sql_schema_guard.py"` | `Operations/tools/claude_sql_schema_guard.py` | Refuse writes to SQL tables whose columns have not been inspected. |
| KOR.Drafter | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| KOR.Drafter | `PreToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| KOR.Drafter | `PreToolUse` | `Write` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| KOR.Drafter | `PreToolUse` | `Write` | `python "C:/VIsual Studio Projects/Operations/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| KOR.Drafter | `PreToolUse` | `Edit` | `python "C:/VIsual Studio Projects/Operations/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| KOR.Drafter | `PreToolUse` | `Grep` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| KOR.Drafter | `PreToolUse` | `Glob` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| KOR.Drafter | `PreToolUse` | `Read` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| KOR.Drafter | `PreToolUse` | `SendMessage` | `python "C:/VIsual Studio Projects/Operations/tools/claude_look_guard.py"` | `Operations/tools/claude_look_guard.py` | Require a complete look at changes before claiming they are done. |
| KOR.Drafter | `PreToolUse` | `SendMessage` | `python "C:/VIsual Studio Projects/Operations/tools/claude_instrument_guard.py"` | `Operations/tools/claude_instrument_guard.py` | Require a known-answer result from an instrument before reporting its zero result. |
| KOR.Drafter | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_bash_guard.py"` | `Operations/tools/claude_bash_guard.py` | Block command mistakes this repository has already encountered and recorded in CLAUDE.md. |
| KOR.Drafter | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_asked_guard.py"` | `Operations/tools/claude_asked_guard.py` | Refuse action classes the user did not ask for. |
| KOR.Drafter | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_sql_schema_guard.py"` | `Operations/tools/claude_sql_schema_guard.py` | Refuse writes to SQL tables whose columns have not been inspected. |
| KOR.Drafter | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prior_art_guard.py"` | `Operations/tools/claude_prior_art_guard.py` | Require a search for existing artefacts and a read of a sibling before creating a recurring artefact. |
| KOR.Drafter | `PreToolUse` | `PowerShell` | `python "C:/VIsual Studio Projects/Operations/tools/claude_api_guard.py"` | `Operations/tools/claude_api_guard.py` | Prevent Anthropic API use in the BD work. |
| KOR.Drafter | `PreToolUse` | `*` | `C:/KOR-Operations/kor/kor.exe context --module "KOR.Drafter" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `PreToolUse` | `Bash\|PowerShell\|Write\|Agent\|SendMessage` | `C:/KOR-Operations/kor/kor.exe recall --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `Stop` | `(no matcher)` | `python "C:/VIsual Studio Projects/Operations/tools/claude_absence_guard.py"` | `Operations/tools/claude_absence_guard.py` | Require a reply claiming something is absent to show the check that proves it. |
| KOR.Drafter | `Stop` | `(no matcher)` | `python "C:/VIsual Studio Projects/Operations/tools/claude_look_guard.py"` | `Operations/tools/claude_look_guard.py` | Require a complete look at changes before claiming they are done. |
| KOR.Drafter | `Stop` | `(no matcher)` | `python "C:/VIsual Studio Projects/Operations/tools/claude_instrument_guard.py"` | `Operations/tools/claude_instrument_guard.py` | Require a known-answer result from an instrument before reporting its zero result. |
| KOR.Drafter | `Stop` | `(no matcher)` | `python -I "C:/VIsual Studio Projects/Framework/tools/claude_board_guard.py"` | `Framework/tools/claude_board_guard.py` | Block final replies whose claimed board-row state contradicts the live board. |
| KOR.Drafter | `Stop` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-check --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `Stop` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe context --module "KOR.Drafter" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `Stop` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe recall --hook --push-back` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `UserPromptSubmit` | `(no matcher)` | `python "C:/VIsual Studio Projects/Operations/tools/claude_prompt_recorder.py"` | `Operations/tools/claude_prompt_recorder.py` | Record user prompts for later authorization checks by the asked guard. |
| KOR.Drafter | `UserPromptSubmit` | `(no matcher)` | `python "C:/VIsual Studio Projects/Operations/tools/PlanDocCheck/check_plan_doc_current.py" owed --hook --ledger "C:/VIsual Studio Projects/KOR.Drafter/work/*/doc-owed.jsonl"` | `Operations/tools/PlanDocCheck/check_plan_doc_current.py` | Read the milestone/update ledger and report what the plan document owes. |
| KOR.Drafter | `UserPromptSubmit` | `(no matcher)` | `C:/KOR-Operations/cli/netops.exe signin-status --hook` | `C:/KOR-Operations/cli/netops.exe` | not stated |
| KOR.Drafter | `UserPromptSubmit` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe context --module "KOR.Drafter" --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `UserPromptSubmit` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe recall --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `PostToolUse` | `Bash` | `python "C:/VIsual Studio Projects/Operations/tools/claude_bash_sample_guard.py"` | `Operations/tools/claude_bash_sample_guard.py` | Mark truncated command output as a sample that cannot support claims about the whole. |
| KOR.Drafter | `PostToolUse` | `Grep` | `python "C:/VIsual Studio Projects/Operations/tools/claude_grep_sample_guard.py"` | `Operations/tools/claude_grep_sample_guard.py` | Mark truncated Grep results as samples, with shown and total counts when available. |
| KOR.Drafter | `PostToolUse` | `Bash\|PowerShell` | `C:/KOR-Operations/kor/kor.exe capture-tool --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `PostToolUseFailure` | `Bash\|PowerShell` | `C:/KOR-Operations/kor/kor.exe capture-tool --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `PostCompact` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-compact --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |
| KOR.Drafter | `SessionEnd` | `(no matcher)` | `C:/KOR-Operations/kor/kor.exe capture-end --hook` | `C:/KOR-Operations/kor/kor.exe` | not stated |

Same target across repos, counted by resolved script/executable path: **15 in all three repos; 7 in some only** (3 in two repos; 4 in one). The 15 shared targets comprise 13 Python scripts and 2 executables; the 7 others are Python scripts. Different executable subcommands count toward the same executable.

Entries per script/executable:

| Script/executable | Framework | Operations | KOR.Drafter | Total |
|---|---:|---:|---:|---:|
| `C:/KOR-Operations/cli/netops.exe` | 1 | 1 | 1 | 3 |
| `C:/KOR-Operations/kor/kor.exe` | 22 | 21 | 19 | 62 |
| `Framework/tools/board_time_guard.py` | 1 | 1 | 0 | 2 |
| `Framework/tools/claude_board_guard.py` | 1 | 1 | 1 | 3 |
| `Framework/tools/commit_proof_guard.py` | 1 | 0 | 0 | 1 |
| `Framework/tools/commit_scope_guard.py` | 1 | 1 | 0 | 2 |
| `Framework/tools/framework_selftest.py` | 1 | 0 | 0 | 1 |
| `Framework/tools/kor_handover.py` | 1 | 0 | 0 | 1 |
| `Framework/tools/plan_review_guard.py` | 1 | 0 | 0 | 1 |
| `Operations/tools/claude_absence_guard.py` | 1 | 1 | 1 | 3 |
| `Operations/tools/claude_api_guard.py` | 4 | 4 | 4 | 12 |
| `Operations/tools/claude_asked_guard.py` | 2 | 2 | 2 | 6 |
| `Operations/tools/claude_bash_guard.py` | 2 | 2 | 2 | 6 |
| `Operations/tools/claude_bash_sample_guard.py` | 1 | 1 | 1 | 3 |
| `Operations/tools/claude_grep_sample_guard.py` | 1 | 1 | 1 | 3 |
| `Operations/tools/claude_instrument_guard.py` | 2 | 2 | 2 | 6 |
| `Operations/tools/claude_look_guard.py` | 2 | 2 | 2 | 6 |
| `Operations/tools/claude_prior_art_guard.py` | 6 | 6 | 6 | 18 |
| `Operations/tools/claude_prompt_recorder.py` | 1 | 1 | 1 | 3 |
| `Operations/tools/claude_run_sample_guard.py` | 1 | 1 | 1 | 3 |
| `Operations/tools/claude_sql_schema_guard.py` | 2 | 2 | 2 | 6 |
| `Operations/tools/PlanDocCheck/check_plan_doc_current.py` | 0 | 1 | 1 | 2 |
| **Total** | **55** | **51** | **47** | **153** |

