r"""Look guard: a claim that something is done needs a COMPLETE LOOK at what changed.

Ian, 2026-10-27: "I think the hook should be a COMPLETE LOOK." / "Yes - build the complete look hook".
Why: today the gates caught counts; only LOOKING caught Jim's inverted clouds, the closed core door and the turned
columns, and the faults Ian keeps catching are claims made from a command's silence or a summary line.

Two doors, one rule:
  Stop        - my final reply to Ian. Blocks ONCE (stop_hook_active then allows, so it can never loop), like
                claude_absence_guard.py.
  PreToolUse  - SendMessage to a peer session. Denied until the look exists or the claim is reworded.

The rule: if the text CLAIMS completion (CLAIM below), then for every kind of change in KorStandards
oversight.LookRule (migration 138) that happened anywhere in this session's transcript, a LOOK of that kind must
come after its last instance. A tool call is a change when its tool is in ChangeTools and ChangePattern matches its
command (Bash/PowerShell) or file path (Edit/Write); a look likewise with LookTools/LookPattern (Read: the file
path). A command that both changes and then looks (a write followed by its SELECT) counts as looked when the look
match comes after the change match in its text. The refusal names each unlooked kind, its last change, and the
rule's COMPLETE text - what a complete look is.

Rules come from KorStandards through KOR.Drafter tools/standards_db.py (env KOR_ENGINEERINGTOOLS_STANDARDSDB),
cached for CACHE_S at %LOCALAPPDATA%\KOR\look_rules_cache.json (a cache of the DB, never the source). DB down and
no cache = allow (fail open), because a gate that blocks every reply when the server reboots stops the work.

What it COVERS: that a look OF THE RIGHT KIND happened after each kind of change, before a completion claim, in
replies and peer messages. What it does NOT: whether the look covered everything (every page, every storey) - the
Complete text tells the session what everything is, the hook cannot count pages it does not know; whether the
look was at the RIGHT file; whether it was understood (10-27: I opened the final model's Level 1, saw P1's solid
west wall and nearly called it a fault); claims phrased without a CLAIM word; changes made by other sessions or
by tools that are not Bash/PowerShell/Edit/Write; a DB write by a script FILE that does NOT go through
KOR.Drafter tools/standards_db.py (raw sqlcmd, its own pyodbc) - nothing in its command or output says it writes.
Writes through standards_db ARE seen: in the command (db.run/record/withdraw, migration 140) and, from any script,
in the OUTPUT line standards_db.run() prints, "KORSTANDARDS WRITE <table> (<n> statements)" (KOR.Drafter 91007ba,
migrations 141-143, Standard Details' case; a rolled-back write prints it too - the line says the batch held writes). Same-class fault it would miss: a reply saying "the drawings are
right" after rendering page 1 of 26 - one Read of a PNG satisfies the drawing rule.

Framework row 1203 (3 Nov 2026; GuardRefusal 440, 441, 893, 898 judged wrong). Three changes:
- A LOOK is also read AS THE SHELL RUNS IT: printf/echo -e "\n" and PowerShell `n are line breaks. The lead read
  every row back with `printf "USE KorStandards;\nSELECT ... FROM oversight.Mistake ..." > rb.sql`, and \bSELECT\b
  saw no SELECT (no word boundary between the n and the S), so the look could never be satisfied. That, not a
  missing oversight.Surface row, was the cause: this guard never read Surface. A CHANGE is still matched on the raw
  text only (a Windows path is full of \t and \n pairs), so a write spelled "\nUPDATE x SET" inside a printf is
  still not seen - as before this fix.
- A db-write's look, as before, is any look (LookPattern) after its last write; now also OUTPUT inserted.*/deleted.*
  in the write itself (every row it touched, as its own result - refusal 440's write) when that command's output is
  not cut by | head, | tail (`tail -n +N`, which drops a fixed head such as sqlcmd's "Changed database context" and
  shows every line after it, is not a cut, nor is Select-Object -Skip N for the same reason), Select-Object -First/-Last
  or grep -m, AFTER the write in that command (review row 284: not anywhere in it). A SELECT look whose output
  is cut STILL counts, as before: refusing it was tried and, on a second real transcript (Operations f6ff24dd, 839
  moments with a claim word), took refusals from 555 to 575 - 41 of the 98 moments that gained one behind greps of
  migration files the ChangePattern misreads as writes ("grep ... 'INSERT INTO ...' | head"). The refusal names the
  TABLES the last write wrote (INSERT INTO / UPDATE..SET / DELETE FROM / MERGE INTO / ALTER TABLE <schema.table>,
  or the qualified name in a "KORSTANDARDS WRITE" line) and, for each one oversight.Surface names, that Surface's
  code and its consumers' check; with no Surface row, its rows read back ARE the whole look.
  Tried and NOT done: requiring the read-back to NAME the written table. The dry run over the lead's 3 Nov
  transcript (196 replies and sends with a claim word) took refusals from 28 to 46 that way, 17 of the 18 more from
  one `grep -E "...UPDATE brain.Memory SET RetiredAtUtc"` the ChangePattern misreads as a write, and 1 from an
  ALTER TABLE in a LocalDB rehearsal whose database was then dropped - a look that could never be done.
  What it does NOT see: a SELECT read-back cut by | head or | tail (the miss above, kept so a misread grep's own
  SELECT goes on counting); whether the look read the table that was WRITTEN (a SELECT of any table, or SELECT
  @@ROWCOUNT, still satisfies - the miss above); whether it covered every row written (a SELECT of one Id satisfies
  a write of ten); whether a Surface's check RAN (Check is prose and CheckSql was NULL in 8 of 8 rows on 3 Nov - the
  refusal names the check, it cannot require it); a write whose own result was cut by a tail and then read back
  by a later SELECT (allowed: the rows were read, the statement's own error may not have been).
- A claim word wrapped in quotes ("passes", 'done', `fixed`) is a MENTION, not a claim, and is skipped: 898 read the
  guard's own misfire, `misreading "passes" as a completion claim`. NOT decided, and still refused when a look is
  missing: a claim word used as another part of speech in an unquoted sentence - 893 was "in three passes" (a noun)
  - and a sentence ABOUT the guard that uses the word unquoted. Deciding either needs the sentence understood; a
  regex guess would also wave through real claims ("all three passes"), so the miss stays named here.

Framework row 1936 (6 Nov 2026): a main session may not hold a big picture (kor context, ReadGuard), so a HELPER'S LOOK COUNTS,
or the two rules deadlock. Ian's rule that every sheet is looked at stands: a helper's Read calls, read from its own transcript
(<session>/subagents/agent-<id>.jsonl), count as Reads at the place it was LAUNCHED, once its finish is in the main transcript
(a queued_command attachment holding <status>completed</status>, or a foreground result with status completed). So a helper
launched before the last render earns nothing, one still running earns nothing, and one that failed earns nothing. It credits
Read calls only: the other kinds' looks are Bash and PowerShell, which a main session may still run. A Workflow's agents are not
credited, and a helper resumed with SendMessage after a render earns nothing: launch a fresh one. Same-class fault it would miss:
a helper sent to do something else that reads one unrelated picture satisfies the drawing rule, and the helper's finding may
never mention what it saw, exactly as one Read of page 1 of 26 already does.

Framework rows 2010 and 1650 (10 Nov 2026). The class, in one sentence: a read-only command that LISTS a .py file before a
drawing script (git show/diff/add, grep, ls, for-loops) read as a drawing run, because the drawing ChangePattern opens with
(?:python|py) and no left boundary, so 'virtual_drafter.py tools/drafted_set.py' holds 'py tools/drafted_set.py'.
change_in() now skips a drawing match that starts inside a word or file name (previous character a letter, digit, '_'
or '.'); the other kinds match as before (review row 1419: on db-write the skip would have dropped `kor.db.run(`).
Replay of all 196 look refusals of 3-10 Nov on their own transcripts, cut at the refused text (187 of 196 reproduce the
stored kinds; 1 transcript not found): 85 change, 83 of 86 drawing refusals cleared - 10 of 10 judged wrong (2776-2780,
5095-5097, 5427-5430) now allowed - and the one real run left (5384, `python -u tools/virtual_drafter.py run`) still
refused, 1 of 1. The build kind (87 refusals, none judged) was NOT changed: in the 160 stored characters of the change
each blamed, 26 show a real dotnet build or build.ps1, 1 (2741) is a grep for 'dotnet publish' - a mention read as a
build, the same shape as this fix but not a word-start, so it is still refused - 1 names it in an echo, and 59 cannot be
read from the stored cut.
What it does NOT cover: `python -X utf8 tools/drafted_set.py` (an option that takes a value) is not seen as a run, before
or after this change; a CLAIM word in the future tense ("I'll ask once it's built") is still a claim - deciding tense needs
the sentence understood (row 1650 asked for that or this line). Same-class fault it would MISS: a drawing script run as
`./tools/drafted_set.py` or through `uv run` names no interpreter and is not seen.

Framework row 2032 (10 Nov 2026). The class, in one sentence: text that is QUOTED - mentioned, not run - was read as a
change that ran (and, in a reply, as a claim). A change is now matched only on mentions_blanked(command, kind), which
blanks, keeping every position: (1) a heredoc body unless it is run - piped to or read as code by an interpreter
(bash, pwsh, python with no script, ...), or written to a script file (.ps1 .sh .py .cmd .bat .js); a .sql file, sqlcmd
or oq.py run it as SQL, which counts for db-write only; so `git commit -F - <<EOF`, `cat > brief.txt <<EOF` and a SQL
literal that names `dotnet build` are text; (2) the quoted value of a message flag (-m --message --note --says --body
--because --description --evidence --what --why --reason --title --summary); (3) the quoted arguments of grep, egrep,
fgrep, rg, findstr, Select-String, and of echo, printf, Write-Host, Write-Output unless that output is piped to an
interpreter or redirected into a script file (or a .sql file, for db-write). A double-quoted string holding $( ) or
backticks is never blanked: the shell runs that. And a claim word inside a `code span` or a fenced ``` block is a quoted
output, not a claim (claim_in). Looks are matched on the raw text, as before.
Replay of all 202 look refusals in GuardRefusal (3-10 Nov window plus every judged one), on their own transcripts cut at
the refused text, HEAD vs this (201 of 202 replayed): refused 110 -> 99, no replay gained a kind; every one of the 13
that changed was read - each blamed change was a mention (guard_log --note, git commit -F - heredoc, a grep pattern, a
SQL or brief heredoc, printf into a lesson file) and 3092 keeps its real dotnet build. Judged wrong allowed 20 of 22
(445 and 2741 by this change, with 42-45 440 441 893 898 2776-2780 5095-5097 5427-5430 from before); judged right still
refused 1 of 1 (5384). Not freed, other classes: 41 - its code-span `DONE` is now skipped, but the same reply says "the
applied run kept everything" after a .sql Write that was never run here; 403 - "nothing built on it", a NEGATED claim
word. Claim words replayed on 199 of 202 refused texts: 0 changed, so the code-span rule frees no stored refusal but 41.
What it does NOT cover: a quoted argument of any other command (python -c "...", oq.py -q "...", a quoted SQL whose
literal names a build) is still matched; a heredoc fed to an unknown command is blanked unless the line pipes it to an
interpreter; prose that names a command unquoted still reads as one; looks inside a commit message still count as looks.
Same-class faults it would MISS (a run hidden as a mention): `cat > x.txt <<EOF` then `bash x.txt`, or `echo "dotnet
build" > b.txt; pwsh b.txt` - the file is run under a name that is not a script's; `xargs`/`ssh host <<EOF` running a
heredoc; an echo or grep that starts a command INSIDE another's quoted code after a ';' (python -c "x; echo 'dotnet
build'" blanks the quoted text after it). Review row 1436 (APPROVE, 4 MINOR, all fixed): a here-string <<< and a shift
1<<n are not heredocs, and a head holding (( is arithmetic; echo/grep count only at a command's start; an unbalanced ```
fence skips no claim. Errs toward refusing, stated: an interpreter name after any path separator (a path ending /sh or
\cmd) makes a heredoc count as run. Pre-existing, not this class: the drawing LookRule does not match a QUOTED script path
(`python "C:/x/tools/virtual_drafter.py" run`) - the quote between .py and run breaks it; a KorStandards fix.

Framework row 2091 (12 Nov 2026, FW-RESET-2; HOW-WE-BUILD 7: narrowed, not deleted). The class, in one sentence: a claim
word used in a frame that does not claim the work is done was read as a claim. claim_in now skips a claim word when
not_a_claim() finds, in the last three words of its own clause (back to . , ; : ! ? ( ) | * a line break or ' - '):
a NEGATOR (not, never, nothing, no, nor, none, neither, cannot, any n't) - "Release was NOT built", "Not built yet";
a MODAL or SUBORDINATOR (would, will, 'll, shall, could, might, must, once, until, till, if, unless, when, whenever,
'to be', 'as each', 'as soon') - "it would have to be built", "I'll ask once it's built", "sent as each passes";
done/DONE as a STATUS WORD (right after 'at' or 'only', or before 'or (a) DECISION') - "you hear from me next at DONE";
or when its SENTENCE (back to . ! ? or a line break; ':' does not end it) opens with an imperative verb (put, commit,
reply, report, send, tell, hand, write, ...) FOLLOWED BY a function word (what, to, only, in, the, ...) - "Commit what
works now", "Put in the state: the GT items done". Kept as claims on purpose: 'should' and 'can' ("It should be fixed
now" is the hedged claim this guard is for); a sentence-start noun ("Commit 3f2a landed", "Report written - done");
"X DONE:"; a negator in an earlier clause or four words back.
Replay of every judged look refusal (GuardRefusal, 4 right / 37 wrong) on its own transcript cut at the refused text,
HEAD vs this: right refused 4 of 4 -> 4 of 4; wrong allowed 26 of 36 replayed -> 33 of 36 (refusal Id 36's text was not
found in its transcript, so 36 of 37 wrong replayed). Tests 119 of 119 (13 of the new ones red on HEAD first). Claim words on all 443 stored look texts, HEAD vs this: 66 freed, every one read - each negated, future,
conditional, a status word or an instruction; none a completion claim. Changes were NOT touched: of the judged
"change misread" class, 6670/6672's sed edits a .sql that fw29_sql.py then RUNS (kor_review.run_sql) - a real write -
and 5020's command and 4277's command each held a real `dotnet publish` / `dotnet build`: the judges' notes were wrong
on the change, so those stay changes (6670/6672 are freed by their instruction frame).
NOT closed: 4277 ("The app is built and I'm not disputing that") and 6209 ("every test I re-ran passes" with a
drawing run unlooked) - a real claim about a DIFFERENT artefact than the unlooked change; deciding that needs the
sentence's subject understood. 5020 - a real publish, then "DONE on my side": refused, and right to be.
What it does NOT cover: a negator or modal more than three words back, or past a comma ("nothing failed, it's done"
stays a claim - correct - but so does "not, as I said, built"); an imperative not in the list ("Ship what passes");
tense from verb forms alone ("it gets built tonight"). Same-class fault it would MISS (a real claim waved through): a
claim whose last three words happen to hold a listed word - "if anyone asks, it's done" splits at the comma and is
caught, but "the step I said would fail passed" and "the one that never ran is fixed" read 'would' / 'never' and go.

stdin  : the Stop payload (transcript_path, stop_hook_active) or the PreToolUse payload (tool_name SendMessage)
stdout : nothing (allow); Stop -> {"decision": "block", ...}; PreToolUse -> permissionDecision "deny"
"""
import json
import os
import re
import sys
import time

