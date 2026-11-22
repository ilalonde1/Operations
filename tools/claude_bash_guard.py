"""PreToolUse guard for Bash: block the mistakes this repo has actually paid for.

CLAUDE.md states every one of these in prose. Prose is read once at the start of a session
and then loses to momentum -- on 2026-08-25 all four were broken, each within minutes of the
rule being quoted out loud. A hook fires at the moment of the action, which is the only
moment that matters.

No jq: it is not installed on this machine, and a jq-based hook exits 0 silently, which
reads as "allowed" and protects nothing.

stdin  : the PreToolUse payload
stdout : nothing (allow), or a permissionDecision of "deny"
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import time

class HeredocEscape:
    """Rule 3b: distinguish Bash expansion, Python source, and verbatim input (see CHECKS below).

    This is a bounded shell reader, not an interpreter: it never runs a command or follows a written file.
    Python's tokenizer identifies literals without evaluating their escapes or executing their contents.
    """
    SHELL_TOKEN = re.compile(
        r"(?P<space>[ \t\r]+)|(?P<comment>\#[^\n]*)|(?P<newline>\n)"
        r"|(?P<op><<<|<<-|<<|>>|[<>]&|&>|[;&|()<>])"
        r"|(?P<word>(?:'[^']*'|\"(?:\\[\s\S]|[^\"\\])*\"|\\[\s\S]|[^\s'\"\\;&|()<>])+)")
    PYTHON = re.compile(r"(?i)^(?:python(?:\d+(?:\.\d+)*)?|py)(?:\.exe)?$")
    PREFIX = re.compile(r"(?i)^([rubf]*)(?:'''|\"\"\"|'|\")")
    # Hand-doubled C# verbatim regexes are still against rule 7 (real refusal 4970).
    CSHARP_REGEX = re.compile(r'@"[^"\n]*\\[abfnrtvBdDsSwWAZzG.?*+()\[\]{}|^$]')
    ESCAPE = re.compile(r"\\(?:x[0-9a-fA-F]{2}|u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|N\{[^}\n]+\}|[0-7]{1,3}|[\s\S])")

    @classmethod
    def python_stdin(cls, tokens):
        """Recognize Python reading source from stdin, not a script/-c/-m reading data from stdin."""
        words = []
        i = 0
        while i < len(tokens):
            if tokens[i] in ('<', '>', '>>', '>&', '<&', '&>'):
                if words and words[-1].isdigit():
                    words.pop()  # file descriptor on a redirect
                i += 2
                continue
            words.append(tokens[i])
            i += 1
        i = 0
        while i < len(words):
            word = words[i]
            if re.match(r"^[A-Za-z_]\w*=", word) or word in ('env', 'command', 'time', 'exec', 'do', 'then'):
                i += 1
            elif word == 'timeout' and i + 1 < len(words):
                i += 2
            elif word in ('sudo', 'nice') or (word == 'uv' and words[i:i + 2] == ['uv', 'run']):
                wrapper = word
                i += 2 if wrapper == 'uv' else 1
                takes_value = {'sudo': {'-u', '-g', '-h', '-p', '-C', '-T'},
                               'nice': {'-n', '--adjustment'},
                               'uv': {'--project', '--directory', '--python', '--with', '--with-editable'}}[wrapper]
                while i < len(words) and words[i].startswith('-'):
                    option = words[i]
                    i += 2 if option in takes_value else 1
                    if option == '--':
                        break
            else:
                break
        if i == len(words) or not cls.PYTHON.fullmatch(words[i].replace('\\', '/').rsplit('/', 1)[-1]):
            return False
        i += 1
        while i < len(words):
            word = words[i]
            if word == '-':
                return True  # subsequent words are script arguments
            if word == '--':
                return i + 1 == len(words) or words[i + 1] == '-'
            if word in ('-c', '-m') or word.startswith(('-c', '-m')):
                return False
            if word in ('-X', '-W'):
                i += 2
            elif word.startswith('-'):
                i += 1
            else:
                return False  # script filename
        return True

    @classmethod
    def bodies(cls, cmd):
        """Read shell headers and their queued bodies once; never find new headers inside a body.

        Each pending redirect retains its command and pipeline. Command substitutions are executable
        even inside double quotes; arithmetic shifts and literal quoted text are not redirects.
        Quotes/backslashes in a delimiter suppress Bash expansion; <<- strips tabs only.
        """
        import shlex
        found = []

        def arithmetic(pos):
            depth = 0
            while pos < len(cmd):
                if cmd[pos] == '(':
                    depth += 1
                elif cmd[pos] == ')':
                    depth -= 1
                    if depth == 0:
                        return pos + 1
                pos += 1
            return pos

        def word(pos, delimiter=False):
            start, quote = pos, None
            while pos < len(cmd):
                c = cmd[pos]
                if c == '\\' and quote != "'":
                    pos += 2
                    continue
                if quote == "'":
                    if c == quote:
                        quote = None
                elif not delimiter and cmd.startswith('$((', pos):
                    pos = arithmetic(pos + 1)
                    continue
                elif not delimiter and cmd.startswith('$(', pos):
                    pos = shell(pos + 2, substitution=True)
                    continue
                elif c == quote:
                    quote = None
                elif quote is None:
                    if c in "'\"":
                        quote = c
                    elif c.isspace() or c in ';&|()<>':
                        break
                pos += 1
            return cmd[start:pos], pos

        def unquote(raw):
            try:
                return shlex.split(raw)[0]
            except (ValueError, IndexError):
                return raw

        def forwarded(consumer, pipeline):
            # Only byte-preserving producers; a file argument to cat replaces stdin.
            plain, i = [], 0
            while i < len(consumer):
                if consumer[i] in ('<', '>', '>>', '>&', '<&', '&>'):
                    if plain and plain[-1].isdigit():
                        plain.pop()
                    i += 2
                else:
                    plain.append(consumer[i]); i += 1
            passthrough = plain and (plain[0] == 'tee' or
                                      (plain[0] == 'cat' and all(w in ('-', '--') for w in plain[1:])))
            index = next(i for i, part in enumerate(pipeline) if part is consumer)
            return pipeline[index + 1] if passthrough and index + 1 < len(pipeline) else consumer

        def shell(pos, substitution=False):
            words, pending, depth = [], [], 0
            pipeline = [words]
            while pos < len(cmd):
                c = cmd[pos]
                if c in ' \t\r':
                    pos += 1
                    continue
                if c == '#':
                    end = cmd.find('\n', pos)
                    pos = len(cmd) if end < 0 else end
                    continue
                if c == '\n':
                    pos += 1
                    for delim, quoted, tabs, consumer, chain in pending:
                        end = re.search(r'(?m)^' + (r'\t*' if tabs else '') + re.escape(delim) + r'\r?$', cmd[pos:])
                        stop = pos + end.start() if end else len(cmd)
                        found.append((quoted, forwarded(consumer, chain), cmd[pos:stop]))
                        pos = pos + end.end() if end else len(cmd)
                        if pos < len(cmd) and cmd[pos] == '\n':
                            pos += 1
                    words, pending = [], []
                    pipeline = [words]
                    continue
                if cmd.startswith('((', pos):
                    pos = arithmetic(pos)
                    continue
                op = cls.SHELL_TOKEN.match(cmd, pos) if c in ';&|()<>' else None
                if op and op.lastgroup == 'op':
                    spelling = op.group(); pos = op.end()
                    if spelling in ('<<', '<<-'):
                        while pos < len(cmd) and cmd[pos] in ' \t':
                            pos += 1
                        raw, pos = word(pos, delimiter=True)
                        if raw:
                            pending.append((unquote(raw), any(q in raw for q in "'\"\\"),
                                            spelling == '<<-', words, pipeline))
                    elif spelling in (';', '&', '|', '(', ')'):
                        if spelling == ')' and substitution and depth == 0:
                            return pos
                        depth += (spelling == '(') - (spelling == ')')
                        words = []
                        if spelling == '|' and not cmd.startswith('|', pos):
                            pipeline.append(words)
                        else:
                            pipeline = [words]
                    else:
                        words.append(spelling)
                    continue
                raw, end = word(pos)
                if end == pos:
                    pos += 1
                else:
                    words.append(unquote(raw)); pos = end
            return pos

        shell(0)
        yield from found

    @classmethod
    def unsafe_literal(cls, text):
        if cls.CSHARP_REGEX.search(text):
            return True
        drive = bool(re.search(r"[A-Za-z]:\\", text))
        for match in cls.ESCAPE.finditer(text):
            escape = match.group()[1:]
            if escape in ('\\', "'", '"'):
                continue
            if drive or escape[0] in 'b01234567':
                return True
            if escape in ('n', 'r', 't', 'a', 'f', 'v', '$', '`') or escape.isspace():
                continue
            if escape[0] in 'xuUN' and len(escape) > 1:
                continue  # complete explicit character escapes, e.g. git separators / JSON test data
            return True  # unknown or incomplete escape, e.g. \P, \d, \U in a Windows path
        return False

    @classmethod
    def unsafe_python(cls, body):
        import io
        import tokenize
        fstrings = []
        try:
            for token in tokenize.generate_tokens(io.StringIO(body).readline):
                if token.type == tokenize.STRING:
                    prefix = cls.PREFIX.match(token.string)
                    if prefix and 'r' not in prefix.group(1).lower() and cls.unsafe_literal(token.string[prefix.end():]):
                        return True
                elif token.type == getattr(tokenize, 'FSTRING_START', -1):
                    fstrings.append('r' in cls.PREFIX.match(token.string).group(1).lower())
                elif token.type == getattr(tokenize, 'FSTRING_MIDDLE', -1):
                    if fstrings and not fstrings[-1] and cls.unsafe_literal(token.string):
                        return True
                elif token.type == getattr(tokenize, 'FSTRING_END', -1):
                    fstrings.pop()
        except (tokenize.TokenError, IndentationError, SyntaxError):
            pass  # incomplete source is not proof of this escape fault; completed tokens were checked
        return False

    def search(self, cmd):
        for quoted, consumer, body in self.bodies(cmd):
            if not quoted and "\\\\" in body:
                return True
            if self.python_stdin(consumer) and self.unsafe_python(body):
                return True
        return None


class QuotesBlanked:
    """Rule 8's matcher (narrowed 12 Nov): the pattern runs on the command with every quoted string emptied, so a '|' inside
    --filter "A|B", or 'dotnet build' inside an echo or a JSON string, is not a pipe or a build."""
    QUOTED = re.compile(r"\"(?:[^\"\\\n]|\\.)*\"|'[^'\n]*'")

    def __init__(self, pattern):
        self.pattern = pattern

    def search(self, cmd):
        return self.pattern.search(self.QUOTED.sub('""', cmd))


class PipedBuild:
    """Rule 8: retain the compiler's status, with literal pipefail state or its immediate PIPESTATUS element.

    Two same-length shell views separate syntax from expansions. Single-quoted/escaped variables, comments and
    heredoc data are never reads; double-quoted command substitutions still contain executable commands.
    This is a bounded lexical check, not control-flow or arbitrary shell evaluation (limits beside the rule).
    """
    CALL = re.compile(r"(?i)(?:^|[;&|({\n])\s*"   # '{' too (21 Nov): `{ dotnet build X | tail; }` was never matched
                      r"(?:(?:timeout(?:\s+-\S+)*\s+\S+|time|env|nice|command|[A-Za-z_]\w*=\S*)\s+)*"
                      r"(?:\S*[/\\])?dotnet(?:\.exe)?\s+(?:build|test|publish)\b(?:[^|;&\n]|(?<=>)&|&(?=>))*\|(?!\|)")
    # Background & ends a pipeline; descriptor duplication and |& do not.
    END = re.compile(r";|&&|\|\||\n|(?<![>&|])&(?![>&])")
    READ = re.compile(r"\$PIPESTATUS\b(?!\w)|\$\{PIPESTATUS(?:\[(\d+|[@*])\])?\}")
    SET = re.compile(r"(?:^|[;({\n]|&&)\s*set\s+([+-])o\s+pipefail(?=\s|[;&)]|$)")

    @staticmethod
    def views(cmd):
        import shlex
        syntax, expansion = [' '] * len(cmd), [' '] * len(cmd)

        def scan(pos, quote=None, stop=False):
            pending, depth = [], 0
            while pos < len(cmd):
                c = cmd[pos]
                if c == '\\':
                    pos += 2
                    continue
                if quote == '"' and c == '"':
                    return pos + 1
                if c == "'" and quote is None:
                    end = cmd.find("'", pos + 1)
                    pos = len(cmd) if end < 0 else end + 1
                    continue
                if c == '"' and quote is None:
                    pos = scan(pos + 1, quote='"')
                    continue
                if cmd.startswith('$(', pos):
                    syntax[pos:pos + 2] = expansion[pos:pos + 2] = '$('
                    pos = scan(pos + 2, stop=True)
                    continue
                if quote is not None:
                    expansion[pos] = c
                    pos += 1
                    continue
                if c == '#' and (pos == 0 or cmd[pos - 1].isspace() or cmd[pos - 1] in ';|&('):
                    end = cmd.find('\n', pos)
                    pos = len(cmd) if end < 0 else end
                    continue
                if cmd.startswith('<<', pos) and not cmd.startswith('<<<', pos):
                    header = re.match(r"<<(-?)[ \t]*(\S+)", cmd[pos:])
                    if header:
                        try:
                            delim = shlex.split(header[2])[0]
                            pending.append((delim, bool(header[1])))
                        except (ValueError, IndexError):
                            pass
                syntax[pos] = expansion[pos] = c
                pos += 1
                if c == '\n':
                    for delim, tabs in pending:
                        end = re.search(r'(?m)^' + (r'\t*' if tabs else '') + re.escape(delim) + r'\r?$', cmd[pos:])
                        pos = pos + end.end() if end else len(cmd)
                        if pos < len(cmd) and cmd[pos] == '\n':
                            pos += 1
                    pending = []
                elif c == '(':
                    depth += 1
                elif c == ')':
                    if stop and depth == 0:
                        return pos
                    depth -= 1
            return pos

        scan(0)
        return ''.join(syntax), ''.join(expansion)

    def pipefail(self, text, upto):
        settings = {m.end() - 1: m[1] == '-' for m in self.SET.finditer(text, 0, upto)}
        enabled, stack = False, []
        for pos, c in enumerate(text[:upto]):
            if pos in settings:
                enabled = settings[pos]
            if c == '(':
                stack.append(enabled)
            elif c == ')' and stack:
                enabled = stack.pop()
        return enabled

    def reads_status(self, text, start, end, index):
        segment = text[start:end]
        # A nested substitution can execute commands that replace the inherited status.
        # Conservatively require the read in this command's own expansions.
        nested = segment.find('$(')
        if nested >= 0:
            segment = segment[:nested]
        for m in self.READ.finditer(segment):
            element = m[1]
            if element in ('@', '*') or (int(element or '0') == index):
                return True
        return False

    def search(self, cmd):
        text, expansion = self.views(cmd)
        for m in self.CALL.finditer(text):
            if self.pipefail(text, m.start()):
                continue
            # Help does not invoke a compiler or tests (live probe 9586).
            if re.search(r'(?:^|\s)(?:--help|-h|-\?)(?=\s|[|]|$)', text[m.start():m.end()]):
                continue
            end = self.END.search(text, m.end())
            if end:
                rest = text[m.end():end.start()]
                # A subshell exports only its final status. The existing brace refusal is conservative:
                # Bash actually preserves PIPESTATUS across a brace group, but its required deny stays.
                in_group = rest.count(")") > rest.count("(") or rest.count("}") > rest.count("{")
                nxt = self.END.search(text, end.end())
                prior = text[:m.start()]
                boundary = max([b.end() for b in self.END.finditer(prior)] +
                               [prior.rfind('(') + 1, prior.rfind('{') + 1, 0])
                # CALL includes the preceding | when dotnet is not pipeline element zero.
                prefix = text[boundary:m.start()] + '|' if m.group()[0] == '|' else ''
                index = len(re.findall(r'\|(?!\|)', prefix))
                if not in_group and self.reads_status(expansion, end.end(), nxt.start() if nxt else len(cmd), index):
                    continue
            return m
        return None


def blank_quotes(cmd, keep=None):
    """cmd with the inside of every quoted string replaced by spaces - same length, so a position in the result is a
    position in cmd. A quoted string whose inside matches keep (e.g. a quoted path to an executable) is left as it is."""
    def sub(m):
        inner = m.group(0)[1:-1]
        return m.group(0) if keep and keep.match(inner) else m.group(0)[0] + " " * len(inner) + m.group(0)[-1]
    return QuotesBlanked.QUOTED.sub(sub, cmd)


class UnsavedCheckout:
    """Rule 2's matcher for `git checkout/restore -- <paths>` (narrowed 12 Nov, see the rule): .search(cmd) is truthy
    unless the same command, BEFORE the discard, saved the work (git stash, a cp / Copy-Item, or a git diff/show
    redirected to a file) and names every path it discards."""
    CALL = re.compile(r"git\s+(?:checkout|restore)\s+--(?=\s|$)([^;&|\n)]*)")
    STASH_ALL = re.compile(r"git\s+stash(?:\s+push)?\s*(?:$|[;&|)\n])")
    SAVE = re.compile(r"git\s+stash\s+(?:push|save)\b"
                      r"|(?:^|[;&|(\n]\s*|\bdo\s+|\bthen\s+)(?:cp|Copy-Item)\b"
                      r"|git\s+(?:diff|show)\b(?![^;&|\n]*--(?:stat|shortstat|numstat|name-only|name-status)\b)"
                      r"[^;&|\n]*?(?<![\d&>])>{1,2}(?!&)")

    def search(self, cmd):
        blank = blank_quotes(cmd)
        for m in self.CALL.finditer(blank):
            args = [a.strip("\"'") for a in cmd[m.start(1):m.end(1)].split()]
            before = cmd[:m.start()]
            if self.STASH_ALL.search(blank[:m.start()]):   # a bare `git stash` / `git stash push` keeps every change
                continue
            # each path must stand in `before` as a whole path (review row 2025: 'tools/xa.py' is not 'a.py')
            if args and self.SAVE.search(blank[:m.start()]) and all(
                    re.search(r"(?<![\w./\\-])" + re.escape(a) + r"(?![\w./\\-])", before) for a in args):
                continue
            return m
        return None


# Repo rule 7 in force here too: every pattern is a raw string.
RULES = [
    # 1. A RECURSIVE WALK OVER SMB. db/tools/verify_e2k_claims.ps1 says it in its own
    #    header: "Get-ChildItem -Recurse over SMB is unusably slow." Two 120s timeouts
    #    came from exactly this, against \\Kor-fs01 and \\KOR-302N, both returning nothing.
    (re.compile(r"(?:^|[;&|(]\s*)(?:find|ls\s+-\w*R)\s+['\"]?(?://|\\\\)"),
     "Recursive walk over SMB. CLAUDE.md rule 1: unusably slow -- it will time out and "
     "return nothing. List one directory at a time, or push the filter to the filesystem."),

    (re.compile(r"Get-ChildItem[^|;]*-Recurse[^|;]*(?://|\\\\)\w"),
     "Get-ChildItem -Recurse over a UNC path. CLAUDE.md rule 1: unusably slow. "
     "Target one directory, or filter at the filesystem."),

    # 1b. EVERY OTHER FORM OF THE SAME WALK (Mistake 120, 2 Nov: a Python os.walk counted 50,352 files of the fs01 IT
    #     archive file by file from KOR-1001 over the VPN, ~10 min, while the rule above caught only the PowerShell
    #     form with -Recurse BEFORE the path). The class: a command that enumerates a share's tree from this PC.
    #     A UNC path here = // or \\ at the START of a path (the command's start, or after whitespace, a quote, ( or =),
    #     then a host name (beginning with a letter, or a dotted IP address), then / or \. So https://, C:\\x (doubled
    #     backslashes after a drive) and integer division ` //2` are not UNC paths.
    #     CHECKS, each with a UNC path in the same pipeline segment: Get-ChildItem / gci / ls / dir with -r or -Recurse*
    #     in any position; dir /s; tree; robocopy /L with /S or /E (a listing); du; python / py in command position
    #     whose text that follows - across lines, so a heredoc body counts - holds os.walk, rglob or recursive=True AND
    #     a UNC path, before any && or ||.
    #     ALLOWS: one folder's listing (no recurse flag), git ls-remote / push / fetch to an fs01 remote, Test-Path,
    #     a robocopy that copies (no /L), any walk of a local path - including one run ON the target via
    #     Invoke-CimMethod with the target's local path (E:\...).
    #     DOES NOT CATCH: a walk inside a script FILE (`python walk.py //Kor-fs01/IT` - Mistake 120's own form if its
    #     walk was in a file; the share path is only an argument), a UNC held in a variable or a mapped drive letter
    #     (Z:\ is local-looking), a .NET Directory.EnumerateFiles in a PowerShell one-liner.
    #     KNOWN FALSE REFUSALS: bash `ls -r //host/x` (-r = reverse order there, one folder) is read as a recurse; a
    #     python one-liner that walks a local path and merely names a share in the same segment (`os.walk('.')` and a
    #     print of '//Kor-fs01/IT/') is refused; a python walk joined to a UNC listing by a single `;` or `|` is refused.
    (re.compile(r"(?i)(?:^|[;&|(\n{]\s*)(?:"
                r"(?:Get-ChildItem|gci|ls|dir)\b(?=[^|;\n]*\s-r(?:ecurse\w*)?\b)"
                r"|(?:cmd(?:\.exe)?\s+/c\s+)?dir\b(?=[^|;\n]*\s/s\b)"
                r"|tree(?:\.com)?\b"
                r"|robocopy(?:\.exe)?\b(?=[^|;\n]*\s/L\b)(?=[^|;\n]*\s/(?:S|E)\b)"
                r"|du\b"
                r")[^|;\n]*?(?:^|(?<=[\s'\"(=]))(?://|\\\\)(?:[A-Za-z][\w.$-]*|\d+\.[\d.]+)[/\\]"
                r"|(?:^|[;&|(\n]\s*)(?:\S*[/\\])?(?:python[\w.]*|py)\b"
                r"(?=(?:(?!&&|\|\|)(?s:.))*?(?:os\.walk|rglob|recursive\s*=\s*True))"
                r"(?=(?:(?!&&|\|\|)(?s:.))*?(?<=[\s'\"(=])(?://|\\\\)(?:[A-Za-z][\w.$-]*|\d+\.[\d.]+)[/\\])"),
     "A recursive walk of a network share from this PC (Mistake 120, 2 Nov: 50,352 files of the fs01 IT archive walked "
     "file by file over the VPN). Run it ON the target - Invoke-CimMethod -ComputerName <host> -ClassName "
     "Win32_Process -MethodName Create -Arguments @{CommandLine='<the walk, on its local path, writing one result "
     "file>'} - and read that one result back (memory feedback_run_on_the_target_never_chatty_over_vpn). One folder's "
     "listing without a recurse flag is allowed."),

    # 2. DESTROYING UNCOMMITTED WORK. On 2026-08-25 a `git checkout --` wiped a parallel
    #    session's uncommitted module. There is no undo; it survived only because a
    #    transcript still held the edit. `git stash push` keeps it.
    #    NARROWED 12 Nov (FW-RESET-2 M8, RESET-PLAN item 3): judged on transcripts 1 of 5 right (6790: only a
    #    `git diff --stat` before it, in a shared checkout) and 4 of 5 wrong - 4822, 5092 saved the paths with
    #    `git diff -- <p> > file`, 6472 and 5618 cp'd them aside first, in the same command.
    #    CHECKS (UnsavedCheckout): every `git checkout|restore -- <paths>` outside a quoted string.
    #    ALLOWS: one whose command, BEFORE it, has a save step - git stash, cp / Copy-Item in command position, or a
    #      git diff/show (not --stat/--name-only) redirected to a file - and names every path it discards.
    #    DOES NOT CATCH: a save step that writes nothing (cp to a folder that does not exist, an empty diff of an
    #      untracked file), or a save of different paths that merely mention the discarded ones; a discard through a
    #      variable that the save step never expanded the same way; a discard inside a quoted string run by another
    #      shell (bash -c "git checkout -- x", pwsh -Command "...") - quoted strings are emptied first, as in rules 6/8.
    (UnsavedCheckout(),
     "git checkout/restore -- discards uncommitted work with no undo, and another session "
     "may own it. Use: git stash push -- <paths>, or save each path first in the same command "
     "(git diff -- <paths> > file, or cp)."),

    (re.compile(r"git\s+reset\s+--hard|git\s+clean\s+-\w*[dfx]"),
     "git reset --hard / git clean destroys uncommitted work with no undo. "
     "Use: git stash push"),

    # 3. A WINDOWS PATH OR REGEX THROUGH SHELL QUOTING. Repo rule 7. Three attempts on
    #    2026-08-25 produced "Invalid escape \P", a UNC collapsed to a single backslash,
    #    and \03 read as a control character -- each time silently.
    (re.compile(r"python3?\s+-c\b[^\n]*\\\\"),
     "Backslashes inside python -c. Repo rule 7: bash eats them and the path or regex "
     "silently changes. Write the script to a file with the Write tool and run that."),

    #    NARROWED 12 Nov (FW-RESET M8, RESET-PLAN item 3): `<<'EOF' ... \\[A-Za-z]` matched ANY backslash-letter after a
    #    heredoc began. 587 refusals in the week to 12 Nov; judged on transcripts, 1 of 8 right (4970: a C# regex through a
    #    non-raw Python string) and 7 of 8 wrong - '\r\n' and '\n' in Python strings, regexes already in r'' literals, all
    #    in quoted heredocs where bash changes nothing (5120: nl='\r\n' if '\r\n' in t else '\n').
    #    NARROWED 21 Nov: bind each body to its consumer; tokenize Python source, not prose or source written by cat.
    #    CHECKS (HeredocEscape): an UNQUOTED delimiter whose body holds '\\' (Bash collapses it, any consumer);
    #      for python/py reading source from stdin, non-raw string/f-string text with \b, octal, unknown/incomplete
    #      escapes, or a drive path containing an active escape; C# @"..." regexes even when doubled by hand (4970).
    #      Headers/bodies are consumed once, including queued same-line heredocs and nested $(...) in double quotes;
    #      arithmetic shifts are not headers. Also follows uv run/sudo/nice and direct cat/tee pipes to Python stdin.
    #    ALLOWS: quoted cat/tee/git messages and source-file writes (even when run later); other non-Python consumers;
    #      Python comments, raw/rf/fr/br/rb literals, deliberate \n/\r/\t/\a/\f/\v (outside C# @"...") and complete \x/\u/\U/\N escapes;
    #      ordinary doubled slashes/escaped quotes; stdin data to Python script/-c/-m; unquoted cat's single \P.
    #    DOES NOT CATCH: source passed through a pipeline/wrapper/alias this bounded reader cannot recognize;
    #      backtick substitutions or substitutions inside arithmetic; later evaluation of files or strings (exec); an unsafe
    #      f-string expression hidden by a raw string on Python <3.12; malformed source after tokenization stops;
    #      relative paths made solely of otherwise allowed escapes (e.g. 'work\new'); C# ordinary-string regexes
    #      doubled by hand without the @" marker. Does not prove author intent: \b/octal deliberately used as
    #      control data and deliberately doubled unquoted heredocs still refuse. No future compiler/file inspection.
    (HeredocEscape(), "A backslash escape inside a heredoc that Python (or bash, for an unquoted delimiter) will change. Repo rule 7: "
     "this is how \\P, \\03, a \\b backspace in a C# regex and a lost UNC prefix happened. Use a raw literal (r'...'), or "
     "the Write or Edit tool for a Windows path or regex."),

    # 4. KILLING A PROCESS BY NAME. Several Claude sessions run test suites on this box at once.
    #    On 2026-09-18 a session's pre-build `Get-Process testhost* | Stop-Process -Force` and
    #    `taskkill /F /IM testhost.exe` ended the PDF-intake suite six times (exit code -1, no
    #    dump, "Test host process crashed"); two hours went to crash hypotheses before the
    #    neighbours' transcripts named it. Kill only your own, by its command line.
    #    The verb must stand where a command stands (the start, or after ; & | ( or a newline), so a
    #    commit message or a comment that NAMES the mistake is not the mistake.
    (re.compile(r"(?i)(?:^|[;&|(\n]\s*)(?:Stop-Process|taskkill|pkill|killall)[^|;\n]*\b(?:testhost|dotnet|takeoff|vstest)\b"
                r"|(?:^|[;&|(\n]\s*)Get-Process\s+[^|;\n]*\b(?:testhost|dotnet|takeoff|vstest)[^|;\n]*\|\s*Stop-Process"),
     "A kill by process NAME (testhost/dotnet/takeoff/vstest). Other Claude sessions run suites on "
     "this machine; a name-wide kill ends theirs too (six runs lost on 2026-09-18). Kill only your own, "
     "by command line: Get-CimInstance Win32_Process -Filter \"Name='testhost.exe'\" | "
     "Where-Object { $_.CommandLine -like '*<your test project>*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }"),

    # 8. A BUILD OR TEST RUN PIPED WITHOUT pipefail. The pipe's exit code replaces the compiler's.
    #    On 2026-10-14 `dotnet build ... 2>&1 | tail -30` exited 0 over four CS0104 errors; "build
    #    green" was reported, the filtered tests and the render harness then ran on the previous
    #    binaries, and five stale PNGs were read as evidence of the new code. CLAUDE.md rule 5.
    #    NARROWED 12 Nov (FW-RESET M8): 62 refusals in the week, judged 1 of 8 right (4753: dotnet test | grep). 5 of 8
    #    wrong were the '|' inside --filter "A|B" with the output sent to a file and the exit code echoed (6351); 2 of 8
    #    were 'dotnet build' inside a JSON or echo string (3565, 5361). So quoted strings are emptied first and dotnet
    #    must stand in command position. DOES NOT CATCH: a build run through a variable ($D build | tail).
    #    12 Nov (review row 1988 minor): a wrapper in front of dotnet - timeout N, time, env, nice, command, VAR=x - is
    #    still command position (`timeout 600 dotnet test X | tail` refused again). DOES NOT CATCH either: a build
    #    inside a quoted string run by bash -c / pwsh -Command (the quotes are emptied first).
    #    NARROWED 21 Nov (plan step 3, PipedBuild): judged since 12 Nov 13 right / 10 wrong. 9 of the 10 read the exit
    #    code with ${PIPESTATUS[0]} in the very next command (8594: `dotnet build | grep ...; echo rc=${PIPESTATUS[0]}`),
    #    which office rule 13 itself names as a fix; 1 (8275) was a pipe in a LATER command after && (`dotnet build ... >
    #    log && P=$(ls | head -1)`) read as the build's. CHECKS: a dotnet build/test/publish in command position whose
    #    own pipeline (it stops at ; && || or a newline) has a '|', unless the command says pipefail or the NEXT
    #    command reads its compiler element of PIPESTATUS (or the whole array). Step-3 independent review:
    #    CHECKS: literal `set -o pipefail` BEFORE the build in its shell scope, respecting `set +o pipefail`;
    #      actual variable expansions in the immediately following command, including double quotes. Comments,
    #      single quotes, escaped dollars and heredoc data cannot supply protection. $(...) remains executable.
    #      ; && || newline and background & end the pipeline. The element index starts afresh at each boundary.
    #    ALLOWS: `...| tail; echo ${PIPESTATUS[0]}`, `...| head && echo "rc=${PIPESTATUS[0]}"`, array capture,
    #      unpiped builds before a later pipe, and --help/-h/-? (9586 invokes no compiler).
    #    DOES NOT CATCH: a status read but ignored, aliases/variables as the executable, arbitrary wrappers or
    #      eval/bash -c/pwsh -Command strings; full shell control flow (`false && set -o pipefail; build | tail`
    #      can appear protected), dynamic option changes, or functions changing pipefail at runtime.
    #    Conservative limits: nested-substitution status reads are not credited; the existing brace-group deny
    #      stays. Bash preserves PIPESTATUS across braces (unlike a subshell), so review 2636's explanation was wrong.
    (PipedBuild(),
     "A dotnet build/test/publish piped without pipefail: the pipe's exit code replaces the build's "
     "(2026-10-14: four CS0104 errors reported as a green build; tests and PNGs then ran on stale "
     "binaries). Prefix the command with `set -o pipefail &&`, read ${PIPESTATUS[0]} in the very next command, "
     "or do not pipe it."),

    # 9. THE SAME FAULT, THROUGH THE WRAPPER SCRIPT. Rule 8 matches the literal `dotnet` command, and
    #    nothing in this shop runs the compiler directly: every deploy is a .ps1 that wraps it
    #    (publish.ps1, publish-networkops.ps1, publish-opportunities.ps1, deploy-*.ps1). So the rule
    #    read as covered while the thing actually typed went straight through.
    #    On 2026-10-19 `.\build\publish.ps1 2>&1 | Select-String ...` showed eight green builds and
    #    was reported as published. It had not published: the share still held the previous build,
    #    six days old, and the filter had eaten the failure. Found only because the next command
    #    happened to read version.txt off the share. Ian: "Why not read the rules FIRST?" - the rule
    #    had been read ten minutes earlier, which is the whole point of putting it in a gate.
    #    PowerShell has no pipefail, so the check is different: capture the output and read
    #    $LASTEXITCODE, which is the only thing that carries the script's real outcome.
    #    TIGHTENED 2026-10-25: the script name had to appear only SOMEWHERE before a pipe, so naming
    #    one as an ARGUMENT tripped it. `Select-String -Path '...\build\publish.ps1' | ForEach-Object`
    #    was denied for reading the file, which teaches the habit of routing around the gate - worse
    #    than the fault it guards. So the name must now stand where a command stands, the same idiom
    #    rules 6 and 7 already use.
    #    CHECKS: publish*/deploy*.ps1 in command position (the start, or after ; & | ( { or a newline,
    #      optionally via &), or passed to -File/-FilePath/-Command, and then piped.
    #    ALLOWS: the same name as an argument to a reader (-Path/-LiteralPath/-Include/-Pattern,
    #      Get-Content, cat, Test-Path, Get-AuthenticodeSignature), and any command that sets
    #      pipefail or reads $LASTEXITCODE.
    #    DOES NOT CATCH: a run through a variable or Invoke-Expression (`$s = '.\publish.ps1'; & $s |
    #      tail`), where the name is nowhere near the pipe; a wrapper not named publish*/deploy*; or a
    #      script that swallows its own failure and exits 0, which no exit-code check can see.
    (re.compile(r"(?is)^(?!.*(?:pipefail|LASTEXITCODE)).*?"
                r"(?:(?:^|[;&|({\n])\s*(?:&\s*)?|-(?:File|FilePath|Command)\s+)"
                r"['\"]?(?:[A-Za-z]:)?[^\s'\";|&]*"
                r"\b(?:publish|deploy)[A-Za-z0-9._-]*\.ps1\b[^|;\n]*\|(?!\|)"),
     "A publish/deploy script piped without reading its exit code: the pipe's result replaces the "
     "script's, so a failed deploy reads as a successful one (2026-10-19: eight green build lines "
     "from publish.ps1 that never reached the share, reported as published). Assign it instead - "
     "`$out = .\\build\\publish.ps1 2>&1` - then check $LASTEXITCODE and print what you need from "
     "$out. Or run it unpiped."),
]

# 5. THE FULL APP SUITE FOR A ONE-AREA CHANGE. 3.5 minutes and the build lock per run. On
#    2026-10-13 it ran twice, 7 minutes, for a change confined to BdReports whose own classes
#    run in 30 s -- with feedback_run_the_targeted_tests_not_the_suite sitting in memory.
#    A `dotnet test` that names the App test project must carry --filter, unless the run is
#    declared deliberate with FULL_SUITE=1 (the pre-publish run).
APP_SUITE = re.compile(r"dotnet\s+test\b[^\n|;&]*(?:Kor\.Operations\.App\.Tests|Kor\.Transmittals\.App\.Tests)")
FILTERED = re.compile(r"--filter\b")
DELIBERATE_FULL = re.compile(r"\bFULL_SUITE=1\b")

# 6. A SQL CALL THAT CAN HANG. On 2026-10-13 three sqlcmd calls over the VPN hung for minutes.
#    sqlcmd's query timeout defaults to 0, which waits forever. A raw sqlcmd must carry -t <seconds>,
#    so a dropped VPN packet becomes an error in seconds instead of a stall.
#    REPLACED 2026-10-15 (Ian: "It causes more harm than good"): this rule used to cap calls at three
#    per ten minutes. The count was never the harm; the hang was. On 2026-10-15 the cap blocked a
#    correct apply-and-verify three times and prevented no hang.
#    CHECKS: sqlcmd in command position (start of a command, after ; & | ( or a newline, or run by
#    `python [-flags]`) without -t. ALLOWS: oq.py / oqwide.py, which pass -l 15 -t 120 themselves.
#    DOES NOT CATCH: a copy of oq.py edited to drop its timeouts; a SqlClient tool with
#    CommandTimeout = 0; a query that is merely slow, not hung, inside its 120 seconds.
#    NARROWED 12 Nov (FW-RESET-2 M8): judged 2 of 5 right (6292, 6648: a real -Q query with no -t) and 3 of 5 wrong -
#    4941 and 6288 were the WORD sqlcmd inside a grep pattern after '\|' (read as a pipe), 6288/6380 a `sqlcmd -?`
#    (help, no query). So the call is looked for with quoted strings emptied (a quoted string that is itself a path
#    to sqlcmd is kept: "C:/Program Files/sqlcmd" is still a call), and a call whose only argument is -? or /? passes.
#    DOES NOT CATCH, added: sqlcmd run as a quoted argument of another shell (pwsh -Command "sqlcmd ...").
#    KNOWN FALSE REFUSAL (seen 12 Nov on this commit's own message): a heredoc body - a `git commit -F -` message -
#    with sqlcmd at a line start or after '(' is read as a call. Write the message to a file and use -F <file>.
RAW_SQLCMD_CALL = re.compile(r'(?im)(?:^|[;&|(])\s*(?:&\s*)?(?:"[^"\n]*[/\\]|[^\s"]*[/\\])?sqlcmd(?:\.exe)?"?(?=\s|$)')
SQLCMD_QUERY_TIMEOUT = re.compile(r'(?<!\S)-t\s*\d+')
SQLCMD_PATH = re.compile(r'(?i)^[^|"\n]*?(?:^|[/\\])sqlcmd(?:\.exe)?$')
SQLCMD_HELP = re.compile(r'\s+[-/]\?(?=\s|$|[;&|)])')


def sqlcmd_without_timeout(cmd):
    """Rule 6: a sqlcmd call (not a mention inside a quoted string, not `sqlcmd -?`) in a command with no -t."""
    if SQLCMD_QUERY_TIMEOUT.search(cmd):
        return False
    text = blank_quotes(cmd, keep=SQLCMD_PATH)
    return any(not SQLCMD_HELP.match(text, m.end()) for m in RAW_SQLCMD_CALL.finditer(text))

# 7. GOING SILENT. "Say the duration before any step over a minute" was promised on
#    2026-10-13 and broken the same afternoon: a 3.5-minute suite ran with no word, then a
#    40-minute stretch. The one line Ian always sees is the command's description. A command
#    that asks for more than two minutes must open that line with its expected duration
#    ("~3 min: ..."), or it does not run.
LONG_TIMEOUT_MS = 120_000
# 12 Nov (FW-RESET M8): a range ("~1-2 min:", "~5-10 min") is a duration too - 6696 was refused for it.
DURATION_FIRST = re.compile(r"(?i)^\s*~?\s*\d+(?:\s*(?:-|–|to)\s*\d+)?\s*(?:s|sec|secs|seconds|m|min|mins|minutes|h|hr|hours)\b")

# 8. A SLOW RUN WHOSE INPUTS ARE NOT THERE. On 2026-10-19 three runs were wasted in one afternoon on inputs a
#    second's check would have shown missing: the five stick-file PDFs gone from the local mirror (empty since
#    10-07), then a sixth source moved on the share - the six-set gate ran 7 m 22 s and refused. Ian: "STOP DOING
#    ANYTHING WITHOUT FUCKING CHECKING. THAT'S A CARDINAL FUCKING RULE."
#    CHECKS: a `dotnet test` of the Core test project that is not filtered to the fast set (Speed!=Slow) is denied
#    unless every \\Kor-fs01 source SixSetsBuildAsBankedTests names exists and the stick-file mirror holds its five
#    PDFs. The unfiltered App EngineeringTools.Tests suite is denied outright: it hangs headless (297 s, 10-19).
#    Since 9 Nov (Operations 5da3715c, board row 1985) the Core tests live in KOR.Drafter, so the source list is
#    read from KOR.Drafter's SixSetsBuildAsBankedTests.cs; if that file cannot be read the run is denied (fail closed).
#    DOES NOT CATCH: a source that exists but is a different file (the gate's own hash check does that); inputs of
#    other slow tests (LiveProjects share paths); a run started from inside the test project's folder with no
#    project path in the command. It ASSUMES KOR.Drafter is a sibling folder of this Operations checkout: a
#    KOR.Drafter cloned anywhere else reads as a missing file and every unfiltered Core run is denied.
CORE_TESTS = re.compile(r"dotnet\s+test\b[^\n|;&]*EngineeringTools\.Core\.Tests")
FAST_ONLY = re.compile(r"Speed!=Slow")
ENGINEERING_APP_TESTS = re.compile(r"dotnet\s+test\b[^\n|;&]*(?:EngineeringTools\.Tests\.csproj|App[\\/]EngineeringTools\.Tests)")
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIX_SETS = os.path.join(os.path.dirname(REPO), "KOR.Drafter", "Kor.Operations.EngineeringTools.Core.Tests", "Intake",
                        "SixSetsBuildAsBankedTests.cs")
STICK_JOBS = ("31130-01", "31138-01", "31168-01", "31065-01", "31202-01")


def mirror_copy_of(remote):
    """DrawingMirror.LocalCopyOf: %TEMP%\\kor-drawings\\<SHA1(lower(dir))[:16] hex>\\<file name>. No share access."""
    directory, name = remote.rsplit("\\", 1)
    key = hashlib.sha1(directory.lower().encode("utf-8")).hexdigest().upper()[:16]
    root = os.environ.get("KOR_GUARD_MIRROR_ROOT") or os.path.join(tempfile.gettempdir(), "kor-drawings")
    return os.path.join(root, key, name)


def missing_slow_inputs():
    """Every input the slow gates read that is not where they will look - empty when all are there."""
    missing = []
    # the gate's LOCAL mirror copies (DrawingMirror.LocalCopyOf), never the share: 2026-10-19 this check itself
    # statted six files across the VPN before every run
    try:
        text = open(SIX_SETS, encoding="utf-8").read()
        for path in re.findall(r'@"(\\\\Kor-fs01[^"]+\.pdf)"', text):
            local = mirror_copy_of(path)
            if not os.path.exists(local):
                missing.append(local)
    except OSError:
        missing.append(SIX_SETS)
    mirror = os.environ.get("KOR_GUARD_STICKFILES") or os.path.join(
        os.environ.get("LOCALAPPDATA", ""), "Temp", "kor-drawings", "stickfiles")
    for job in STICK_JOBS:
        if not os.path.exists(os.path.join(mirror, job + ".pdf")):
            missing.append(os.path.join(mirror, job + ".pdf"))
    return missing



# W8 (Operations docs/codex/CODEX-CLAUDE-WEAKNESSES-RESPONSE.md:172; Framework lanes/2026-11-19/PLAN-W1-W9-CHECKS.md
# check 2), 19 Nov: a fresh coordinator started in C:\VIsual Studio Projects\KOR.Drafter-portmerge while the previous
# coordinator's PdfToSafe test (pid 11508, testhost 13784) still ran there; that afternoon's rebuild had already failed on
# a DLL lock. The old check knew ONE folder (the Operations main checkout, by a hard-coded path), so a host in any other
# repo or any git worktree was invisible. Now: the folder the build is FOR (the project path it names, else a leading
# cd "X", else the hook's cwd) is walked up to its git root (a .git folder, or a .git FILE - a worktree), and the build is
# refused while a testhost.exe, or a dotnet build/test/publish, whose command line names that root is alive - any repo,
# any worktree, this session's own run included (it holds the same lock). Also KOR.Drafter's test slot file.
# CHECKS: process command lines (Win32_Process), the slot file. DOES NOT CHECK: an MSBuild node-reuse server (no path on
# its command line); a build run by path through a variable; edits by another session in the same tree (no lock to see).
# SAME-CLASS MISS: a second session EDITING files in a tree where a first one builds - nothing here sees it.
BUSY_NAMES = ("testhost.exe", "dotnet.exe")
BUSY_DOTNET = re.compile(r"(?i)\b(?:test|build|publish|vstest\.console\.dll)\b")
LEADING_CD = re.compile(r"""(?i)^\s*(?:cd|Set-Location|sl|pushd)\s+(?:-Path\s+)?(?:"([^"]+)"|'([^']+)'|([^\s;&]+))\s*(?:&&|;)""")
BUILD_PATH = re.compile(r"""(?i)dotnet(?:\.exe)?\s+(?:build|test|publish)\s+(?:"([^"]+)"|'([^']+)'|([^\s;&|-][^\s;&|]*))""")
DRAFTER_SLOT = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Temp", "kor-drafter-testslot.txt")


