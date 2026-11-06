# CODEX - how KOR stores and reaches its knowledge, so an LLM keeps full IQ without burning the week

Written 5 Nov 2026 by OverLORD, the Framework repo's overseer session, on Ian's order. Three reviewers answer this separately: two fresh
Claude sessions and Codex. You are one of them and have not seen the others. Reasoning: high. Design only: no code, no hooks, no
settings, no database writes, no builds.

## Ian's question, his words
"There MUST be a more efficient intelligent way to keep the BRAIN/MEMORY/CONTEXT/DETAIL RECALL/REASONING - up to date always - and
produce work based on a closed system (KOR). Something where we can use the virtual drafter et al without spending our usage in 2
days?" And: "is there a way to let worker agents do what they need for a task but conversational ones that repaeat and redouble and
compund the token usage is restricted?" And: "Is that the MOST INTELLIGENT, BEST, MOST EFFICIENT USE of what amounts to how to do we
store and access data in LLM."

His limit, 31 Oct: "I'm only ok cutting spend so far as it doesn't cut IQ."
On memory, 4-5 Nov: "It needs to know full context. It needs to know EVERYTHING." "SHOULD we not load the full memory before asking it
to do a task????"

## The order of your work
1. Read the facts below and whatever the sources show. Check any number you rely on; a number here is a claim until you have.
2. Write YOUR OWN answer first, to your answer file, before step 3.
3. Only then read the sealed first design, `C:\VIsual Studio Projects\Framework\docs\MEMORY-DESIGN-OVERLORD-FIRST-DRAFT.md`, and append
   a section attacking it: what is wrong, what it missed, what it got right. Do not change your step 2 answer after reading it; add to it.

A reviewer who confirms the sealed design has added nothing unless it tried to break it first and says how.

## What to write
Under 1,500 words. Ian reads the result as one page through OverLORD.
1. **The recommendation, plainly:** how knowledge is stored, how a session reaches it, what a session carries, and what happens as it
   grows. What is different for a conversation, a worker on a task, a helper and a reviewer.
2. **What IQ it risks,** and how we would measure that it did not fall. A test that could fail, before and after.
3. **What it saves,** as a number computed from data, with the method and what it ignores. No figure without a source; "not measured"
   is a good answer.
4. **What it costs to build,** and the smallest first piece that would prove or kill the idea.
5. **At least two real alternatives you rejected,** and why, including one you took seriously.
6. **What you could not check.**

Codex writes one file: `docs/codex/CODEX-KOR-MEMORY-AND-TOKEN-DESIGN-RESPONSE.md`. A Claude reviewer writes the answer file its own order names.

## Measured (OverLORD, 5 Nov 22:53 BCDT, this PC's Claude Code transcripts since Mon 2 Nov 12:00, when the allowance week began)
- Raw tokens 3,785M over 14,280 requests. Raw = input + cache creation + cache read + output, per request.
- New tokens (input + cache creation + output, so not cache read) 110M, 2.9%.
- By kind: raw share, sessions, mean context at a request.
  - Interactive conversation: 52.3%, 20 sessions, 421k. New 24.3M.
  - Helper subagent: 32.1%, 105 sessions, 216k. New 29.9M.
  - Headless worker (`claude -p`): 11.3%, 316 sessions, 260k. New 17.2M.
  - Commit reviewer (`claude -p`, one per commit): 4.3%, 545 sessions, 70k. New 38.8M.
- By context at the request: under 100k 6.4%; 100-200k 13.0%; 200-300k 15.1%; 300-500k 25.8%; over 500k 39.7%.
- By start folder: Framework 57.8%, KOR.Drafter 27.5%, Operations 12.9%, NetworkOps 1.1%. The largest Drafter worker, 164M.
- 4 sessions took 1,534M raw between them, with peaks of 977k, 971k, 917k and 769k.
- Ian's Usage screen, All models: 57% 3 Nov 21:55; 59% 3 Nov 22:35; 65% 4 Nov 00:13; 74% about 4 Nov 09:25; 77% 4 Nov 14:49. The week
  resets Mon 12:00. Raw tokens per point have run from 15M to 68M. **Nobody knows what the allowance actually counts.** If cache reads
  count far below new tokens, the priorities above change.