# Repo rule 7: raw strings for every regex.
# "done(?!-when\b| when\b)": exactly "done-when" / "done when" names the board's finish-line column, not a claim
# (Framework row 151, 2 Nov 2026). Only those two forms: "done - when ..." or "done" + a line break + "When" is still a
# claim (review row 24: the first version, [\s-]+, let "All done - when you're ready" through).
CLAIM = re.compile(
    r"\b(?:done(?!-when\b| when\b)|fixed|built|rebuilt|published|deployed|applied|verified|landed|shipped|works|passes|passed"
    r"|complete|completed|ready to (?:send|issue|go)|all good|confirmed working)\b", re.I)
# row 2091: the frames in which a claim word claims nothing (not_a_claim)
NEGATORS = {"not", "never", "nothing", "no", "nor", "none", "neither", "cannot"}
MODALS = {"would", "will", "shall", "could", "might", "must", "once", "until", "till", "if", "unless", "when", "whenever"}
STATUS_AFTER = re.compile(r"\s+or\s+(?:a\s+)?DECISION\b", re.I)
IMPERATIVE = re.compile(
    r"[\s>#*\-\d)]*(?:put|commit|reply|report|send|tell|hand|write|answer|message|give|include|record|keep|wait|stop"
    r"|say|list|name|mark|leave|return)\s+(?:what|to|only|in|it|me|back|your|each|every|all|them|this|that|when|once"
    r"|at|as|now|the|an?|us|nothing|no|everything|anything|whatever|which|over)\b", re.I)