def _win(p):
    """A Git Bash /c/x path as C:/x; anything else as given."""
    m = re.match(r"^/([a-zA-Z])/(.*)$", p)
    return f"{m.group(1).upper()}:/{m.group(2)}" if m else p


def git_root(path):
    """The git root above path (a .git folder, or the .git FILE of a worktree), lower-case with backslashes; None if none."""
    try:
        d = os.path.abspath(_win(path))
    except Exception:
        return None
    if os.path.isfile(d):
        d = os.path.dirname(d)
    while True:
        if os.path.exists(os.path.join(d, ".git")):
            return d.lower().replace("/", "\\").rstrip("\\")
        up = os.path.dirname(d)
        if up == d:
            return None
        d = up


def build_root(cmd, cwd):
    """The git root of the tree this build is FOR: the project path it names, else a leading cd, else cwd."""
    base = cwd or os.getcwd()
    cd = LEADING_CD.match(cmd)
    if cd:
        base = os.path.join(base, _win(next(g for g in cd.groups() if g)))
    m = BUILD_PATH.search(cmd)
    if m:
        p = _win(next(g for g in m.groups() if g))
        return git_root(p if os.path.isabs(p) else os.path.join(base, p)) or git_root(base)
    return git_root(base)


def processes():
    """[(pid, name, command line)] of testhost.exe and dotnet.exe. KOR_GUARD_PROCS names a JSON file of the same shape
    (tests); otherwise Win32_Process by CIM - by command line, never by name alone."""
    fake = os.environ.get("KOR_GUARD_PROCS")
    if fake:
        with open(fake, encoding="utf-8") as f:
            return [tuple(x) for x in json.load(f)]
    # JSON, not a tab-joined line: the first version used '{0}`t{1}' in SINGLE quotes, where PowerShell never expands `t,
    # so no row ever split and the guard allowed every build - its fake-process tests passed, the live probe caught it
    try:
        out = subprocess.run(
            ["powershell", "-NoProfile", "-NonInteractive", "-Command",
             "@(Get-CimInstance Win32_Process -Filter \"Name='testhost.exe' OR Name='dotnet.exe'\" | "
             "Select-Object ProcessId,Name,CommandLine) | ConvertTo-Json -Compress -Depth 2"],
            capture_output=True, text=True, timeout=15).stdout.strip()
    except Exception:
        return []
    return parse_procs(out)


