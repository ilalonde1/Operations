# Step 3 guards: independent review and bounded fixes

Reviewed 21 Nov 2026 in `Operations-step3-guards-review`, branch `step3-guards-review`.
Starting HEAD was `356646fc` (the brief), whose parent was the requested `8d654575`.
The brief is evidence to check, not an authority for the judgments below.

All four items have a **FAULT**. The previous Bash narrowings substantially reduced false
refusals, but their tests left executable shell contexts and fictitious status protection
unchecked. Keep the prior-art gate, but not the claim that its unchanged implementation is
sound. Keep absence log-only; skipping its rebuild respects Ian's ruling, whereas treating
its rows as measured mistakes does not. I made the bounded fixes below and explicitly left
the remaining faults listed under each item.

| Item | My judgment of the brief's population | Final own tests | Disposition |
|---|---|---|---|
| Heredoc escapes | **1 right / 118 wrong / 0 not settled of 119** | Bash suite **264/264** | Fix executable contexts, wrappers and C# newline escapes; retain mandated conservative refusals |
| Piped builds | **32 right / 82 wrong / 2 not settled of 116** | Same Bash suite | Fix fictitious protection, wrong status element, background boundary and help |
| Prior art | **21 right / 8 wrong / 0 not settled of 29** | **55/55** | Keep gate; stop counting enumeration/help as content search; document remaining heuristic misfires |
| Absence log | **1 right / 29 wrong / 0 not settled of 30** | **30/30** | Narrow two demonstrated phrase classes; default remains silent and log-only |

These are judgments of the **original logged actions**, not of today's matcher output.
RIGHT means the named substantive fault was present. Merely matching the guard's words or
its filename heuristic is not enough. WRONG does not certify the rest of the command or
the underlying work. Duplicate rows remain in the denominators.

## Evidence, method, and containment

Before the scratch analysis I searched `tools/`, `tools/ClaudeGuardAudit`, `docs/codex/`
and `../KOR.Drafter/db/tools/` for prior guard review, replay, and transcript-recovery work.
I used the guards' existing test protocols and read the heredoc narrowing brief/response,
Framework inventory 2 response, weaknesses response, gap-analysis responses and step-5
plan-audit response. Their prior decisions explain the gates; they do not determine my
new judgments. No permanent tool, new guard or new rule was added.

The SELECT-only snapshot contains 298 rows: 238 Bash rows (including the separate controls
and later probes below), 30 prior-art rows, and 30 absence-log rows. Access was exclusively
through `python -I tools/SqlQuery/oq.py -q ...`, against
`KorStandards.oversight.GuardRefusal`. No other table was queried. I selected Id, Guard,
[Rule], [Session], Repo, Matched, RefusedAtUtc, Judgement and Note. JSON was returned in
200-character chunks because the query tool's default column width would truncate a
single large JSON column. Database judgments and notes were compared with the recovered
evidence, not adopted as labels.

The Bash population predicate was `[Rule] LIKE 'A backslash escape inside a heredoc%'`
or `[Rule] LIKE 'A dotnet build/test/publish piped%'`, plus control 4970. The heredoc proof
window was `2026-11-12T15:45:00-07:00 <= RefusedAtUtc < 2026-11-21T11:48:00-07:00`.
The explicit ID lists below freeze the populations independently of later live rows.

I recovered full commands/Write requests/final messages from the two named local Claude
transcript roots, including subagents, matching session, text or normalized target path,
and timestamp. The local evidence records transcript path, line, timestamp, tool-use ID,
full text and SHA-256. Recovery succeeded for **236/238 Bash, 30/30 prior-art and 30/30
absence rows**. Row 7616 needed correction of the stored CP1252/UTF-8 mojibake: decoding
the stored prefix restores an exact prefix of the transcript command. Long-delayed Stop
rows were matched to the retained message; they are not separate independently authored
claims. I read the relevant prior searches/reads and subsequent tool results for prior-art
judgments, and the complete final messages and relevant preceding evidence for absence.

Replays parse recovered strings; **they never execute recovered commands**. Bash replays
used empty process/absent-slot fixtures. Every replay payload omitted `hook_event_name`.
For absence, I copied only the recovered assistant message into an ignored local JSONL
and passed that copy to Stop, never a live transcript. All 30 default Stop invocations
returned exit 0 with empty stdout and stderr.