COMMIT_MSG = re.compile(r"""-m\s+(?:"(?:[^"\\]|\\.)*"|'[^']*'|@'.*?'@)""", re.S)
# row 1203: the shell's line-break escapes, so "\nSELECT" in a printf reads as a SELECT
ESCAPES = re.compile(r"\\[nrt]|`[nrt]")
# row 1203: a db-write's table, schema-qualified (an optional database part before it); brackets allowed
_NAME = r"(?:\[[^\]]+\]|\w+)"
QUALIFIED = _NAME + r"(?:\s*\.\s*" + _NAME + r")+"
WRITE_TABLE = re.compile(r"\b(?:INSERT\s+INTO|DELETE\s+FROM|MERGE\s+INTO|ALTER\s+TABLE)\s+(" + QUALIFIED + r")"
                         r"|\bUPDATE\s+(" + QUALIFIED + r")\s+SET\b", re.I)
# output cut to its first or last lines. `tail -n +N` is NOT a cut: it drops a fixed head (sqlcmd's "Changed database
# context" line) and shows every line after it
TRUNCATED = re.compile(r"\|\s*head\b|\|\s*tail\b(?!\s+(?:-n\s*)?\+\d)|Select-Object\s+[^|;\n]*-(?:First|Last)\b"
                       r"|\bgrep\s+(?:-\w+\s+)*-m\s*\d", re.I)
