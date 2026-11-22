# CODEX bash guard rule 3b: keep catching an escape Python or bash will change, stop refusing text written verbatim

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by a Claude session (Framework coordinator fa93ad77, 21 Nov 2026), and Claude sessions are the ones this
guard refuses. Ian asked that every Codex brief from a Claude session say so up front (#133998). Treat everything below
as claims. Verify each against the code, the transcripts and the guard's own tests, and say where the framing is
wrong. You write the cases, not the author.

## The goal, which must stay covered

Operations `CLAUDE.md` rule 7: "Never write a C# regex through a non-raw Python string." `\b` in a Python string is a
backspace (U+0008); the C# then holds an invisible control character, the pattern matches nothing, and every part tests
fine alone. Same class: a Windows path mangled by Python escapes (`\6`, `\v`, `\0` octal, `\U` a SyntaxError) and a UNC
prefix collapsed by bash in an UNQUOTED heredoc. The guard is `tools/claude_bash_guard.py`, rule 3b, class
`HeredocEscape` (top of the file) and its comment block (search "NARROWED 12 Nov (FW-RESET M8, RESET-PLAN item 3)").
Read both first: they state what it checks, allows and does not catch. Ian has ruled that this rule is narrowed, not
cut: a cut leaves rule 7 with no check.

## Evidence (claims: open each one yourself)

oversight.GuardRefusal, Guard `bash`, Rule starting `A backslash escape inside a heredoc`: 119 refusals since the 12 Nov
narrowing (12 Nov 15:45 to 21 Nov 11:47 local). A worker session judged 23 of them on 21 Nov: 0 right, 23 wrong.
`RefusedAtUtc` holds LOCAL time (UTC-7). `Matched` is cut at about 400 characters, so recover each full command from
its transcript: `C:\Users\ilalonde\.claude\projects\<folder>\<session>.jsonl` or, for sessions run as kor-claude,
`C:\Users\kor-claude\.claude\projects\...`; some calls are in `<session>\subagents\agent-*.jsonl`. Match on the
command's start and the timestamp.

**Right (MUST REFUSE)**, judged under the rule before the 12 Nov narrowing:
- 4970 (session 1119f13b, KOR.Drafter, 10 Nov 00:17 local): `python - <<'EOF'` editing StatedLadder.cs, writing a C#
  regex `@"\\s*..."` through a non-raw Python string with the escapes doubled by hand. This is rule 7's own case.
- The existing deny cases in `tools/claude_bash_guard_test.py` (search "rule 3b narrowed 12 Nov"): `\b` into a C# string
  through a Python string, `'C:\Users\...'` in a Python string, `'\03'`, an unquoted `<<EOF` body holding `\\Kor-fs01`,
  `print('\P')` in an unquoted `<<PY`.

**Wrong (MUST PASS)**, 21 refusals, with the worker's reason as logged in `Note`:
- 8264 a0ed29c9: quoted `<<'EOF'` python; backslashes only in `r"\bred\b"` (raw) and intended `"\x00"`/`"\x01"`
  git-format separators.
- 8591 acc84549: quoted `<<'EOF'` to `cat` (rawdump.py, dump.csproj), bytes verbatim; only intended `\n` in Python.
- 8595 cd2499ed: quoted python; escapes are deliberate JSON test data (`\n \" \\ \t \x01 \u2028`).
- 8609 c2ad5988: Python part only `\n`; Windows paths were in a quoted `<<'EOF'` to `cat`.
- 8615 3920cc62: quoted `<<'EOF'` to `cat` lesson.md, Windows paths in prose.
- 8686 ac80da06: quoted to `cat` state2.md, `C:\Program Files` in prose.
- 8687 3920cc62: quoted to `cat` a commit message, one backslash in prose.
- 8688 51d2fd14: quoted to `cat` a C# file, verbatim `@""` regexes written byte-exact (what rule 7 wants).
- 8689 24484c67: quoted to `cat` msg1.txt, `bridge\batch` in prose.
- 8708 51d2fd14: quoted to `cat` state.md, Windows path in prose.
- 8801 5c5a10b5: quoted `cat >>` LIVE-PLAY.txt, UNC/Windows paths in prose.
- 8891 5750502b: quoted to `cat` pdcrun.csproj / Program.cs, ProjectReference paths.
- 8909 d0258715: quoted python; every Windows path in `r"..."`, the regex in `rf"..."`.
- 8911 d0258715: quoted `cat >>` a PROOF txt, `\\<computer>\C$` in prose.
- 8920 780c3992: quoted to `cat` state.txt, Windows path in prose.
- 8921 2d0ec830: quoted python; regex in `r"..."` and an intended `'\u2028'`.
- 8922 780c3992: `^\d{3}-` in a quoted `cat >>` .md; the python heredoc had no odd escape.
- 8924 c779e271: quoted python editing C#; only `\"` inside Python strings.
- 8929 780c3992: quoted `git commit -F -` message, one `\,` in prose.
- 8961 e7287342: quoted `git commit -F -` message, `results\mesh\` in prose.
- 8978 ee8754e5: quoted to `cat` state-cleanup.md, UNC path in prose.

**Claims about the cause** (the worker's, 21 Nov; check them): (a) the matcher reads every heredoc body as Python, but
a QUOTED delimiter hands bash's bytes through unchanged, so a body that goes to `cat`, `tee` or `git commit -F -` is
written verbatim and rule 7 has nothing to protect; (b) inside Python, `\x..`, `\u....`, `\U........`, `\N{...}`, `\a`,
`\f`, `\v`, `\0` and octal are legal escapes that the author may intend, so "an escape Python will change" is not by
itself the fault - the fault is an escape that turns a path or a regex into something else; (c) the worker's proposed
fix: fire only on odd escapes inside non-raw Python string literals, or on an unquoted delimiter, never on a quoted
`<<'EOF'` into `cat`.

One question the author has NOT settled, which your cases must decide: a quoted heredoc into `cat > x.py` (or `.cs`,
`.ps1`) writes the bytes verbatim, but the file is later RUN or COMPILED, so a `\b` inside a Python string in that file
still lands as a backspace. Say what the rule should do there, and why.

The author is holding back further real refusals, right and wrong, as a hidden check.

## Tasks (the method used for kor whole and kor repeat, docs/codex/CODEX-KOR-*-NARROW*.md)

Work in `C:\VIsual Studio Projects\Operations-bash-heredoc-narrow` on branch `bash-heredoc-narrow`, at develop 1038597c.

**A. Cases first, committed alone** (`bash heredoc narrow A: cases (codex)`), in `tools/claude_bash_guard_test.py`:
- MUST REFUSE: 4970 as its real command, every existing rule-3b deny case unchanged, and general forms you construct:
  a C# regex with `\b`/`\d`/`\s` through a non-raw Python string; a Windows path in a non-raw Python string (`\U`, `\6`,
  `\v`, `\0`); a UNC or `\\` in an unquoted heredoc to any consumer.
- MUST PASS: each of the 21 wrong refusals above as its real command (recovered from its transcript; a command may be
  shortened only where the cut cannot change the verdict, and say where), and the general forms: a quoted heredoc to
  `cat`, `tee`, `git commit -F -`; Windows paths and regexes inside raw literals; intended `\n \r \t \x.. \u....` in
  Python.
- Keep every existing test in that file unchanged and passing. Do NOT amend commit A after you start B. If a case needs
  changing, make a new commit and say why.

**B. Narrow** `HeredocEscape` (and rule 3b's comment block: CHECKS / ALLOWS / DOES NOT CATCH, updated to match)
(`bash heredoc narrow B: <one line> (codex)`). Do not change a MUST REFUSE to pass. Do not add a new tool, guard or rule.
If two cases cannot both hold, stop and explain why.

**C. Proof.**
- `python -I tools/claude_bash_guard_test.py` (the author ran it at 1038597c: "149 of 149 as expected", exit 0) before B (A's new MUST PASS cases fail, every old case passes) and after
  (all pass). Report the counts.
- Replay: every one of the 119 logged refusals whose full command you can recover, through the guard before and after
  (feed the PreToolUse payload on stdin exactly as the tests' `verdict()` does). Report X of 119 recovered, and for each
  one that changes from deny to allow, one line on why rule 7 has nothing to protect there. Every recovered command
  that still denies, one line on what it would change.

## What you may and must not touch

- READ-ONLY database access is allowed for this task, and only SELECTs on oversight.GuardRefusal to list the refusals.
  Use the SQL login in `tools/SqlQuery/oq.py` (`python -I tools/SqlQuery/oq.py -q "SELECT ..."`).
- The guard LOGS a refusal to oversight.GuardRefusal when it denies (`deny()` calls Framework `tools/guard_log.py`
  `record`). The author read guard_log.py's header: a payload WITHOUT `hook_event_name` is not treated as a live hook
  and writes nothing, which is how the tests stay out of the log. Check that in the code (read-only, it is outside this
  worktree), build every replay payload without `hook_event_name`, and say how you made sure nothing was written. If
  you cannot, stop and say so.
- Must not: push, install, or edit outside this worktree; change any other rule in the guard, any other guard, settings
  file or git hook; write the database; run anything on another machine; open C:\Users\ilalonde\Documents\KOR-holdout.
- If the post-commit hook writes to the database, skip hooks for your commit command as in the kor repeat round
  (`git -c core.hooksPath=<an empty folder> commit ...`), and say so.
- Keep TEMP and TMP at their normal values (NOT inside this worktree): in the kor repeat round a TEMP inside the
  worktree let a test write real rows to the database.

## Write ONE response file

`docs/codex/CODEX-BASH-HEREDOC-NARROW-RESPONSE.md`, committed with B. It holds:
- your verdict on causes (a)-(c) and your answer to the `cat > x.py` question;
- the cases (X refuse / Y pass, each with its source);
- what changed and why;
- the proof numbers (tests before/after, replay X of 119, every changed verdict);
- repo rule 11 (what it covers, what it does not, and one same-class fault it still misses);
- anything wrong or missing in this brief.