def parse_procs(out):
    """[(pid, name, command line)] from Get-CimInstance ... | ConvertTo-Json: an array, or (one process) a lone object."""
    try:
        data = json.loads(out) if out else []
    except ValueError:
        return []
    if isinstance(data, dict):   # ConvertTo-Json writes a lone object, not a one-item array, on Windows PowerShell 5.1
        data = [data]
    return [(str(p.get("ProcessId")), p.get("Name") or "", p.get("CommandLine") or "") for p in data if isinstance(p, dict)]


def busy_in(root):
    """Why the tree at root is busy (a list of lines), or [] - a testhost or a dotnet build/test naming root, or the
    KOR.Drafter slot file for a KOR.Drafter tree."""
    if not root:
        return []
    why = []
    for pid, name, line in processes():
        text = (line or "").lower().replace("/", "\\")
        if name.lower() not in BUSY_NAMES or (root + "\\") not in text:
            continue
        # only the tree's OWN project folders hold its locks: the first folder under the root must hold a .csproj
        # (review 2305: a KOR.Drafter\work\_suite\<sha> copy's testhost is inside the tree and holds none of them -
        # record #132034, an hour of false refusals the old one-folder check had already fixed once)
        seg = re.match(r'[^\\"\s]+', text.split(root + "\\", 1)[1])   # the folder name ends at \ or a quote or a blank
        first = seg.group(0) if seg else ""
        try:
            own = any(f.lower().endswith(".csproj") for f in os.listdir(os.path.join(root, first)))
        except OSError:
            own = False
        if not own:
            continue
        if name.lower() == "dotnet.exe" and ("/nodemode" in text or not BUSY_DOTNET.search(text)):
            continue
        why.append(f"pid {pid} {name}: {line[:160]}")
    slot = os.environ.get("KOR_GUARD_SLOT") or DRAFTER_SLOT
    if os.path.basename(root) == "kor.drafter" and os.path.isfile(slot):
        try:
            why.append("KOR.Drafter test slot held: " + open(slot, encoding="utf-8", errors="replace").read().strip()[:160])
        except OSError:
            pass
    return why


