Rule 3b is narrowed and retained. **222 of 222 cases pass. All 119 logged commands were recovered and replayed: 114 change from deny to allow, five remain denied.** The separately cited 4970 changes from allow to deny, restoring the literal rule-7 requirement that the November 12 implementation had lost.

Work is on `bash-heredoc-narrow`. The starting HEAD was `8c9f2898`, two brief-only commits after the specified `1038597c`; the guard and original tests match that specified baseline. Commit A is `02e78c68` (`bash heredoc narrow A: cases (codex)`), containing only test additions. A2 is `a2294314` (`bash heredoc narrow A2: cover descriptor duplication (codex)`), also tests only. Neither was amended. This response accompanies B. Production changes are confined to `HeredocEscape` and rule 3b's explanatory comment; an AST comparison confirms all executable code outside that class is unchanged.

The evidence supports cause **(a)** for direct verbatim consumers. The old matcher scanned every body without identifying its consumer. Quoting the delimiter protects the body from Bash expansion; `cat`, `tee`, and `git commit -F -` do not then apply Python string decoding. Quoting does not protect source actually consumed by Python. It also does not prove an arbitrary pipeline or later interpreter harmless.

Cause **(b)** is substantially correct: decoding an escape is often the intended operation. Git separators `\x00`/`\x01` (8264), JSON test characters (8595), a BOM (7091), and Unicode separators (8921) are concrete examples. The existing `\03` deny and the requested unquoted-double-backslash denies nevertheless impose conservative policy checks. They cannot simultaneously be advertised as proof that the author's intent was wrong. Intended octal and backspace data can still be refused.

Proposal **(c)** needs both narrower semantics and better parsing. Matching every unusual escape in a non-raw literal still rejects deliberate hexadecimal/Unicode data. Matching every unquoted delimiter would reject Bash's unchanged single `\P`; only the required doubled-backslash condition is retained there. Conversely, matching only *odd* escapes loses 4970: its C# regex escapes were manually doubled. Python decodes those pairs correctly, but the command violates rule 7's explicit non-raw-string prohibition. This implementation refuses that recognizable C# verbatim-regex form even when doubled. It does not claim 4970's regex was actually corrupted.

The old raw-literal regex also mishandled raw f-strings and escaped quotes. More subtly, it found apparent new heredocs inside a body: 8924's C# `<< P` shifts caused a scan into subsequent shell text. Bodies are now consumed once, associated with their own command, and Python source is lexed with `tokenize`. Raw prefixes, triple strings, escaped quotes, comments, and Python 3.12 f-string tokens therefore retain their actual lexical boundaries. The shell reader handles quoted, partially quoted, and backslash-quoted delimiters, tab stripping, multiple bodies, common Python options/wrappers, and descriptor duplication. Python script, `-c`, and `-m` stdin is treated as data, not presumed to be source.

**`cat > x.py` passes when its delimiter is quoted**, including a command followed by `python x.py`. It writes exactly the source supplied; inspecting later execution would require file tracking and analysis beyond this rule. That script may still contain a real bug. The same boundary applies to `.cs` and `.ps1`: filename extensions do not change how the current heredoc transports bytes. Tests explicitly establish this limit, rather than treating a source-file write as proof of safe future execution.

The committed additions total **26 MUST REFUSE / 47 MUST PASS**: 22 full real commands (one refuse / 21 pass), 48 original constructed cases (23 refuse / 25 pass), and three A2 constructed cases (two refuse / one pass). No transcript command was shortened or substituted. All 22 fixture strings compare exactly with the recovered commands and their UTF-8 SHA-256 hashes. Existing `CASES` and all five original case/helper functions (including `verdict`) remain unchanged; the test-file changes are additions only.

Each real case below comes from the assistant's Bash `tool_use.input.command` at the cited local transcript line and UTC timestamp. `Session / file` identifies the parent UUID and either its main JSONL or its `subagents/agent-*.jsonl`. Complete absolute paths, command hashes, and timestamp offsets are retained in the local recovery manifest; hashes also appear beside every real committed case.