I read Framework `tools/guard_log.py`: `record()` returns False for a payload without
`hook_event_name` before importing the connection helper, constructing SQL or starting a
process. A separate local probe with import/SQL/process/error-write sentinels confirmed
that early return for three event-free payload shapes. The existing test payload builders
also omit the event. A final read-only SELECT found **0** rows after snapshot ID 9588 for
`guard-test-%`, `t-%` or `step3-review-offline` sessions. This is an additional check; the
early-return proof, rather than a global row-count assumption, establishes isolation.

TEMP and TMP stayed `C:\Users\ilalonde\AppData\Local\Temp`. The existing tests used their
normal temporary fixtures and synthetic prior-art session state. Final frozen runners
also passed `-B` to child Python processes. All commits used
`git -c core.hooksPath=obj/step3-guards-review/empty-hooks commit ...`; that directory is
empty. No shared hook was changed. No push, install, remote execution, holdout access,
database write, settings edit or other guard change was performed.

Local detailed evidence is under ignored `obj/step3-guards-review/`: `recovered.json`,
`historical-replays.json`, `hook-replay.json`, `absence-replay.json`, the inspection files,
and frozen red/green test output. This task-local scratch material is not a shipped tool
or an additional response file. The committed cases are synthetic; I did not add more
real transcript bodies to the test fixtures.

## 1. Heredoc escapes — FAULT

### Independent proof

The historical test trees reproduce **222/222 at 9d843e32** and **237/237 at 8d654575**.
I executed their frozen test/source versions while preserving the original paths used by
their fixtures. Replaying all 119 recovered commands against the historical rule gives:

| Rule source | Deny | Allow |
|---|---:|---:|
| `1038597c`, before the narrowing | 119 | 0 |
| `9d843e32`, original heredoc B | 5 | 114 |
| `8d654575`, review baseline | 5 | 114 |
| This review | 5 | 114 |

Thus the numerical replay claim is true. It does **not** mean the five retained refusals
are all right. The original commands show:

| Judgment | IDs | Evidence |
|---|---|---|
| RIGHT (1) | **7514** | Unquoted heredoc carrying a literal UNC prefix with two backslashes; Bash removes one, damaging the path |
| WRONG, still conservatively denied (4) | **7578, 7830, 7832, 8149** | Deliberately doubled unquoted transport: 7578 doubles Windows path separators; 7830/7832 use four leading slashes to deliver two and escape dollar signs; 8149 supplies the escaping required by the resulting JSON |
| WRONG, now allowed (114) | IDs below | Quoted transport to cat/tee/git, raw Python strings, deliberate formatting/character escapes, or another non-executing consumer; no unintended escape conversion of the named kind |

The 114 WRONG/allowed IDs are:

`6868, 6914, 6959, 6985, 6990, 7005, 7018, 7069, 7077, 7085, 7091, 7093, 7159, 7173,
7182, 7186, 7198, 7204, 7238, 7242, 7244, 7255, 7261, 7268, 7296, 7300, 7304, 7319,
7338, 7358, 7360, 7393, 7400, 7446, 7468, 7479, 7484, 7507, 7529, 7533, 7545, 7570,
7581, 7584, 7586, 7616, 7620, 7622, 7632, 7636, 7642, 7647, 7656, 7659, 7665, 7669,
7671, 7699, 7717, 7718, 7721, 7722, 7723, 7729, 7739, 7742, 7775, 7776, 7778, 7793,
7807, 7821, 7827, 7835, 7860, 7866, 7870, 7879, 7884, 7900, 7906, 7911, 7918, 7934,
7965, 7980, 8145, 8163, 8167, 8176, 8264, 8591, 8595, 8609, 8615, 8686, 8687, 8688,
8689, 8708, 8801, 8891, 8909, 8911, 8920, 8921, 8922, 8924, 8929, 8953, 8961, 8978,
9059, 9218`.

Separately: **4970 RIGHT**, 1/1, changes allow->deny at 9d843e32 and stays denied. It is
a C# verbatim regex transported through a non-raw Python string, which rule 7 expressly
forbids, even though its hand-doubling need not corrupt that particular output.
**9584 RIGHT**, 1/1 later probe, deliberately supplies the unsafe Python regex shape and
remains denied. Neither is quietly added to the 119 denominator.