DOTNET_BUILD_AT_COMMAND = re.compile(r"(?is)(?:^|[;&|(\n])\s*(?:(?:timeout(?:\s+-\S+)*\s+\S+|time|env|nice|command|[A-Za-z_]\w*=\S*)\s+)*"
                                     r"(?:\S*[/\\])?dotnet(?:\.exe)?\s+(?:build|test|publish)\b")


PAYLOAD = {}


def deny(reason):
    try:   # F11 (Framework row 1159): every refusal is a row; logging can never change what this guard does
        sys.path.append("C:/VIsual Studio Projects/Framework/tools"); import guard_log  # noqa: E702
        guard_log.record("bash", reason.split(". ")[0][:200], (PAYLOAD.get("tool_input") or {}).get("command"), None, payload=PAYLOAD)
    except Exception:
        pass
    json.dump({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": reason,
    }}, sys.stdout)
    sys.exit(0)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return
    PAYLOAD.update(payload if isinstance(payload, dict) else {})
    tool_input = payload.get("tool_input") or {}
    cmd = tool_input.get("command") or ""
    if not cmd:
        return

    # Rule 7 since 19 Nov (Ian "Go", Framework plan step 3): the line is FILLED, not refused - 14 days to 19 Nov it refused
    # ~880 commands (judged wrong 4) and each cost a retry. The description gets "up to N min: " (the timeout, a ceiling,
    # not a guess) after every other rule has had its say; a deny below still wins.
    timeout = tool_input.get("timeout") or 0
    fill = None
    if isinstance(timeout, (int, float)) and timeout > LONG_TIMEOUT_MS \
            and not DURATION_FIRST.search(tool_input.get("description") or ""):
        fill = dict(tool_input, description=f"up to {-(-int(timeout) // 60000)} min: {tool_input.get('description') or 'command'}")
    _rest(cmd)   # every other rule; a deny there exits before the fill is written
    if fill:   # updatedInput replaces the WHOLE input, so every field is carried; no permissionDecision: normal flow
        json.dump({"hookSpecificOutput": {"hookEventName": "PreToolUse", "updatedInput": fill}}, sys.stdout)