| ID | Expected | Session / file / line | UTC tool-call timestamp |
| --- | --- | --- | --- |
| 4970 | deny | `1119f13b-af83-4549-8a67-24e4289569d5:L93` | 2026-11-10T07:17:13.844Z |
| 8264 | allow | `a0ed29c9-a893-4264-b14d-14cca0d980b5:L2374` | 2026-11-19T21:30:11.863Z |
| 8591 | allow | `acc84549-5143-46b1-ad9c-630e3f716c8d:L426` | 2026-11-20T04:06:51.921Z |
| 8595 | allow | `cd2499ed-046a-4e0b-9392-a3e8c95f0137:L459` | 2026-11-20T04:13:06.672Z |
| 8609 | allow | `c2ad5988-6082-4890-b0bb-2a69792f929d:L1410` | 2026-11-20T04:57:36.366Z |
| 8615 | allow | `3920cc62-71f9-45e4-9c3a-70b447e9aada:L1682` | 2026-11-20T05:09:39.281Z |
| 8686 | allow | `ac80da06-010b-48fe-b321-e576eb44a116:L2504` | 2026-11-20T05:42:07.277Z |
| 8687 | allow | `3920cc62-71f9-45e4-9c3a-70b447e9aada:L1990` | 2026-11-20T05:49:00.851Z |
| 8688 | allow | `51d2fd14-eabc-404f-a29f-f18a4e6f4969:L333` | 2026-11-20T05:49:14.817Z |
| 8689 | allow | `24484c67-7a4f-42ec-a905-a48d47e32dbe:L329` | 2026-11-20T05:52:05.305Z |
| 8708 | allow | `51d2fd14-eabc-404f-a29f-f18a4e6f4969:L848` | 2026-11-20T06:11:19.830Z |
| 8801 | allow | `5c5a10b5-ce8a-4fb3-9cbc-8938fb0393c3:L181` | 2026-11-20T06:43:21.764Z |
| 8891 | allow | `5750502b-789a-4668-9619-607de3348260:L533` | 2026-11-20T07:38:02.732Z |
| 8909 | allow | `d0258715-45e4-41d3-9008-2ba89942b35f:L1001` | 2026-11-20T08:55:17.975Z |
| 8911 | allow | `d0258715-45e4-41d3-9008-2ba89942b35f:L1370` | 2026-11-20T09:21:31.016Z |
| 8920 | allow | `780c3992-0bf1-4ce7-9bea-f1d1c32ed2c7:L1359` | 2026-11-20T10:45:03.086Z |
| 8921 | allow | `2d0ec830-6eee-449a-8361-af26bbffa277:L1148` | 2026-11-20T10:47:36.101Z |
| 8922 | allow | `780c3992-0bf1-4ce7-9bea-f1d1c32ed2c7:L1877` | 2026-11-20T11:06:06.944Z |
| 8924 | allow | `c779e271-25a7-4297-807b-4090b8483dc3:L339` | 2026-11-20T11:40:27.566Z |
| 8929 | allow | `780c3992-0bf1-4ce7-9bea-f1d1c32ed2c7:L2291` | 2026-11-20T12:11:44.141Z |
| 8961 | allow | `e7287342-6067-4ac2-818a-e923a9744797:L1162` | 2026-11-20T18:29:02.944Z |
| 8978 | allow | `ee8754e5-8d40-4410-94b9-004d09152ab6:L2158` | 2026-11-20T20:03:47.407Z |

The following are Codex-constructed commands in `HEREDOC_BOUNDARIES`. Each label identifies its exact committed command. A2's final three cases were added after inspection showed that the initial implementation split `2>&1` at `&`. Both Python cases were observed to allow incorrectly before that implementation was corrected. The quoted-cat neighbor already passed. The original A commit was not changed.

| No. | Case | Expected | Source |
| --- | --- | --- | --- |
| 1 | C# word boundary through Python | deny | Codex construction; A |
| 2 | C# digits through Python | deny | Codex construction; A |
| 3 | C# whitespace through Python | deny | Codex construction; A |
| 4 | C# manually doubled regex through Python (4970 class) | deny | Codex construction; A |
| 5 | Windows Unicode-looking path | deny | Codex construction; A |
| 6 | Windows octal-looking path | deny | Codex construction; A |
| 7 | Windows vertical-tab path | deny | Codex construction; A |
| 8 | Windows null path | deny | Codex construction; A |
| 9 | UNC path through non-raw Python | deny | Codex construction; A |
| 10 | Relative Windows path | deny | Codex construction; A |
| 11 | Unquoted cat UNC | deny | Codex construction; A |
| 12 | Unquoted tee doubled slash | deny | Codex construction; A |
| 13 | Unquoted git message UNC | deny | Codex construction; A |
| 14 | Unquoted Python raw cannot protect against Bash | deny | Codex construction; A |
| 15 | Quoted cat Windows paths and regexes | allow | Codex construction; A |
| 16 | Quoted tee Windows paths | allow | Codex construction; A |
| 17 | Quoted git message regex | allow | Codex construction; A |
| 18 | Quoted Python-file write has no Python consumer | allow | Codex construction; A |
| 19 | Later file execution is outside this rule | allow | Codex construction; A |
| 20 | Quoted C#-file write | allow | Codex construction; A |
| 21 | Quoted PowerShell-file write | allow | Codex construction; A |
| 22 | Raw Python paths, bytes, and regexes | allow | Codex construction; A |
| 23 | Raw f-string including escaped quotes | allow | Codex construction; A |
| 24 | Intentional formatting and Unicode escapes | allow | Codex construction; A |
| 25 | Intentional JSON data with quotes and slash | allow | Codex construction; A |
| 26 | Comments do not interpret Python escapes | allow | Codex construction; A |
| 27 | Raw triple quoted string with internal quotes | allow | Codex construction; A |
| 28 | Non-raw triple quoted string | deny | Codex construction; A |
| 29 | Non-raw f-string | deny | Codex construction; A |
| 30 | Raw literal does not mask following non-raw literal | deny | Codex construction; A |
| 31 | Python then cat use their own bodies | allow | Codex construction; A |
| 32 | Cat then Python cannot exempt unsafe Python | deny | Codex construction; A |
| 33 | Bit shifts inside Python are not nested heredocs | allow | Codex construction; A |
| 34 | Heredoc spelling in a quoted argument is not a redirect | allow | Codex construction; A |
| 35 | Backslash-quoted delimiter | allow | Codex construction; A |
| 36 | Partly quoted delimiter | allow | Codex construction; A |
| 37 | Tab-stripping quoted heredoc | allow | Codex construction; A |
| 38 | Tab-stripping unquoted heredoc | deny | Codex construction; A |
| 39 | A script's stdin is data, not Python source | allow | Codex construction; A |
| 40 | Python -c reads input as data | allow | Codex construction; A |
| 41 | Python -m reads input as data | allow | Codex construction; A |
| 42 | Python implicit stdin source | deny | Codex construction; A |
| 43 | Python executable path and wrapper | deny | Codex construction; A |
| 44 | Windows Python launcher | deny | Codex construction; A |
| 45 | Quoted non-Python interpreter | allow | Codex construction; A |
| 46 | Unquoted single slash to cat stays literal in Bash | allow | Codex construction; A |
| 47 | Two same-line heredocs have separate consumers | deny | Codex construction; A |
| 48 | Two same-line safe heredocs | allow | Codex construction; A |
| 49 | Descriptor duplication before Python stdin marker | deny | Codex construction; A2 redirection review |
| 50 | Descriptor duplication on wrapped Python | deny | Codex construction; A2 redirection review |
| 51 | Descriptor duplication does not make cat Python | allow | Codex construction; A2 redirection review |