### Faults fixed and red proof

`1524fc3d` is the cases-only A commit. Its frozen code/tests give **237/248**: all 11
added cases fail against the then-current implementation. `36064251` is B; immediately
after it the suite was **248/248**. Later pipe cases bring the shared suite to 264/264.

The failures were executable Python inside double-quoted and nested command substitutions;
arithmetic left-shifts swallowing a later real heredoc; `uv run`, `sudo -u`, `nice -n`;
direct cat/tee pipes supplying Python source; and C# verbatim `@"\n"`/`@"\r"` transported
through non-raw Python strings. The bounded reader now retains the consumer across those
shell contexts and consumes queued heredoc bodies once. It distinguishes arithmetic from
redirection and includes the C# control escapes in the regex check.

The brief's whitespace-delimiter concern is not a valid Bash closing-delimiter example:
ordinary `<<EOF` requires an exact delimiter line; `<<-EOF` permits leading **tabs**, not
arbitrary surrounding spaces. Restoring arbitrary whitespace acceptance would invent a
shell boundary. Nor is generic `xargs python -` equivalent to passing the heredoc as Python
source: xargs consumes its input as arguments and normally gives the child `/dev/null` as
stdin. Special arrangements need their own evidence; no blanket xargs rejection was added.

### Rule 11 and remaining faults

Covers the named shell contexts, direct Python stdin consumers, supported wrappers,
active Python escapes and the C# verbatim-regex marker. Allows quoted data transport,
raw strings and intentional formatting escapes. It does not evaluate generated files,
arbitrary transformations, aliases, backticks or substitutions hidden inside arithmetic.
The comments beside rule 3b state these boundaries.

Same-class miss: a relative path `'work\new'` still passes and Python can change its
backslash-n into a newline. **Left now:** without path-use evidence it is indistinguishable
from an intentionally formatted string, and blanket rejection would restore the measured
formatting misfires. C# ordinary-string regexes hand-doubled without the `@"` marker and
unrecognized wrapper/pipeline forms likewise remain outside this bounded check.

The four retained false refusals are **left now** because the brief expressly requires
every existing MUST REFUSE case unchanged, and inferring intended escaping would need a
separate agreed test-contract change. Existing real-text fixtures are unchanged for the
same preservation requirement; they remain a privacy/maintenance note, not proof of a
false allow. All added cases use synthetic text.

## 2. Piped builds — FAULT

### Independent proof and judgments

Recovered **114/116**, including **4290**, which the earlier replay omitted. That row is
a PowerShell request: build output goes to a file, `$LASTEXITCODE` is saved, and a later
Select-String pipeline reads the log. It is a WRONG refusal, not an unsafe build pipe.
Rows **8447 and 8543** have probe session names and no matching local transcript. Their
short stored commands suggest the rule, but I count both **NOT SETTLED**, not independently
verified right.

| Rule source | Recovered commands | Deny | Allow |
|---|---:|---:|---:|
| `9d843e32`, before the coordinator's pipe change | 114 | 48 | 66 |
| `8d654575` | 114 | 32 | 82 |
| This review | 114 | 32 | 82 |

On the brief's **113** recovered subset (exclude 4290), its claims reproduce: **14/14**
database-judged-right remain denied; **10/10** judged-wrong since 12 Nov become allowed;
**6** unjudged become allowed; **18** unjudged remain denied. The six are **416, 2365,
2808, 4575, 7297, 8202**, with immediate compiler-status reads. The ten are **8275, 8594,
8597, 8805, 8842, 8894, 8904, 8905, 8912, 8931**: 8275 has an unrelated later pipe;
the other nine read the compiler status immediately. Row 2807 also reads it but was
already allowed by the earlier command-position matcher; it is not a seventh transition.

**RIGHT (32)** — real dotnet output pipelines with no active protection or immediate
compiler-status capture:

`437, 449, 463, 2120, 4753, 5680, 6238, 6773, 6872, 7132, 7192, 7245, 7254, 7257,
7302, 7306, 7315, 7332, 7409, 8592, 8603, 8614, 8691, 8740, 8890, 8898, 8910, 8919,
8925, 8928, 8992, 9009`.

