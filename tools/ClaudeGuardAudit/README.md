# ClaudeGuardAudit — the instruments that measure the hooks

The hooks in `tools/claude_*_guard.py` are committed. Until 2026-10-27 the instruments that
*measure* them were not: they lived in a session scratchpad, which means every number ever
reported about a guard's coverage was unreproducible the moment that session ended. This folder
is those instruments, with the output they actually produced.

Searched before creating this folder: a listing of `tools/`, a content search of
`C:\VIsual Studio Projects` for the instrument names (which found `tools/CorpusGateBisect/net_probe.ps1`
and the two committed guard files), and `tools/*Audit*`, `*Guard*`, `*Bisect*`, `SuiteDiff` for an
existing home. There was none for guard audits; `CorpusGateBisect` is the precedent for the shape.

## Why this exists

`claude_instrument_guard.py` passed **11 of 11** of its own fixtures and then refused **3 of the
PDF Ingestor's sends**, holding real work. The fixtures could not fail: the same hand wrote the
regex and the strings it had to match. Virtual Drafter caught it, not the suite.

The fix that mattered was not a better regex, it was running the guard **over real transcripts**.
Doing so found a fourth fault in minutes — `145 buttons: certain 50 | likely 27 | no match found 68`
read as a nil *because of three words*, in an output reporting 77 matches — and then found that the
detector had never matched the guard's own founding case at all, because that claim was written
"None of the ~3.9 GB…" and "nothing I ran removed…" rather than in the guard's vocabulary.
Widening it took detection from 372 to 669 nil claims: it had been blind to roughly 45%.

**A gate's coverage is the narrowest of its stages, not the one that was measured.**

## The instruments

| File | Question it answers |
|---|---|
| `dryrun_instrument_guard.py` | One transcript, with its own must-refuse and must-allow known answers printed **before** the count. Run this before wiring any guard. |
| `sweep_instrument_guard.py` | All transcripts. Exits non-zero and refuses to print totals if either known answer is wrong. |
| `why_never_fires.py` | *Why* each nil claim was allowed, broken down by the exit it took, cross-checked against `verdict()` — `disagreements` must read 0 or the breakdown is not trustworthy. |
| `per_question_weakness.py` | Whether the run that cleared a nil asked the **same question** as the nil, and which claims were never armed. |
| `confirm_three_causes.py` | The three reported causes of the wrong refusals, measured against the live regexes rather than taken on report. |
| `inspect_refusal.py` | One record's refusal text plus the instrument's own runs, for reading a single case. |
| `scan_destructive.py`, `scan_gitclean.py`, `scan_drafter_deletes.py` | The founding case: which **executed** commands could have removed untracked files. Mentioning a command in prose is not running it, so only `tool_use` inputs count. |
| `verify_rule9.py`, `guardtest.py` | `claude_bash_guard.py` against the exact command it wrongly denied on 2026-10-25, and the forms that must still be denied. `deny()` exits 0, so the exit code says nothing — read the decision. |

`*.out-2026-10-27*.txt` are the outputs those runs produced that day, kept because the population
drifts: the same 62 files gave 369, then 372, then 374, then 669 nil claims as the live transcript
grew. A count here is evidence of what was measured then, not a current figure.

## What this folder covers, and what it does not

Covered: whether a guard arms, which exit it takes, and whether it refuses — over real transcript
records rather than fixtures.

**Not covered**, and each one is a way a number from here can still be wrong:

- **Only written scripts.** All three faults that motivated the instrument guard were **inline** —
  a heredoc into python, inline PowerShell, and `Imperial: 0`, which is not a nil phrase. The
  never-armed bucket in `per_question_weakness.py` is that gap, and it is large.
- **Attribution is a guess.** A run is credited to the most recently edited script whose basename
  appears in the command. An indirect invocation (`& $p`) cannot be attributed at all and *disarms*
  the guard rather than blocking.
- **Per-script, not per-question.** One successful run of a general-purpose runner clears every
  later nil from it, however different the question.
- **`found_something` is a heuristic.** It reads any count of 1 or more as a finding unless the
  preceding words frame it as sizing the input, so `33 hashed, 0 mismatches` reads as a finding.
- **Reading your own transcript changes it.** These instruments scan the session that is running
  them, and a sentence like this one contains nil phrases.