# the written rows returned by the write itself: each row the statement wrote, as its own result
OUTPUT_ROWS = re.compile(r"\bOUTPUT\s+(?:inserted|deleted)\.", re.I)
# row 1936: a background helper's end, as the main transcript holds it (an attachment of type queued_command)
HELPER_DONE = re.compile(r"<task-notification>\s*<task-id>([\w-]+)</task-id>(?:(?!</task-notification>).)*?<status>completed</status>", re.S)
QUOTES = "\"'`‘’“”"
# row 2032: where quoted text is a MENTION. A heredoc opener; an interpreter that runs what it is fed; a file whose body
# runs; a message flag whose quoted value is prose; a command whose quoted arguments are text or a search pattern.
# review row 1436: not a here-string (<<<) and not a shift (1<<n): no '<' or word character right before, no '<' after
HEREDOC = re.compile(r"(?<![<\w)])<<(?!<)-?[ \t]*(['\"]?)([A-Za-z_]\w*)\1")
INTERPRETER = re.compile(r"(?:^|[\s(|;&{])(?:\S*[\\/])?(?:python3?|py|bash|sh|zsh|pwsh|powershell|cmd|node|sqlcmd|iex"
                         r"|Invoke-Expression)(?:\.exe)?(?=[\s;|&)]|$)", re.I)
PIPE_TO_INTERPRETER = re.compile(r"\|\s*(?:\S*[\\/])?(?:python3?|py|bash|sh|zsh|pwsh|powershell|cmd|node|sqlcmd|iex"
                                 r"|Invoke-Expression)\b", re.I)
SCRIPT_SINK = re.compile(r">{1,2}\s*[\"']?[^\s\"'|;&]+\.(?:ps1|psm1|sh|bash|py|cmd|bat|js)\b", re.I)
SQL_SINK = re.compile(r">{1,2}\s*[\"']?[^\s\"'|;&]+\.sql\b", re.I)
SQL_RUNNER = re.compile(r"\b(?:oq|oqwide)\.py\b|\bsqlcmd\b", re.I)
PY_SCRIPT = re.compile(r"(?:python3?|py)(?:\.exe)?\s+(?:-\S+\s+)*[\"']?[^\s\"'|;&-][^\s\"'|;&]*\.py\b", re.I)
MESSAGE_FLAG = re.compile(r"""(?<![\w-])(?:-m|--message|--note|--says|--body|--because|--description|--evidence|--what"""
                          r"""|--why|--reason|--title|--summary)(?:\s+|=)("(?:[^"\\]|\\.)*"|'[^']*')""")