**WRONG (82)** — quoted filter alternations or mentions of dotnet, unpiped output redirected
to a file, unrelated later pipes, or immediate status capture:

`382, 416, 1067, 1923, 2365, 2691, 2739, 2807, 2808, 3565, 4014, 4290, 4575, 4724,
4725, 4728, 4733, 4743, 4752, 4758, 4759, 4782, 4811, 4837, 4900, 4902, 4931, 4978,
4980, 5015, 5062, 5070, 5119, 5149, 5155, 5157, 5158, 5160, 5216, 5274, 5358, 5361,
5496, 5525, 5533, 5534, 5606, 5627, 5665, 5672, 5726, 5730, 5753, 5755, 5764, 5856,
6103, 6125, 6251, 6295, 6309, 6310, 6328, 6351, 6449, 6598, 6600, 6833, 6842, 6856,
7297, 8202, 8275, 8594, 8597, 8805, 8842, 8894, 8904, 8905, 8912, 8931`.

**NOT SETTLED (2): 8447, 8543.** Separately, later probe **9586 WRONG**, 1/1:
`dotnet build --help | ...` requests help, not a compilation. The added help case fixes it.

### Faults fixed and red proof

`99391731` is cases-only A. Frozen tests/source give **248/263**, with all 15 additions
failing. Thirteen unsafe shapes had been allowed: single-quoted/escaped/comment-only
PIPESTATUS, the wrong array element, a build at a later pipeline position, a stale nested
substitution read, a mere mention of pipefail, disabling it, enabling it too late, enabling
it only in an earlier subshell, disabling it after enabling it, a comment, and a build
inside a quoted command substitution. Two safe shapes were refused: a background build
followed by an unrelated pipe, and help.

The first candidate implementation exposed a regression on replay **8912**: a preceding
unrelated pipeline contaminated the index of the compiler in the later pipeline. I added
the synthetic `printf x | head; dotnet build X | tail; echo ${PIPESTATUS[0]}` in a separate
cases-only **A2, `6bf9a0c4`**, after observing it wrongly denied by that candidate. A was
not amended. The saved candidate replay records the 8912 denial; the final replay restores
allow. A2 concerns that intermediate candidate, not an alleged failure of the original
8d654575 code on this allow case.

**B, `f230bc84`**, separates shell syntax from variable expansions, tracks literal
pipefail settings and parenthesis scope, requires the compiler's actual array element,
and respects command/background boundaries. All **264/264** now pass. The replay
population stays 32 denied / 82 allowed; the improvements address the new counterexamples,
not imaginary extra transitions in the historical data.

The group explanation in the earlier review was wrong. A local harmless Bash probe using
`false | cat` showed PIPESTATUS[0] = 1 after a brace group, but 0 after a subshell. An
immediate substitution can inherit the status; running `true` inside it first destroys it.
The original brace test was already denied because its first semicolon makes the next
segment `}`, not because the post-pipeline group check found a brace. I preserved that
MUST REFUSE expectation and corrected the comment. The old background-ampersand comment
also contradicted END; both the implementation and current explanation now say `&` ends
the build's pipeline.

### Rule 11 and remaining faults

Covers explicit dotnet build/test/publish pipelines, supported command wrappers, literal
prior pipefail state, and immediate unescaped compiler-element/all-array reads. Allows
unrelated pipelines, valid captures and help. It does not prove that a read status was
acted upon, interpret arbitrary shell code, or recognize every executable alias/wrapper.
Nested-substitution status reads are conservatively not credited; the brace refusal stays.

Same-class false allow: **`false && set -o pipefail; dotnet build X | tail`** can still
appear protected although the set command never ran. **Left now:** proving conditional
execution and dynamic shell state needs a control-flow contract beyond this bounded
lexical fix; accepting it remains a named limitation, not a soundness claim.

## 3. Prior art — FAULT; keep the gate, correct the implementation