Validation used Python 3.12.10 and only the Python guard suite; no solution or .NET build was needed.

| Check | Result |
|---|---|
| Untouched baseline: `python -I tools/claude_bash_guard_test.py` | 149/149, exit 0 |
| A corpus, before implementation: same command | 174/219, exit 1; all 149 original checks pass |
| A's failures | All 21 real MUST PASS cases, 22 constructed MUST PASS cases, 4970, and the constructed manually doubled C# regex |
| First implementation, original A corpus | 219/219, exit 0 |
| A2 against that implementation | Both new Python denies incorrectly allow; cat allow passes |
| Final corpus against frozen `1038597c` source | 176/222, exit 1; 46 failures, all additions |
| Final: `python -I tools/claude_bash_guard_test.py` | 222/222, exit 0 |
| Original executable code outside `HeredocEscape` | Identical AST, compared as UTF-8 |
| Real committed fixture text and SHA-256 | 22/22 match recovered full commands |
| Safe Bash byte probes | Single backslashes preserved, unquoted pairs collapsed, quoted pairs preserved |

The final-corpus baseline check uses a local copy of the original guard source with its original `__file__`, redirecting only the test harness's guard subprocess invocation. The worktree guard was not swapped. Thus preflight paths, payloads, and environment retain the original harness behavior. The ordinary before-B command was also run directly as required, before any implementation edit.

Recovery queried **only `KorStandards.oversight.GuardRefusal`**, through `python -I tools/SqlQuery/oq.py -q ...`. The population is `Guard = 'bash'`, `[Rule] LIKE 'A backslash escape inside a heredoc%'`, and `RefusedAtUtc >= '2026-11-12T15:45:00-07:00' AND RefusedAtUtc < '2026-11-21T11:48:00-07:00'`. The historical control was selected separately by `Id = 4970`. The result is exactly 119 + 1 rows. These timestamps actually carry a `-07:00` offset despite the column's name; recovery compared timezone-aware instants, not a second manually applied seven-hour adjustment.

**119 of 119** population commands were recovered, plus 4970. Matching used the entire stored `Matched` prefix, the session's main/subagent transcripts, and the nearest timestamp. The matched tool calls precede logging by 0.354260–20.476322 seconds. Across the 120 recovered commands, 100 came from ilalonde's transcript root and 20 from kor-claude's; 48 were in subagent files. Recovery enumerated only the two specified `.claude/projects` roots. No holdout directory was opened.

Replay fed each full command as a string in the same stdin payload shape and empty-process/absent-slot environment as `verdict()`. It did **not execute any recovered shell command**. Before B, the population replay was 119 deny / zero allow. After B it is five deny / 114 allow; the descriptor correction leaves these results unchanged. The historical 4970 control is outside that denominator: allow before, deny after.

Every population row is listed below, including the reason for each changed verdict. An allow means this rule has no identified escape fault to protect against at that heredoc transport step; it is not approval of unrelated actions named in the command or of a generated program's later behavior. The remaining denies say exactly what Bash would change, including where that change appears intentional.