# review row 1436: only at a command's start (text start, a line break, ; | & ( { or after then/do/else), so an 'echo'
# inside another command's quoted code (python -c "print('echo'); ...") is not taken for one
TEXT_COMMAND = re.compile(r"(?:^|[\n;|&({])[ \t]*(?:(?:then|do|else|!)[ \t]+)*(grep|egrep|fgrep|rg|findstr|Select-String"
                          r"|echo|printf|Write-Host|Write-Output)(?![\w-])", re.I)
CACHE_S = 600
CACHE = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "KOR", "look_rules_cache.json")
SURFACE_CACHE = os.path.join(os.path.dirname(CACHE), "look_surface_cache.json")
DRAFTER_TOOLS = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "KOR.Drafter", "tools"))


def _cached_rows(cache, sql):
    """Rows of `sql` from KorStandards, cached CACHE_S at `cache`; the stale cache when the DB is down; else []."""
    try:
        st = os.stat(cache)
        if time.time() - st.st_mtime < CACHE_S:
            return json.load(open(cache, encoding="utf-8"))
    except (OSError, ValueError):
        pass
    try:
        sys.path.insert(0, DRAFTER_TOOLS)
        import standards_db  # noqa: E402
        rows = standards_db.rows(sql)
        if rows:
            os.makedirs(os.path.dirname(cache), exist_ok=True)
            json.dump(rows, open(cache, "w", encoding="utf-8"))
            return rows
    except BaseException:   # SystemExit from standards_db when the env var is missing, sqlcmd errors, timeouts
        pass
    try:
        return json.load(open(cache, encoding="utf-8"))
    except (OSError, ValueError):
        return []


def load_rules():
    """[{Kind, ChangeTools, ChangePattern, LookTools, LookPattern, Complete}], or [] when neither DB nor cache."""
    return _cached_rows(CACHE, "SELECT Kind, ChangeTools, ChangePattern, LookTools, LookPattern, OutputPattern, Complete "
                               "FROM oversight.LookRule WHERE RetiredAtUtc IS NULL")


def load_surfaces():
    """[{Code, Name, Check}] from oversight.Surface (row 1203), or [] - only ever used to NAME a check in a refusal."""
    return _cached_rows(SURFACE_CACHE, "SELECT Code, Name, [Check] FROM oversight.Surface")


def as_read(text):
    """A command as the shell runs it: the escapes printf, echo -e and PowerShell turn into line breaks become one,
    padded to the escape's two characters so every position in it is the same position in `text`. Used for LOOKS
    only: a change is matched on the raw text, because a Windows path is full of these pairs ("\\takeoff\\takeoff.exe
    model-render" is a drawing run, and read this way it is not - the first version of this fix lost exactly that)."""
    return ESCAPES.sub("\n ", text)


def look_in(lpat, raw, read, start=0):
    """The look pattern's match in a command, on its raw text or as the shell reads it, from `start`."""
    return lpat.search(raw, start) or lpat.search(read, start)


def table_key(name):
    """'KorStandards.[oversight].Commitment' -> 'oversight.commitment' (schema.table, lower case)."""
    parts = [p.strip().strip("[]").strip() for p in name.split(".")]
    return ".".join(parts[-2:]).lower()


def names_table(text, key):
    """Does `text` name the table `key` (schema.table), with or without brackets or a database part before it?"""
    for m in re.finditer(QUALIFIED, text):
        if table_key(m.group(0)) == key:
            return True
    return False


def written_tables(text, cpat):
    """[(table key or '*', end of the write in text)] - '*' for a write cpat sees that names no qualified table (db.run,
    Invoke-KorSqlScript, an unqualified name). The refusal names the keys; '*' is never named."""
    out = []; starts = set()
    for m in WRITE_TABLE.finditer(text):
        out.append((table_key(m.group(1) or m.group(2)), m.end())); starts.add(m.start())
    for m in cpat.finditer(text):
        if m.start() not in starts:
            out.append(("*", m.end()))
    return out


def result_text(c):
    """A tool_result's text: a string, or a list of text parts."""
    cc = c.get("content")
    if isinstance(cc, str):
        return cc
    if isinstance(cc, list):
        return "\n".join(p.get("text", "") for p in cc if isinstance(p, dict) and p.get("type") == "text")
    return ""


def helper_reads(path, agent):
    """The Read calls in a helper's own transcript, beside its session's: <session>/subagents/agent-<id>.jsonl."""
    reads = []
    try:
        with open(os.path.join(os.path.splitext(path)[0], "subagents", f"agent-{agent}.jsonl"), encoding="utf-8") as f:
            for line in f:
                if '"Read"' not in line:
                    continue
                try:
                    entry = json.loads(line)
                except ValueError:
                    continue
                if entry.get("type") != "assistant":
                    continue
                for c in entry.get("message", {}).get("content", []) or []:
                    if isinstance(c, dict) and c.get("type") == "tool_use" and c.get("name") == "Read":
                        reads.append(("Read", str((c.get("input") or {}).get("file_path") or "")))
    except OSError:
        pass   # no transcript, no credit
    return reads


