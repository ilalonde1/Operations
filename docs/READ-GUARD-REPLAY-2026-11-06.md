# A lead holds findings, not raw material: the replay behind the sizes (board row 1936, 6 Nov 2026)

The rule and what it misses are in the header of `Kor.Operations.Framework/Context/ReadGuard.cs`. This file holds the measure.

**What was read.** `kor context --read-replay <list>`, run 6 Nov 16:18, over every `*.jsonl` under `~/.claude/projects` modified
since 2 Nov: 1,100 transcripts, 991 main and 109 helper. By script only, no transcript read whole. A 31218-01 session was running
and was read only.

**What the replay stands on.** A Read's size is the result's own (characters, or an image's base64 x 3/4), because a file on disk
may have changed since; the live rule reads bytes on disk. So 979 below is a floor: live, a picture Claude Code had shrunk is judged at its size on
disk, a read that failed is judged too, and a read with no range or over 300 lines is judged on the whole file, where the replay saw only the part returned. Tokens
of an image are the next request's context less this one's, shared across the calls of the batch. Tokens of a text file are characters
/ 3. "Re-sent" counts raw tokens over the requests left before the next compaction or the transcript's end, the same weight for
main and helper. It does not price a cheaper model or the cache.

## Result

| what | count |
|---|---|
| main-session Reads with a result | 4,922 |
| refused | **979** (938 image, 36 file, 5 pdf) |
| sessions with a refusal | 34 of 991 |
| in the two longest sessions (eb9075b2 9,636 requests, 32f94af3 6,662) | 706 of 979 |
| command outputs warned, over 10,000 characters | 164 of 14,383, in 29 sessions |

## Sizes

| size | why |
|---|---|
| image 50,000 bytes | a crop. Tokens follow bytes weakly: under 50k bytes median 1,851, 100-200k 3,013, over 400k 4,187 (single-call Reads, n 410) |
| file 40,000 bytes, no range | about 13k tokens. At 20,000 (the 16:12 run): refusals rise from 974 to 1,092, sessions from 34 to 92, read-then-edit from 6 to 33 |
| range 300 lines | the 99th percentile of the ranges sessions already read (limit p50 40, p90 130, p99 300, max 480) |
| pdf 2 pages | a page is a picture. 13 of 13 PDF reads named pages; 5 named more than 2 and are refused |
| output 10,000 characters | about the 99th percentile; the largest seen was 29,851 |

**No low-context line.** The candidates were run at 100k, 200k and 300k of context. Every one gives up more than it spares: reads under
100k are 30 refusals, re-sent a median 133 more requests, 39M tokens in the main against 3M to 10M through helpers. Reads
at 850k and over (100) came out slightly negative on the first model, and G5 refuses those already, handover steps apart.

## Net tokens, honestly

The 979 refused reads cost **576M tokens re-sent** in the main sessions (of 1,183M for every main Read). A helper starts near 46k: first
request, median 46,389 of 108 helpers; the two Sonnet look helpers started near 33k. Through a helper, with a 300-token finding kept in the main:

| helper cost per picture | helper total | net |
|---|---|---|
| 28,071 (a99d0a35, 18 pictures, 8 requests) | 81M | **+495M** |
| 51,392 (a9ca9e64, 4 pictures, 5 requests) | 104M | **+472M** |
| 398,338 (all three helpers with 3 or more pictures, the third an Opus helper doing other work: 37 requests) | 444M | **+132M** |

Only two helpers in the data were sent purely to look, so that first pair is thin. The total is carried by two very long sessions.
At the worst rate a normal job loses: 006abbce (680 requests) 8.3M in the main against 14.7M; 6f41faf2 (108 requests) 10.6M against 11.8M. At
28,071 they gain: 8.3M against 1.7M, and 10.6M against 1.1M. By requests left, on a first rough model (46k for each of 3 requests per group of nearby
reads, the 16:12 run): under 15 left loses 4.4M on 75 reads, 15 to 40 gains 5M on 131, 40 and up gains the rest. A session cannot know what is left, so
the rule cannot use it.

Worth saying: a helper's cost is per helper, so a look at 18 pictures in one is the cheap case, and one picture per helper is the dear one.

## Refusals of legitimate work, by session and line

"Legitimate" here means one of three, read by script: the file was named in the user's last message (43), it was edited later in the session
(5), or under 15 requests were left, so a helper saves little or costs more (69, "few left" below). 117 of 979, 110 distinct (session, file). The first 20:

| session:line | kind | file | size | context, requests left | why it was legitimate | the route it takes now |
|---|---|---|---|---|---|---|
| 32f94af3:27 | file | project_networkops_build.md | 46,639 | 59k, 482 | edited later | a range; an Edit accepts it |
| 32f94af3:40167 | file | ApiHost.cs | 40,427 | 78k, 183 | edited later | a range |
| af88ebfa:63 | file | StartRender.cs | 44,729 | 92k, 108 | edited later | a range |
| eb9075b2:54698 | file | project_virtual_drafter_what_it_is_and_how_to_ru... | 50,976 | 206k, 303 | edited later | a range |
| 2ae2e5fc:26 | file | part1.txt | 41,408 | 28k, 9 | named by the user | a range |
| 1b6f5557:80 | file | framework_selftest.py | 47,827 | 95k, 20 | named by the user | a range |
| f4c914ab:31 | file | RULES-TO-CHECKS.md | 44,485 | 37k, 1 | named by the user | a range |
| 81339a96:846 | file | BridgeExec.cs | 261,221 | 63k, 3 | named by the user | a range |
| 4e0396fd:38 | file | StallGuard.cs | 57,777 | 54k, 3 | named by the user | a range |
| 624c809b:35 | file | TurnCapture.cs | 47,834 | 60k, 5 | named by the user | a range |
| 32f94af3:25302 | pdf | KOR-Rack-Recabling-Runbook-2026-10-02-web.pdf | over 2 pages | 933k, 9 | named by the user | 2 pages at a time, or a helper |
| 32f94af3:25312 | pdf | KOR-Rack-Recabling-PortMap-2026-10-03-web.pdf | over 2 pages | 943k, 8 | named by the user | 2 pages at a time, or a helper |
| eb9075b2:104883 | image | dimsample2.png | 299,226 | 385k, 605 | named by the user | **a helper: see below** |
| eb9075b2:111430 | image | L3-vs-L4-framing.png | 55,920 | 108k, 596 | named by the user | **a helper** |
| eb9075b2:106281 | image | S2_08-A5-moved-north-1500.png | 102,267 | 530k, 472 | named by the user | **a helper** |
| eb9075b2:112977 | image | doors-page2.png | 271,029 | 354k, 454 | named by the user | **a helper** |
| eb9075b2:113066 | image | look-sw.png | 436,026 | 367k, 447 | named by the user | **a helper** |
| 7b3e2b20:598 | file | full.txt | 46,207 | 160k, 13 | few left | a range |
| 1593c1cf:1229 | file | Schedule E - Peace Regional Pool Facility Condition Assessment Report.txt | 157,889 | 416k, 2 | few left | a range |
| c44010aa:80 | file | StallGuard.cs | 57,269 | 69k, 4 | few left | a range |

Counts: image 94 (61 few left, 33 named; 30 distinct named pictures), file and pdf 23.

**Justified, with one left to decide.**
- A range reaches the same bytes, so a refused text file costs one retry. An Edit accepts a partial Read (this session did it: Program.cs lines 129-131, then an Edit).
- Few left: the rule cannot know that. It is small: the 78 refused reads with under 15 left cost 3.0M in the main and 2.4M through helpers at 28,071, more at any dearer rate.
- **A picture named in the user's last message (30 distinct, 33 reads; the five listed are all from eb9075b2).** The brief gave the rule no lift and this
  session did not draw a finer line than it was given (Operations CLAUDE.md rule 14). So it is refused, and a helper describes the picture. If the main session
  seeing with its own eyes what Ian pointed at matters more than the tokens, a pass for a path in his latest message is about ten lines. Not built: **OverLORD's call.**
- A fresh review (`kor_review.py`: Read, Grep, Glob only, no Agent, --max-turns 12) is judged as a main session. Two of the lines above were checked and are reviewers
  (4e0396fd, c44010aa: 4 and 7 requests, the word in the transcript); how many in all was not counted. Its route is a range only, and each is a turn of its 12. No review has run under this rule yet.

## The look guard

`tools/claude_look_guard.py` is the only guard found that demands a Read of a picture: a search of `tools/claude_*.py` and Framework
`tools/claude_*.py` for `\.png` and `"Read"` found no other (the prior-art guard records Reads and demands none of a picture). It now
credits a helper's Read once the helper has finished, at the place the helper was launched. On the real job transcript c60a5e28: 19
Reads in the main, 41 the guard now counts, the 22 more being its two helpers' reads of pictures. No deadlock. Header and tests are beside it.

## What OverLORD reads off the next job, for row 1936's "net tokens on one real job"

The baseline is 31218-01's session c60a5e28 as it stood at 16:18 on 6 Nov, before this rule: 61 requests, first context 89,607, last 461,691,
20.4M re-sent in the main; helpers a99d0a35 (505,286) and a9ca9e64 (205,571); 9 of its 19 Reads would have been refused, about 45k tokens.
On the next job of like size, after the install:

1. `kor context --read-replay <the main transcript>`, and the same for each `<session>/subagents/agent-*.jsonl`: the `session` row's `requests`, `last` and
   `sends`, and each `helper` row's `sends`. **Net: the main's sends plus its helpers' sends, per request, against the baseline's.**
2. `SELECT [Rule], COUNT(*) FROM oversight.GuardRefusal WHERE Guard = 'kor-context-read' AND [Session] = '<id>' GROUP BY [Rule]`: refused, by kind.
3. IQ, from that job's gates and looks: every gate as it would have been; did `claude_look_guard` refuse a claim the helper's look should have met (Guard `look`); did
   anyone catch a fault in a picture that a helper had read and not mentioned. That last one is the cost that tokens do not show.