| ID | Transcript source | Before → after | Reason / change |
| --- | --- | --- | --- |
| 6868 | `0cdfbece:L741` | deny → allow | Quoted cat emits `finding.txt` verbatim; no Bash/Python escape decoding at this step. |
| 6914 | `1e2750b2:L1954` | deny → allow | Quoted cat emits `$s` verbatim; no Bash/Python escape decoding at this step. |
| 6959 | `1e2750b2:L3804` | deny → allow | Quoted cat emits `$f` verbatim; no Bash/Python escape decoding at this step. |
| 6985 | `1e2750b2/agent-a813a149e6617aaca:L30` | deny → allow | Quoted cat emits `s.py` verbatim; no Bash/Python escape decoding at this step. |
| 6990 | `1e2750b2/agent-ac4ed975b77d96a48:L262` | deny → allow | Python regexes are r/rf literals, including escaped quotes; no escape decoding of regex text. |
| 7005 | `1e2750b2/agent-ad3195372f088ec7b:L647` | deny → allow | Quoted cat emits `state.md` verbatim; no Bash/Python escape decoding at this step. |
| 7018 | `2c92fbae/agent-a2e10f01425730ae3:L199` | deny → allow | Quoted cat emits `INPUTS-v5.env` verbatim; no Bash/Python escape decoding at this step. |
| 7069 | `2c92fbae/agent-a3788c2bff56a0245:L530` | deny → allow | Python intentionally counts the doubled-slash C: root and uses \u2002 in heading searches. |
| 7077 | `2c92fbae:L4527` | deny → allow | Quoted cat emits `state-0551.md` verbatim; no Bash/Python escape decoding at this step. |
| 7085 | `2c92fbae/agent-aba74b7fad1d86464:L586` | deny → allow | Quoted cat emits `STATE.md` verbatim; no Bash/Python escape decoding at this step. |
| 7091 | `2c92fbae/agent-aba74b7fad1d86464:L956` | deny → allow | Python paths are raw; \xef\xbb\xbf is an intentional UTF-8 BOM, with newline/quote formatting. |
| 7093 | `2c92fbae/agent-aba74b7fad1d86464:L1035` | deny → allow | Quoted git commit message; backslashes are message text, with no Bash/Python escape decoding. |
| 7159 | `1e2750b2/agent-a7475aedcea115d73:L171` | deny → allow | Quoted cat emits `ps210.ps1` verbatim; no Bash/Python escape decoding at this step. |
| 7173 | `1e2750b2/agent-a2ffd1eb93cdec90f:L779` | deny → allow | Python search regexes are raw, including the escaped quote; only intended newline replacement elsewhere. |
| 7182 | `1e2750b2/agent-a40e9a360fc23be94:L188` | deny → allow | Python regexes are raw; \x27/\x22 deliberately represent quote characters. |
| 7186 | `1e2750b2/agent-a1486f41a67faff90:L619` | deny → allow | Quoted cat emits `ps210.ps1` verbatim; no Bash/Python escape decoding at this step. |
| 7198 | `1e2750b2/agent-a613b6fa58c4255dd:L207` | deny → allow | Unquoted cat preserves the path’s single backslashes; \$ deliberately protects variable names in the emitted env file. |
| 7204 | `1e2750b2/agent-a1486f41a67faff90:L982` | deny → allow | Python re patterns use r/rf, so their regex escapes remain literal. |
| 7238 | `2c92fbae/agent-adc6f7cea93eb0f0a:L678` | deny → allow | Quoted cat emits `a24head.txt` verbatim; no Bash/Python escape decoding at this step. |
| 7242 | `2c92fbae/agent-a666b97bd41c48d85:L103` | deny → allow | Quoted cat emits `refl.ps1` verbatim; no Bash/Python escape decoding at this step. |
| 7244 | `2c92fbae/agent-a2340db7e36d8ac12:L271` | deny → allow | Quoted cat emits `$S` verbatim; no Bash/Python escape decoding at this step. |
| 7255 | `2c92fbae/agent-ae2d18e9753e58016:L203` | deny → allow | Quoted cat emits `Kor.Operations.EngineeringTools.Pdf.MuPdf.csproj` verbatim; no Bash/Python escape decoding at this step. |
| 7261 | `2c92fbae/agent-adc6f7cea93eb0f0a:L1669` | deny → allow | Quoted git commit message; backslashes are message text, with no Bash/Python escape decoding. |
| 7268 | `2c92fbae:L8911` | deny → allow | Quoted cat emits `watch.sh` verbatim; no Bash/Python escape decoding at this step. |
| 7296 | `2c92fbae/agent-ad941e690f149294b:L360` | deny → allow | Quoted cat emits `frozen-drafting-snapshots-3b.txt` verbatim; no Bash/Python escape decoding at this step. |
| 7300 | `2c92fbae/agent-a2fe34a3222b7490a:L588` | deny → allow | Quoted cat emits `ArchSchemeParityTests.cs` verbatim; no Bash/Python escape decoding at this step. |
| 7304 | `2c92fbae/agent-a997030971a673136:L483` | deny → allow | Quoted cat emits `$S` verbatim; no Bash/Python escape decoding at this step. |
| 7319 | `2c92fbae/agent-a1b227737c79aef8f:L247` | deny → allow | Quoted cat emits `stated.csv` verbatim; no Bash/Python escape decoding at this step. |
| 7338 | `2c92fbae/agent-a3dca269e718e4938:L474` | deny → allow | Quoted cat emits `mutate.sh` verbatim; no Bash/Python escape decoding at this step. |
| 7358 | `2c92fbae/agent-a37734e9d5f551d6d:L328` | deny → allow | First cat uses intentional \$ quoting; Windows-path prose is in the second, quoted cat body. |
| 7360 | `2c92fbae/agent-a37734e9d5f551d6d:L557` | deny → allow | Quoted cat emits `stated.csv` verbatim; no Bash/Python escape decoding at this step. |
| 7393 | `2db8c79f:L203` | deny → allow | Quoted cat emits `BRIEF-COMMON.md` verbatim; no Bash/Python escape decoding at this step. |
| 7400 | `2c92fbae/agent-a84f801bf80791f82:L828` | deny → allow | Quoted cat emits `mkbodies.py` verbatim; no Bash/Python escape decoding at this step. |
| 7446 | `1e2750b2/agent-a7591801249f9d314:L1132` | deny → allow | Quoted cat emits `stated.csv` verbatim; no Bash/Python escape decoding at this step. |
| 7468 | `1e2750b2/agent-adb5ebb121fe304ce:L210` | deny → allow | Quoted cat emits `brief-refill.txt`, `brief-v8inv.txt` verbatim; no Bash/Python escape decoding at this step. |
| 7479 | `1e2750b2/agent-a23ef0ca051b6d7c3:L197` | deny → allow | Quoted cat emits `aud.sh` verbatim; no Bash/Python escape decoding at this step. |
| 7484 | `1e2750b2/agent-a23ef0ca051b6d7c3:L201` | deny → allow | Quoted cat emits `aud.sh` verbatim; no Bash/Python escape decoding at this step. |
| 7507 | `1e2750b2/agent-aa3023024ebbfd04b:L529` | deny → allow | Quoted cat emits `trial.sh` verbatim; no Bash/Python escape decoding at this step. |
| 7514 | `1e2750b2:L11612` | deny → deny | Bash collapses the literal two-slash UNC prefix before Kor-fs01 to one slash: a real path hazard. |
| 7529 | `2fd5ef67/agent-a8ec76cad1f415d45:L369` | deny → allow | Quoted cat emits `an.sh` verbatim; no Bash/Python escape decoding at this step. |
| 7533 | `2fd5ef67/agent-a4ba378724cca773c:L607` | deny → allow | Unquoted cat paths have single backslashes; escaped dollars/backticks deliberately preserve those characters. |
| 7545 | `2fd5ef67/agent-a8ec76cad1f415d45:L626` | deny → allow | Quoted cat emits `stated.csv` verbatim; no Bash/Python escape decoding at this step. |
| 7570 | `2fd5ef67/agent-a4ba378724cca773c:L1588` | deny → allow | Python body has no unsafe escape; appended rf regex source is in a quoted cat body. |
| 7578 | `2fd5ef67/agent-ac4ef84d6e6abf660:L498` | deny → deny | Bash reduces doubled Windows-path separators to singles. This appears intentional; retained conservative refusal. |
| 7581 | `2fd5ef67/agent-ae79297dccd68a904:L278` | deny → allow | Python search regex is raw, including escaped quotes; newline formatting is intentional. |
| 7584 | `26b20084:L217` | deny → allow | Quoted cat emits `p1.ps1` verbatim; no Bash/Python escape decoding at this step. |
| 7586 | `2fd5ef67/agent-ae79297dccd68a904:L1103` | deny → allow | Python regexes are r/rf literals; other escapes are intentional newlines in JSON/Markdown formatting. |
| 7616 | `d335b274/agent-a58c3e73049033abf:L99` | deny → allow | Python regexes are raw, including escaped double quotes; raw regex boundaries are preserved. |
| 7620 | `7dbf344a:L328` | deny → allow | Quoted cat emits `CODEX-31218-SET-AUDIT-V8-RESPONSE.md` verbatim; no Bash/Python escape decoding at this step. |
| 7622 | `4f85ab91:L984` | deny → allow | Quoted cat emits `state.md` verbatim; no Bash/Python escape decoding at this step. |
| 7632 | `b9b30f49/agent-aacbc58b2778717fa:L123` | deny → allow | Quoted cat emits `crop.py` verbatim; no Bash/Python escape decoding at this step. |
| 7636 | `b9b30f49:L566` | deny → allow | Quoted cat emits stdout verbatim; no Bash/Python escape decoding at this step. |
| 7642 | `bc471b16:L430` | deny → allow | Python intentionally uses \x00 as a missing-value sentinel during a text consistency check. |
| 7647 | `bc471b16/agent-a9ded2d7ba095c07c:L314` | deny → allow | Quoted cat emits `trace.py` verbatim; no Bash/Python escape decoding at this step. |
| 7656 | `bc471b16:L1418` | deny → allow | Quoted cat emits `FIX-3A.md` verbatim; no Bash/Python escape decoding at this step. |
| 7659 | `bc471b16/agent-a1fb848d09838e485:L32` | deny → allow | Quoted cat emits `scan.py` verbatim; no Bash/Python escape decoding at this step. |
| 7665 | `17bc90f5:L1735` | deny → allow | Python source/output paths are r/rf literals; no path-escape decoding. |
| 7669 | `6069cecd:L195` | deny → allow | Quoted cat emits `rule.py` verbatim; no Bash/Python escape decoding at this step. |
| 7671 | `d8469274:L863` | deny → allow | Quoted cat emits `strip.py` verbatim; no Bash/Python escape decoding at this step. |
| 7699 | `92f88338:L935` | deny → allow | Quoted cat emits `CODEX-01819-PACKAGE-AUDIT-V10-RESPONSE.md` verbatim; no Bash/Python escape decoding at this step. |
| 7717 | `ba836f7e:L127` | deny → allow | Quoted cat emits `COMMON.md` verbatim; no Bash/Python escape decoding at this step. |
| 7718 | `ba836f7e/agent-a5fd6df06bbf9cdce:L37` | deny → allow | Quoted cat emits `pg.py` verbatim; no Bash/Python escape decoding at this step. |
| 7721 | `ba836f7e/agent-a801e3c6ba0552651:L197` | deny → allow | Python regexes are raw, including escaped single quotes; redirection only sends output to a log. |
| 7722 | `ba836f7e/agent-ad6883dcb136bbd0d:L147` | deny → allow | Quoted cat emits `xcheck.py` verbatim; no Bash/Python escape decoding at this step. |
| 7723 | `ba836f7e/agent-a0d26a7b4087b1d8a:L157` | deny → allow | Quoted cat emits `pg.py` verbatim; no Bash/Python escape decoding at this step. |
| 7729 | `73849a7a:L838` | deny → allow | Quoted cat emits `CODEX-31218-SET-AUDIT-V9-RESPONSE.md` verbatim; no Bash/Python escape decoding at this step. |
| 7739 | `e03f1405:L411` | deny → allow | Quoted cat emits `LOOK-6b9d2f2e.md` verbatim; no Bash/Python escape decoding at this step. |
| 7742 | `87ddc55f:L1068` | deny → allow | Quoted cat emits `$F` verbatim; no Bash/Python escape decoding at this step. |
| 7775 | `bbcef775:L782` | deny → allow | Quoted cat emits `jm-status.md` verbatim; no Bash/Python escape decoding at this step. |
| 7776 | `bbcef775:L808` | deny → allow | Quoted cat emits `LOOK-LAST.md` verbatim; no Bash/Python escape decoding at this step. |
| 7778 | `1a47c7f8:L637` | deny → allow | Python intentionally splits extracted PDF text on \f and normalizes whitespace with a raw regex. |
| 7793 | `139b7ae2:L998` | deny → allow | Python intentionally searches for existing \x01 corruption and replaces it with a raw path; this repairs the fault. |
| 7807 | `5f8b0977:L638` | deny → allow | Quoted cat emits `STATE.md` verbatim; no Bash/Python escape decoding at this step. |
| 7821 | `bfd3cbae:L1025` | deny → allow | Quoted cat emits `wait-and-send.sh` verbatim; no Bash/Python escape decoding at this step. |
| 7827 | `c0344aee:L1125` | deny → allow | Quoted cat emits `read-log.ps1`, `read-safe-log.ps1` verbatim; no Bash/Python escape decoding at this step. |
| 7830 | `bfd3cbae:L1454` | deny → deny | Bash reduces four UNC-prefix slashes to two, pairs to singles, and \$ to $. Appears intentionally escaped; retained refusal. |
| 7832 | `2f8d47bc:L2107` | deny → deny | Bash reduces four UNC-prefix slashes to two, pairs to singles, and \$ to $. Appears intentionally escaped; retained refusal. |
| 7835 | `5f8b0977:L2101` | deny → allow | Quoted cat emits `STATE.md` verbatim; no Bash/Python escape decoding at this step. |
| 7860 | `2bd3d137:L827` | deny → allow | Quoted cat emits `1-stage.ps1` verbatim; no Bash/Python escape decoding at this step. |
| 7866 | `f1181e13:L179` | deny → allow | Quoted cat emits `one.ps1`, `drive.ps1` verbatim; no Bash/Python escape decoding at this step. |
| 7870 | `f1181e13:L888` | deny → allow | Unquoted cat preserves single path backslashes; \$sf deliberately writes literal $sf in the state note. |
| 7879 | `c6418112:L705` | deny → allow | Quoted cat emits `stop-diag.ps1` verbatim; no Bash/Python escape decoding at this step. |
| 7884 | `17bc90f5:L8264` | deny → allow | Quoted cat emits `fix2prog.ps1` verbatim; no Bash/Python escape decoding at this step. |
| 7900 | `c6418112:L2910` | deny → allow | Quoted cat emits `STATE.md` verbatim; no Bash/Python escape decoding at this step. |
| 7906 | `4f295f71:L1399` | deny → allow | Quoted cat emits `STATE.md` verbatim; no Bash/Python escape decoding at this step. |
| 7911 | `cc3ea774:L1443` | deny → allow | Quoted cat emits `r3check_table.py` verbatim; no Bash/Python escape decoding at this step. |
| 7918 | `22e900e1:L1187` | deny → allow | Quoted cat emits `r4_finalize.sh` verbatim; no Bash/Python escape decoding at this step. |
| 7934 | `71968c0c:L428` | deny → allow | Unquoted cat deliberately escapes backticks; \&#124; is inside grep’s quoted expression in a command substitution. |
| 7965 | `148862fb:L9235` | deny → allow | Python intentionally normalizes Unicode punctuation with \u escapes; regexes are raw. |
| 7980 | `8876d9cf:L1447` | deny → allow | Quoted cat emits `$f` verbatim; no Bash/Python escape decoding at this step. |
| 8145 | `9becc1f8:L2331` | deny → allow | Quoted cat emits `state.md` verbatim; no Bash/Python escape decoding at this step. |
| 8149 | `9becc1f8:L2669` | deny → deny | Bash reduces four slashes to two for JSON path escapes. Appears intentional; retained conservative refusal. |
| 8163 | `9becc1f8:L4154` | deny → allow | Quoted cat emits `state3.md` verbatim; no Bash/Python escape decoding at this step. |
| 8167 | `1ec8f69e:L280` | deny → allow | Quoted cat emits `classify.py` verbatim; no Bash/Python escape decoding at this step. |
| 8176 | `1ec8f69e:L1148` | deny → allow | Quoted cat emits `state2143.txt` verbatim; no Bash/Python escape decoding at this step. |
| 8264 | `a0ed29c9:L2374` | deny → allow | Python regex is raw; \x00/\x01 deliberately split Git’s formatted output. |
| 8591 | `acc84549:L426` | deny → allow | Quoted cat emits `rawdump.py`, `dump.csproj` verbatim; no Bash/Python escape decoding at this step. |
| 8595 | `cd2499ed:L459` | deny → allow | Python’s newline, quote, slash, tab, \x01 and \u2028 are deliberate JSON round-trip test data. |
| 8609 | `c2ad5988:L1410` | deny → allow | Python uses intended newlines; the Windows paths belong to a separate quoted cat body. |
| 8615 | `3920cc62:L1682` | deny → allow | Quoted cat emits `lesson.md` verbatim; no Bash/Python escape decoding at this step. |
| 8686 | `ac80da06:L2504` | deny → allow | Quoted cat emits `state2.md` verbatim; no Bash/Python escape decoding at this step. |
| 8687 | `3920cc62:L1990` | deny → allow | Quoted cat emits `msg1.txt` verbatim; no Bash/Python escape decoding at this step. |
| 8688 | `51d2fd14:L333` | deny → allow | Quoted cat emits `$F` verbatim; no Bash/Python escape decoding at this step. |
| 8689 | `24484c67:L329` | deny → allow | Quoted cat emits `msg1.txt` verbatim; no Bash/Python escape decoding at this step. |
| 8708 | `51d2fd14:L848` | deny → allow | Quoted cat emits `state.md` verbatim; no Bash/Python escape decoding at this step. |
| 8801 | `5c5a10b5:L181` | deny → allow | Quoted cat emits `LIVE-PLAY.txt` verbatim; no Bash/Python escape decoding at this step. |
| 8891 | `5750502b:L533` | deny → allow | Quoted cat emits `pdcrun.csproj`, `Program.cs` verbatim; no Bash/Python escape decoding at this step. |
| 8909 | `d0258715:L1001` | deny → allow | Python Windows paths are raw; the regex is rf, including escaped quotes. |
| 8911 | `d0258715:L1370` | deny → allow | Quoted cat emits `PROOF-PORT-G5A.txt` verbatim; no Bash/Python escape decoding at this step. |
| 8920 | `780c3992:L1359` | deny → allow | Quoted cat emits `state.txt` verbatim; no Bash/Python escape decoding at this step. |
| 8921 | `2d0ec830:L1148` | deny → allow | Python regex is raw and \u2028 is an intentional Unicode-separator assertion. |
| 8922 | `780c3992:L1877` | deny → allow | The regex text is in quoted cat prose; the subsequent Python body has no unsafe escape. |
| 8924 | `c779e271:L339` | deny → allow | Python only uses intended escaped quotes/newlines; C# << shifts inside a string are not heredoc headers. |
| 8929 | `780c3992:L2291` | deny → allow | Quoted git commit message; backslashes are message text, with no Bash/Python escape decoding. |
| 8953 | `ee8754e5/agent-ad06778ad4c8b7f3c:L337` | deny → allow | Quoted cat emits `support_on_floor.py` verbatim; no Bash/Python escape decoding at this step. |
| 8961 | `e7287342:L1162` | deny → allow | Quoted git commit message; backslashes are message text, with no Bash/Python escape decoding. |
| 8978 | `ee8754e5:L2158` | deny → allow | Quoted cat emits `state-cleanup.md` verbatim; no Bash/Python escape decoding at this step. |
| 9059 | `b6568d08:L2117` | deny → allow | Quoted cat emits `state-3.md` verbatim; no Bash/Python escape decoding at this step. |
| 9218 | `b49d2807:L510` | deny → allow | Quoted cat emits `bashv.py` verbatim; no Bash/Python escape decoding at this step. |