def tool_calls(path):
    """Every tool call AND tool result in the transcript, in order: (name, text) - a command or file path for a call;
    ("__output__", its text) for a result (migration 141: a write may announce itself only in its output). A helper that has
    FINISHED adds its Read calls at the place it was launched (row 1936: see the header)."""
    out = []
    done = set()
    with open(path, encoding="utf-8") as f:
        for line in f:
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            if entry.get("type") == "attachment":
                att = entry.get("attachment")
                if isinstance(att, dict) and att.get("type") == "queued_command":
                    done.update(HELPER_DONE.findall(str(att.get("prompt") or "")))
                continue
            if entry.get("type") == "user" and isinstance(entry.get("message", {}).get("content"), list):
                for c in entry["message"]["content"]:
                    if isinstance(c, dict) and c.get("type") == "tool_result":
                        out.append(("__output__", result_text(c)))
                launched = entry.get("toolUseResult")
                if isinstance(launched, dict) and launched.get("agentId"):
                    out.append(("__helper__", str(launched["agentId"])))
                    if launched.get("status") == "completed":
                        done.add(str(launched["agentId"]))
                continue
            if entry.get("type") != "assistant":
                continue
            for c in entry.get("message", {}).get("content", []) or []:
                if not isinstance(c, dict) or c.get("type") != "tool_use":
                    continue
                inp = c.get("input") or {}
                text = str(inp.get("command") or inp.get("file_path") or inp.get("path") or "")
                out.append((c.get("name") or "", text))
    expanded = []
    for name, text in out:
        if name != "__helper__":
            expanded.append((name, text))
        elif text in done:
            expanded.extend(helper_reads(path, text))
    return expanded


def last_assistant_text(path):
    text = None
    with open(path, encoding="utf-8") as f:
        for line in f:
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            if entry.get("type") != "assistant":
                continue
            parts = [c.get("text", "") for c in entry.get("message", {}).get("content", []) or []
                     if isinstance(c, dict) and c.get("type") == "text"]
            if parts:
                text = "\n".join(parts)
    return text or ""


def change_in(cpat, text):
    """The first match of a ChangePattern that starts a command word, not the inside of one (row 2010). The drawing rule
    opens with (?:python|py) and no left boundary, so in `git show --stat x tools/virtual_drafter.py tools/drafted_set.py`
    the 'py' ending virtual_drafter.py, a space and tools/drafted_set.py read as `py tools/drafted_set.py` - a drawing
    run. A match whose first character is a letter or digit and whose previous character is a letter, digit, '_' or '.'
    is inside a word or a file name, and the search goes on from the next character."""
    pos = 0
    while True:
        m = cpat.search(text, pos)
        if not m or not m.group(0)[:1].isalnum() or m.start() == 0:
            return m
        prev = text[m.start() - 1]
        if not (prev.isalnum() or prev in "_."):
            return m
        pos = m.start() + 1


def _blank(s, a, b):
    """`s` with s[a:b] turned to spaces (line breaks kept), so every other position in it is unchanged."""
    return s[:a] + re.sub(r"[^\n]", " ", s[a:b]) + s[b:]


def _expands(q):
    """A double-quoted string the shell runs code in while expanding it: $( ) or backticks. Never a mention."""
    return q.startswith('"') and ("$(" in q or "`" in q)


def _heredoc_runs(head, kind):
    """Is a heredoc's body RUN, judged from the command line that opens it (`head`)? Yes when it is piped to or read by
    an interpreter as its code, or written to a script file; a .sql file, sqlcmd or oq.py run it as SQL, which can be
    a db-write and nothing else. A python SCRIPT reading it (guard_log.py, kor_brain.py) reads data. Everything else -
    git commit -F -, cat > note.txt, kor record - holds text."""
    if PIPE_TO_INTERPRETER.search(head) or SCRIPT_SINK.search(head):
        return True
    if SQL_SINK.search(head):
        return kind == "db-write"
    im = INTERPRETER.search(head)
    if not im:
        return False
    word = im.group(0).strip(" \t(|;&{").lower()
    if "sqlcmd" in word:
        return kind == "db-write"
    if re.search(r"(?:python3?|py)(?:\.exe)?$", word) and PY_SCRIPT.search(head):
        return kind == "db-write" and bool(SQL_RUNNER.search(head))
    return True


def _quoted_args(text, i):
    """[(start, end)] of each quoted string in the shell command from `i` to its end (an unquoted line break, ';', '|',
    ')' or a '&' that is not part of a redirect), and that end."""
    spans = []; n = len(text)
    while i < n:
        c = text[i]
        if c in "\"'":
            j = i + 1
            while j < n and text[j] != c:
                j += 2 if c == '"' and text[j] == "\\" else 1
            spans.append((i, min(j + 1, n))); i = j + 1
            continue
        if c in "\n;|)" or (c == "&" and text[i - 1:i] != ">"):
            break
        i += 1
    return spans, i