def _rest(cmd):

    for pattern, reason in RULES:
        if pattern.search(cmd):
            deny(reason)

    # CLAUDE.md: "Do not start one and then keep editing" -- it holds the build output lock (fired 2026-08-25, a
    # ten-minute wait). W8, 19 Nov: only the tree the build is FOR, and only a run from that tree's OWN project folders
    # (busy_in): a build of another repo (2026-09-19, JoeBrain held 20 min) or a suite copy inside the tree
    # (10 Nov, KOR.Drafter\work\_suite, an hour) takes none of these locks.
    if DOTNET_BUILD_AT_COMMAND.search(blank_quotes(cmd)):
        root = build_root(cmd, PAYLOAD.get("cwd"))
        why = busy_in(root)
        if why:
            deny(f"W8: the tree you are building ({root}) is busy - a build or test from it is still running and holds its "
                 "output lock (this would fail with MSB3027, or test the wrong binaries). It may be ANOTHER session's: do not "
                 "kill it; ask that session (ListAgents / SendMessage) or wait. " + " | ".join(why))

    if APP_SUITE.search(cmd) and not FILTERED.search(cmd) and not DELIBERATE_FULL.search(cmd):
        deny("The full App suite without --filter: 3.5 minutes and the build lock, for a change that "
             "touches one area (it ran twice on 2026-10-13, 7 minutes, for BdReports alone). Run the "
             "classes you changed: --filter \"FullyQualifiedName~<Namespace or class>\". The one "
             "deliberate full run, before a publish, is prefixed FULL_SUITE=1.")

    if ENGINEERING_APP_TESTS.search(cmd) and not FILTERED.search(cmd):
        deny("The unfiltered App EngineeringTools.Tests suite hangs headless (297 s on 2026-10-19, testhost alive, no "
             "progress). Run it filtered: --filter \"FullyQualifiedName~PdfToSafe\" --blame-hang-timeout 120s.")

    if CORE_TESTS.search(cmd) and not FAST_ONLY.search(cmd):
        missing = missing_slow_inputs()
        if missing:
            deny("A slow Core test run whose inputs are not there - it would run for minutes and then refuse. "
                 "Missing:\n  " + "\n  ".join(missing) + "\nFind where each went, mend the path in "
                 "SixSetsBuildAsBankedTests.cs or restore the mirror, then run. (Rule 8, 2026-10-19.)")

    if sqlcmd_without_timeout(cmd):
        deny("sqlcmd without a query timeout: its default is 0, which waits forever, and three calls hung "
             "for minutes over the VPN on 2026-10-13. Add -t <seconds> (and -l 15 for the login), or use "
             "oq.py, which sets both.")


if __name__ == "__main__":
    main()