4970's separate allow-to-deny change restores a policy check, not a demonstrated byte-corruption finding: its non-raw triple strings contain C# `@"\\s*..."` regexes. Python reduces each doubled slash to the single slash the C# regex needs. Rule 7 nevertheless explicitly requires raw Python strings, and the brief explicitly requires this command to refuse.

Database safety was checked before the first test/replay. Framework's `tools/guard_log.py:record()` returns `False` for a non-null payload without `hook_event_name`, before importing the database helper, constructing SQL, or starting a subprocess. The tests omit that key. The replay helper constructs payloads from scratch and asserts that key is absent, rather than reusing live transcript payloads. A direct mocked probe confirmed no SQL-construction, subprocess, or error-log call on that path. A SELECT after testing found **zero `guard-test-*` rows on 21 Nov**. All database statements issued for this task were SELECTs on the permitted table.

The shared post-commit hook calls installed `kor capture-git post-commit --hook`, which writes the database. Every task commit therefore uses `git -c core.hooksPath=Kor.Operations.Framework.Tests/obj/bash-heredoc-narrow/no-hooks commit ...` against an empty local directory; persistent Git configuration was not changed. TEMP and TMP remained `C:\Users\ilalonde\AppData\Local\Temp`; the existing harness used its normal temporary fixtures. There was no push, install, remote execution, settings/hook edit, or source edit outside this worktree.