def mentions_blanked(text, kind):
    """A command with the text it only MENTIONS blanked (row 2032), for the change match of rule `kind`: the body of a
    heredoc that is not run (_heredoc_runs), the quoted value of a message flag (-m, --note, --says, --body, ...), and
    the quoted arguments of a search (grep, rg, findstr, Select-String) or of echo / printf / Write-Host /
    Write-Output - unless that output is piped to an interpreter or written to a script file (or a .sql file, for a
    db-write). A double-quoted string holding $( ) or backticks is never blanked: the shell runs that."""
    t = text
    for m in list(HEREDOC.finditer(t)):
        if t[m.start():m.end()].strip() == "":   # already blanked (inside an earlier heredoc)
            continue
        line_end = t.find("\n", m.end())
        if line_end < 0:
            continue
        head = t[t.rfind("\n", 0, m.start()) + 1:line_end]
        if "((" in head or _heredoc_runs(head, kind):   # (( ... << n )) is arithmetic (review row 1436)
            continue
        close = re.compile(r"^[ \t]*" + re.escape(m.group(2)) + r"[ \t]*$", re.M).search(t, line_end + 1)
        t = _blank(t, line_end + 1, close.start() if close else len(t))
    for m in MESSAGE_FLAG.finditer(t):
        if not _expands(m.group(1)):
            t = _blank(t, m.start(1), m.end(1))
    for m in list(TEXT_COMMAND.finditer(t)):
        spans, end = _quoted_args(t, m.end(1))
        if m.group(1).lower() in ("echo", "printf", "write-host", "write-output"):
            seg = t[m.start(1):end]
            if (PIPE_TO_INTERPRETER.match(t, end) or SCRIPT_SINK.search(seg)
                    or (kind == "db-write" and SQL_SINK.search(seg))):
                continue
        for a, b in spans:
            if not _expands(t[a:b]):
                t = _blank(t, a, b)
    return t


def unlooked(calls, rules):
    """[(rule, last change text, tables it wrote)] for every kind changed and not looked at since its last change.
    The tables are the db-write kind's (row 1203), used only to name them and their oversight.Surface in the refusal;
    [] for every other kind or a write that names no schema-qualified table."""
    missing = []
    for r in rules:
        ctools = set(r["ChangeTools"].split("|")); ltools = set(r["LookTools"].split("|"))
        cpat = re.compile(r["ChangePattern"], re.I); lpat = re.compile(r["LookPattern"], re.I)
        opat = re.compile(r["OutputPattern"]) if r.get("OutputPattern") else None
        db = r["Kind"] == "db-write"
        last_change = last_look = None; change_text = ""; tables = []
        for i, (name, text) in enumerate(calls):
            if name == "__output__":
                # a change announced in a tool's OUTPUT (141); outputs are never looks - a look is something done
                om = opat.search(text) if opat else None
                if om:
                    last_change, change_text = i, "output: " + om.group(0) + " " + text[om.end():om.end() + 60].strip()
                    q = re.search(QUALIFIED, om.group(0)) if db else None
                    tables = [table_key(q.group(0))] if q else []
                continue
            # a commit MESSAGE describes work, it does not do it: "git commit -m 'e2k_book.py: ...'" is not a drawing
            # run (the first dry run on the 10-27 transcript flagged exactly that)
            text = COMMIT_MSG.sub("-m ''", text)
            read = as_read(text) if name in ("Bash", "PowerShell") else text   # row 1203: for looks only
            # row 2032: a change is matched only in what the command RUNS - text it mentions is blanked, positions kept
            ctext = mentions_blanked(text, r["Kind"]) if name in ("Bash", "PowerShell") else text
            # the word-start skip is the drawing kind's only (review row 1419: on db-write it would drop `kor.db.run(`)
            cm = ((change_in(cpat, ctext) if r["Kind"] == "drawing" else cpat.search(ctext))
                  if name in ctools else None)
            lm = None
            if name in ltools:
                # a look in the same command counts only when it comes after the change in the text
                lm = look_in(lpat, text, read, cm.end() if cm else 0)
                # the cut is looked for AFTER the write only: a `git log | head` before it does not touch its output
                # (review row 284)
                if db and cm and not lm and not TRUNCATED.search(text, cm.end()):
                    # row 1203: OUTPUT inserted.* / deleted.* returns every row the write touched as the statement's
                    # own result - the written rows read back, unless the output is cut (call 1542: | tail -3)
                    lm = OUTPUT_ROWS.search(text, cm.end())
            if cm:
                last_change, change_text = i, text
                tables = sorted({k for k, _ in written_tables(ctext, cpat) if k != "*"}) if db else []
            if lm:
                last_look = i
        if last_change is not None and (last_look is None or last_look < last_change):
            missing.append((r, change_text, tables))
    return missing


def surface_for(key, surfaces):
    """oversight.Surface rows whose Name names the table `key` (schema.table)."""
    return [s for s in surfaces or [] if names_table(s.get("Name") or "", key)]


def reason_for(missing, claim, surfaces=None):
    lines = [f"The text claims completion (\"{claim}\") but these changes have not been LOOKED at since their last "
             "instance (Ian: a COMPLETE LOOK before any claim; rules: KorStandards oversight.LookRule):"]
    for r, text, tables in missing:
        lines.append(f"- {r['Kind']}: last change `{text[:140]}` - complete look = {r['Complete']}")
        for key in tables:
            hits = surface_for(key, surfaces)
            if not hits:
                lines.append(f"  {key}: no oversight.Surface row names it, so its look is its rows read back: a SELECT "
                             f"of {key} after the write (or OUTPUT inserted.* in it), with no tail/head on the output")
            for s in hits:
                lines.append(f"  {key}: oversight.Surface {s.get('Code')} ({s.get('Name')}) - its consumers' check: "
                             f"{s.get('Check')}. Read the rows back (a SELECT of {key}, no tail/head), and run that check")
    if any("Read" in r["LookTools"].split("|") for r, _, _ in missing):
        lines.append("A picture too big for a main session (row 1936) is looked at by a helper: launch it after the change; "
                     "its Read of the picture counts here once the helper has finished.")
    lines.append("Do the look, then claim. Or reword so the text states only what was looked at.")
    return "\n".join(lines)