The brief's 29-row population and **23 unjudged / 6 judged wrong** database split reproduce.
I recovered all 29 requests and examined their preceding searches/reads. My judgments are
**21 RIGHT / 8 WRONG / 0 NOT SETTLED**. The two rows 8622/8623 map to the same transcript
line **1777**, same tool-use ID, same path and content in session `acc84549...`: the
duplicate-logging claim is supported, but two rows do not make two wasted turns.

| Judgment | IDs | Evidence |
|---|---|---|
| RIGHT: missing family read | **4101, 4103, 4612, 4812, 6926, 8542** | No relevant existing family precedent had been opened before the Write. 8542 had authored its own guard, but had not read the pre-existing guard test; opening `guard_log_test.py` is a meaningful additional step. 4812's diagnostic wrongly calls numbered handover docs migrations, but a sibling handover still had not been read |
| RIGHT: missing qualifying substantive search | **5576, 6922, 6924, 7052, 7055, 7537, 7540, 7624, 7638, 7974, 7976, 8170, 8945, 8963, 8972** | Lists, specific-file reads, truncated searches or searches for a different task preceded these creations; their full prefixes did not establish the requested prior-art search. 5576 is an intentional live probe, not saved production work |
| WRONG: relevant work searched but filename/target-directory heuristic missed it | **4213, 4964, 4965, 8622, 8623** | See concrete evidence below |
| WRONG: wrong proxy for knowing the convention | **4996, 5010, 7843** | Relevant test conventions were read under different prefixes; or the whole sibling family had just been authored in this session |

The eight WRONG findings are not based on unchanged reissued content:

- **4213**, session `92443cad...`, lines 545/551: the pilot's parent directory was listed
  (only four entries, despite a head-30 cap), its design/errata had been read, then PLAN.md
  was created. `tasks.json` was refused because the basename word `tasks` and new child
  directory were absent from the recorded look. That is not evidence of doing no prior work.
- **4964/4965**, subagent in session `5f31475d...`, before lines 116/120: searches for
  EtabsOpenCheck, netops and earlier CSI run scripts, a Grep of `docs/one-tool`, and reads
  of the earlier API/build script precede `1-stage.ps1` and `3-fetch.ps1`. The demanded
  words `stage`/`fetch` are generic filenames, not better descriptions of the prior work.
- **8622/8623**: line 1688 searches callers of the actual ported module names across repos;
  lines 1770/1771 list the evidence parent and existing port conventions. The gate then
  demands the generic `callers-to-switch.txt` words or exact new subdirectory.
- **4996**, session `72b5b2be...`, lines 980–987: the session searches existing gate tests
  and reads `entrance_cloud_note_gates_test.py`, after work with the catalogue's plot tests.
  Refusal demands a root `plot_*` sibling rather than recognizing the relevant convention.
- **5010**, session `d2e38ff0...`, lines 973/974: the session lists gate tests and reads
  `keep_going_test.py` to learn the same Run harness. A `plan_*` prefix is not the test format.
- **7843**, session `bfd3cbae...`: it read `stop-r4-210.ps1` at line 1975, then authored
  the three `stop-b2-*` siblings at 2002, 2136/2144 and 2474. Refusing a fourth at 2529
  because no sibling has a recorded Read adds no missing knowledge of that family.

For **8945, 8963 and 8972**, I agree with the coordinator's substantive distinction:
finding nothing and then writing the same artifact is not a misfire. For **8542**, reading
the previous test convention was also legitimate. I disagree with treating this argument
as a disposal of all misfires; the eight cases above contain actual prior work the heuristic
failed to credit. “27 of 29 hit the target” is at most a rough category description, not
27 independently justified refusals.

Later **9588 RIGHT**, separately 1/1: the request to create this very brief preceded a
content search; the author subsequently performed it. No new brief population is inferred
from that single later row.

### Fix, proof, and rule 11

I found a separate reproducible false-allow class in the unchanged code: after ordinary
look/read requirements, **`rg --files`**, **`rg --files -g ...`**, **`rg --type-list`**,
**`rg --help`** and **`rg --version`** all satisfied the strict *content* gate. They do
not search file contents. This directly defeats the third check's stated purpose.

**A `26a4c256`** adds those five cases, each observed failing: frozen result **50/55**.
**B `e2d337bc`** identifies these non-content modes before recording a content search:
**55/55**. Existing `rg -l`/`--files-with-matches` remain content-search modes. The
look/read requirements and all pre-existing tests remain intact.