Under repo rule 11, the fault class is **confusing escape-bearing data with source for an interpreter that will transform it**. The check covers the enumerated shell consumer/delimiter forms, actual Python literal boundaries, known unsafe escapes, deliberate formatting/control data, raw strings, and the preserved rule-7 C# policy case. The harness compares every original check, every added case, and each of the 119 full historical commands, using local evidence. Actual test failures, parsed consumers, byte-probe outputs, and each changed replay verdict were inspected. There is no graphical artifact to render.

It does **not** prove arbitrary shell or Python semantics, author intent, runtime variable expansion, future file execution, all wrapper/alias spellings, pipelines forwarding source to Python, quoted command substitutions, or errors after tokenization stops. It does not inspect the generated C#/PowerShell/Python artifact. On Python before 3.12, an unsafe expression embedded in a raw f-string can be hidden in a single raw STRING token; the tested environment is 3.12.10. It also does not cover manually doubled C# ordinary-string regexes lacking the recognized verbatim marker. Conservative false refusals remain for intentional octal/backspace data and intentionally doubled unquoted heredocs.

A measured same-class miss remains: `python - <<'PY'` with `s = '@"\n"'` still allows and Python changes the intended C# regex escape into an actual newline. This is the longstanding ambiguity between intentional newlines and emitted regex syntax; the implementation does not claim complete enforcement of rule 7. Relative Windows paths composed only of allowed formatting escapes, such as `'work\new'`, can likewise change unnoticed. No broader claim is made from the passing totals.