def in_code(text, pos):
    """Is `pos` inside a fenced ``` block, or inside a `code span` on its own line (row 2032, refusal 41: "should read
    `DONE|1|1|0...`" quotes an expected output)?"""
    # an unbalanced fence (review row 1436) proves nothing: without the even count, no claim is skipped as fenced
    if text.count("```") % 2 == 0 and text.count("```", 0, pos) % 2:
        return True
    ls = text.rfind("\n", 0, pos) + 1
    le = text.find("\n", pos)
    before, after = text[ls:pos].replace("```", ""), text[pos:le if le >= 0 else len(text)].replace("```", "")
    return before.count("`") % 2 == 1 and "`" in after


def _words(s):
    return re.findall(r"[A-Za-z]+(?:['’][A-Za-z]+)?", s)


def not_a_claim(text, m):
    """Row 2091: is the claim word at `m` used in a frame that does not claim the work is done? The frame is read from
    the words before it in its own CLAUSE (back to . , ; : ! ? ( ) | * a line break or ' - '), at most three of them:
    (1) NEGATED - a negator among them ("Release was NOT built", "Not built yet", "nothing built on it");
    (2) FUTURE or CONDITIONAL - a modal or subordinator among them ("it would have to be built", "I'll ask once it's
        built", "sent as each passes"); `should` and `can` are NOT in the list: "should be fixed now" is the hedged
        claim this guard exists for;
    (3) a STATUS WORD - done/DONE right after 'at' or 'only', or followed by 'or (a) DECISION' ("you hear from me next
        at DONE", "report only DONE or DECISION", "Reply only at done");
    (4) inside an INSTRUCTION - its SENTENCE (back to . ! ? or a line break; a ':' does not end it) opens with an
        imperative verb followed by a function word ("Commit what works now", "Put in the state: the items done").
        The function word is what keeps "Commit 3f2a landed" and "Report written - done" claims."""
    s = text[:m.start()]
    clause = re.split(r"[.,;:!?()|*\n]| - | — ", s)[-1]
    last = [w.lower() for w in _words(clause)[-3:]]
    if any(w in NEGATORS or w.endswith("n't") or w.endswith("n’t") for w in last):
        return True
    if any(w in MODALS or w.endswith("'ll") or w.endswith("’ll") for w in last):
        return True
    if any(a == "to" and b == "be" for a, b in zip(last, last[1:])) or any(
            a == "as" and b in ("each", "soon") for a, b in zip(last, last[1:])):
        return True
    word = m.group(0).lower()
    if word == "done" and (last[-1:] in (["at"], ["only"]) or STATUS_AFTER.match(text, m.end())):
        return True
    sentence = re.split(r"[.!?\n]", s)[-1]
    return bool(IMPERATIVE.match(sentence))


def claim_in(text):
    """The first claim word that is USED, not mentioned: a word wrapped in quotes ("passes") is skipped (row 1203); so
    is one in a code span or fence (row 2032), and one in a negated, future/conditional, status-word or instruction
    frame (row 2091, not_a_claim)."""
    text = text or ""
    for m in CLAIM.finditer(text):
        before = text[m.start() - 1] if m.start() else ""
        after = text[m.end()] if m.end() < len(text) else ""
        if before and after and before in QUOTES and after in QUOTES:
            continue
        if in_code(text, m.start()):
            continue
        if not_a_claim(text, m):
            continue
        return m
    return None


def verdict(text, calls, rules, surfaces=None):
    """The refusal reason, or None when the text may go."""
    m = claim_in(text)
    if not m:
        return None
    missing = unlooked(calls, rules)
    return reason_for(missing, m.group(0), surfaces) if missing else None


def log_refusal(text, why, payload):
    """F11 (Framework row 1159): every refusal is a row - Rule = the unlooked kinds, Matched = the claim in its sentence.
    Logging can never change what this guard does."""
    try:
        sys.path.append("C:/VIsual Studio Projects/Framework/tools"); import guard_log  # noqa: E702
        m = claim_in(text)
        kinds = ",".join(ln[2:].split(":")[0] for ln in why.splitlines() if ln.startswith("- "))
        guard_log.record("look", kinds, text[max(0, m.start() - 150):m.end() + 150] if m else why, None, payload=payload)
    except Exception:
        pass


def main():
    try:
        payload = json.load(sys.stdin)
    except ValueError:
        return
    path = payload.get("transcript_path")
    if not path or not os.path.exists(path):
        return
    rules = load_rules()
    if not rules:
        return
    try:
        calls = tool_calls(path)
    except OSError:
        return
    surfaces = load_surfaces()
    if payload.get("tool_name") == "SendMessage":
        msg = (payload.get("tool_input") or {}).get("message") or ""
        why = verdict(msg if isinstance(msg, str) else json.dumps(msg), calls, rules, surfaces)
        if why:
            log_refusal(msg if isinstance(msg, str) else json.dumps(msg), why, payload)
            json.dump({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                              "permissionDecisionReason": why}}, sys.stdout)
        return
    if payload.get("stop_hook_active"):
        return
    try:
        text = last_assistant_text(path)
        why = verdict(text, calls, rules, surfaces)
    except OSError:
        return
    if why:
        log_refusal(text, why, payload)
        json.dump({"decision": "block", "reason": why}, sys.stdout)


if __name__ == "__main__":
    main()