## How it works today
- **Claude Code hooks** on six repos run `kor` (C#). At session start, `kor start` prints pages into context: the module's state, resume
  block, board rows, and memory and record one-liners. Claude Code cuts each SessionStart hook at 10,000 characters, which is why there
  are many pages. A fresh session reads about 75k-100k tokens before any work.
- **The brain:** SQL Server `KorStandards` on KOR-APP01, schema `brain`: `Memory` (short records with a source), `LaneState` (resume
  blocks), `Event`. Board: `oversight.Commitment`. Reads: `C:/VIsual Studio Projects/Operations/tools/SqlQuery/oq.py -q "..."`, with
  `USE KorStandards`; `C:/KOR-Operations/kor/kor.exe memory --module Framework`. About 379 bodies for Framework and Office; 705 memory
  rows for Operations. Loading every body for Operations is about 1.3M tokens.
- **Capture:** hooks ask each turn what it found and record decisions and findings with their sources. A source going stale marks the
  record stale.
- **Context limit:** warn at 300k and every 100k after, refuse work at 850k and force a handover; auto-compaction fires about 867k on
  the 900k window, the model's cliff about 975k.
- **Just built, not installed:** `kor start --part pages` loads every memory one-liner at start, and a gate refuses work until
  `kor read --subject` has read the task's bodies. Cold tests score 6 of 6 in 6 repos. It adds 23k-80k tokens per turn, per repo. Lookup
  is plain word matching: a Peace River wastewater tender read one note on the word "wastewater", and that counted as read. Details:
  Framework `docs/START-FULL-MEMORY.md`.
- **The product:** the Virtual Drafter (`KOR.Drafter`) reads drawings and builds ETABS and Revit models and drafted sheets, mostly
  deterministic code with checks, plus model sessions directing it. How much of a run is model time is not measured.

## Agreed already (do not re-decide)
- **No API at runtime.** Nothing calls Anthropic's paid programming interface for this work: the platform exports inputs, Claude writes
  in its own session, a command imports them. Operations `CLAUDE.md` rule 14. Embedding or ranking that runs locally or in SQL is open.
- **Built in C# or SQL,** not new Python or PowerShell (records #1262, #127890). SQL Server on KOR-APP01, one database per app. No
  SQLite, LocalDB or a new engine without reading what exists first.
- **No bulk in SQL:** short records plus pointers (path + SHA-256). Transcripts, logs, models and PDFs stay on disk.
- **Nothing lost:** a split, move or trim of memory loses nothing ("no lobotomies"). Copy, prove X of Y.
- **Closed system.** KOR's work and client data stay in KOR. The Drafter's code is confidential to its repo.
- **One lead per repo,** helpers temporary, work on the board. No cap on parallel helpers. Ian is told only a deliverable change, his
  decision, or an answer.

## Read these, and only these unless a claim needs more
| file | why |
|---|---|
| Framework `docs/KNOWLEDGE-IDE.md` | what the brain is meant to be |
| Framework `docs/START-FULL-MEMORY.md` | what was just built, with its measured costs |
| Framework `docs/G5-CONTEXT-STOPS.md` | what the context limit does and has measured |
| Operations `docs/HOW-WE-BUILD.md` | the method Ian made edict |
| `KOR.Drafter/docs/THE-PRODUCT.md` | the product |
| Claude Code's public documentation | memory, subagents, skills, hooks, prompt caching, compaction: what the tool offers. Do not assume. |

## Rules
- Do not edit any tool, hook, setting, doc or database. Proposals only.
- Codex: you cannot read the transcripts cheaply. Rely on the measured facts above and say so. Ian runs you; change nothing but your response file.