The brief was accurate about the 119-row population and all 21 named wrong refusals. It was incomplete or misleading in these respects: the starting HEAD also includes two brief commits; 4970 already passes the November 12 guard and is not itself proof of a corrupted regex; legal escapes and deliberate Bash escaping require intent-sensitive judgment, so four retained population refusals are conservative rather than proven bugs; the raw matcher misses f-prefix/escaped-quote boundaries and treats shifts as headers; and `oversight.GuardRefusal` needs the `KorStandards` qualification with this login (`[Rule]` is also a reserved SQL identifier). The two unnamed judged-wrong rows, 8953 and 9059, naturally appear in the mandated full-population replay; they were not sought in any holdout source or used as fixtures in A.

Prior-art searches covered `tools/`, `tools/ClaudeGuardAudit/`, `docs/codex/` narrowing responses, and `../KOR.Drafter/db/tools/` for guard, transcript, refusal, replay, and hook patterns. The existing guard test protocol and prior response's command-local hook bypass supplied the method. No new shipped tool, guard, or rule was added. One Codex session performed the work; no subagents were started.

Local proof artifacts are under the ignored `Kor.Operations.Framework.Tests/obj/bash-heredoc-narrow/`: `rows.txt`, `recovered.json`, `recovery.txt`, `before-tests.txt`, `after-tests.txt`, `baseline-final-tests.txt`, `final-tests.txt`, and `before-replay.json` / `after-replay.json` / `final-replay.json`. The recovery/replay/baseline helpers there are task-local proof scaffolding. The committed deliverables are the test additions, the narrowed matcher/comment, and this one response file.