The header now states CHECKS / ALLOWS / DOES NOT CATCH and the observed residual misfires.
It checks recognized search requests, not successful searches or whether results were read.
Same-class miss: **`rg desired-topic missing-directory`** can be credited despite failing;
a wrong-scope search can still match a topic word. **Left now:** this PreToolUse gate has
no result evidence, and changing its lifecycle would exceed this narrow fix.

The eight false refusals are **left now**: broadening filename, parent-directory and family
equivalence without a validated replacement would weaken existing MUST REFUSE cases; the
transcripts justify documenting them, not inventing semantic equivalence from eight rows.
Keep the search/read obligation; do not claim these implementation limits are SOUND.

## 4. Absence-log — FAULT as a classifier; stays log-only

The database split is indeed **2 right / 26 wrong / 2 unjudged**. My independent result is
**1 RIGHT / 29 WRONG / 0 NOT SETTLED of 30**:

| Judgment | IDs | Evidence |
|---|---|---|
| RIGHT | **8934** | “there is no board row for it yet” about MuPDF warnings, without a corresponding board search establishing that absence |
| WRONG: supporting evidence already supplied or explicitly scoped capability/context | **8242, 8443, 8600, 8610, 8613, 8806, 8893, 8897, 8926, 8930, 8936, 8937, 8954, 9207, 9208, 9209, 9210, 9211, 9212, 9213, 9228, 9275** | Replies cite the concrete check/error/count/configuration or the known access boundary; the missing literal `Checked:` is not missing evidence |
| WRONG: metaphor/degree idiom | **8941, 8942, 8944** | Ian could not see overnight activity while asleep; “nowhere near” complete/clear is degree, not artifact absence |
| WRONG: unresolved investigation | **8988, 8998** | “Cause not found” reports an investigation still open, with experiments and next work described |
| WRONG: negation reversed | **8991** | “no tracked file is missing” asserts presence; the substring `is missing` reverses its meaning |
| WRONG: candid admission of unread context | **9003** | “What I don't have is the plan itself in your words” is immediately qualified by “I didn't read it this session”; it does not assert that the plan does not exist |

For the previously unjudged rows: **9228** cites the server gate and the recorded account
access limitation; **9275** describes the actual push refusal for commits lacking session
trailers. Both are WRONG absence-log candidates. For **9003**, the session's failure to
read the user's plan is a real work failure, but classifying its candid admission as an
unproven repository-absence claim conflates two different faults. I disagree with that
stored “right” judgment. No database judgments were changed.

The log is useful as a **candidate queue for manual inspection of phrases**, not as an
absence-error rate, actual refusal count, or count of wasted turns. It omits claims without
its phrases, accepts a `Checked:` marker without verifying its scope, and repeats messages.
The 1/30 finding is for this reviewed population, not a measured overall sensitivity or
precision. Its default path costs no extra response turn, although logging still performs
work; that is a reason to preserve log-only behavior, not a reason to trust its metrics.

### Narrowing, proof, and rule 11

**A `f5646b46`** adds four synthetic versions of 8942/8944 and 8988/8998; all misfire:
**26/30** against the frozen pre-change implementation. **B `abce987a`** excludes the
degree idiom `nowhere near` and the unresolved-investigation phrase `cause not found`,
per match. **30/30** pass with the suite's `KOR_GUARD_BLOCK=1` test override. The normal
default was separately replayed and stayed silent for **30/30** copied messages.

The candidate matcher changes **30 -> 26**, exactly IDs **8942, 8944, 8988, 8998**; all
other historical verdicts remain. This was a demonstrated word-list narrowing, not an
artifact-based rebuild. The stale header saying the default blocks was corrected.

Covers the existing phrase candidates except the two demonstrated idioms. Allows those
idioms without exempting independent absence claims elsewhere in a reply. Does not verify
artifacts, evidence scope or the truth of a check. Same-class new blind spot: a real stored
item named **cause** reported as “cause not found” is excused. **Left now:** preserving the
investigation idiom is worth this narrow ambiguity; a reliable distinction needs the
artifact-based rebuild Ian has not authorized here. The remaining supporting-evidence,
negation and metaphor misfires are left for that rebuild rather than expanding this into
another collection of brittle sentence rules.

“Skip” is therefore right **for the rebuild and for blocking**, but wrong **for evaluating
the log or fixing a directly demonstrated cheap narrowing**. It remains log-only.

## Claims in the brief that were wrong, incomplete, or easy to misread

1. The historical **222/222, 237/237, 119-replay and 113-replay numbers are reproducible**.
   They do not establish soundness on missing shell contexts or protection counterexamples.
2. The five retained heredoc denies are not five correct refusals: four are deliberately
   escaped transport. Control 4970 establishes a rule-7 policy violation, not by itself
   proof that its particular doubled regex was corrupted.
3. Arbitrarily space-padded delimiter lines are not valid Bash terminators. The xargs
   concern needs an actual source-routing example; naming an executable wrapper is not enough.
4. `@"\n"` was a real missed class and is fixed. `'work\new'` remains a named ambiguous
   false allow. Real-text fixtures remain because the brief also requires existing tests
   unchanged; the new cases avoid that practice.
5. The brace explanation was false: Bash preserves PIPESTATUS across a brace group. The
   existing case's refusal does not exercise the claimed brace-boundary reasoning. The
   background-& comment contradicted the code. Both are now explained accurately.
6. “PIPESTATUS in the next command” was insufficient: literal text, the wrong element,
   comments and overwritten nested status all previously qualified. Merely mentioning
   pipefail qualified too. The brief did not identify these false allows.
7. 4290 **is recoverable** and is a false refusal of a PowerShell log/capture command.
   8447/8543 remain unrecovered; their probe labels cannot substitute for a transcript.
8. “Keep prior-art” is reasonable policy; “unchanged is sound” is false. The 27/29 category
   assertion neither proves useful intervention nor accounts for helpers, handovers and
   the evidence-file/family heuristic misfires. The duplicate 8622/8623 assertion is true.
   Rewriting the same content after a search alone does not prove a misfire, but actual
   relevant searches/reads before the refusal do establish the eight cases above.
9. Absence's stored 2/26/2 judgments are not independent truth. Row 9003 names an unread
   context problem; rows 9228/9275 add two supported/non-absence candidates. “Costs no
   turns” does not make the telemetry an accurate measurement.
10. RefusedAtUtc values in this query include an explicit **-07:00 offset**. I used that
    offset when matching UTC transcript timestamps; applying an extra seven-hour shift to
    already-aware values would be wrong. The starting tree was the brief commit atop the
    requested develop revision, not exactly the develop commit itself.

## Commit sequence and final verification

| Commit | Purpose |
|---|---|
| `1524fc3d` | Heredoc A, 11 red cases alone |
| `36064251` | Heredoc B |
| `99391731` | Pipefail A, 15 red cases alone |
| `6bf9a0c4` | Pipefail A2, candidate regression exposed by replay 8912 |
| `f5646b46` | Absence A, four red cases alone |
| `abce987a` | Absence B |
| `f230bc84` | Pipefail B |
| `26a4c256` | Prior-art A, five red cases alone |
| `e2d337bc` | Prior-art B |
| `ae591ef7` | Comment follow-up: remove stale pipe wording and state retained prior-art misfires |
| This final commit | This response only |

Final frozen HEAD runs: **Bash 264/264, prior-art 55/55, absence 30/30**. Frozen A runs:
**237/248, 248/263, 26/30, 50/55**, respectively, with the intended additions failing.
All original test files have **additions only**, verified against 8d654575; no pre-existing
test line or MUST REFUSE expectation was changed. An AST comparison confirms the Bash
guard's executable source outside **HeredocEscape and PipedBuild** is unchanged. Full
final-hook replay of the 236 recovered Bash requests produced **39 denies / 197 allows**;
per-rule counts above remain the relevant proof, since the full hook also contains other
rules. `git diff --check` passes.

Only the three named guard files, their three test files and this response were changed
by this review. The brief was already present in the starting commit. No .NET project was
changed, built or tested; the full solution was not run. One Codex session, no subagents;
no monetary-cost measurement is available. The remaining faults are explicitly left with
reasons above, rather than described as fixed or hidden behind passing aggregate counts.
