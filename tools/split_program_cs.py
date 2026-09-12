"""ONE-SHOT REFACTOR (completion plan WP3, 2026-09-11): the takeoff CLI's Program.cs - 5,150 lines,
sixty-odd verbs as top-level `if (args[0].Equals("verb")) { ... return n; }` blocks - split into one
file per verb under Verbs/, a registry in the original order, global usings, and a Program.cs that is
the help check, the dispatch, and the default (the rebar CSV delta). Nothing in a verb's body is
edited: the body moves into `public static int Run(string[] args)` verbatim, its `if` condition into
`Matches(args)`, its leading comment above the class. The gate is the help-list test and the six-set
test byte-identical.

    python tools/split_program_cs.py            # writes Verbs/*.cs, TakeoffVerbs.cs, GlobalUsings.cs, Program.cs

Kept in tools/ as the record of how the split was done; it has nothing to do after it has run once.
"""
import io
import os
import re

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Kor.Operations.EngineeringTools.TakeoffCli")
PROGRAM = os.path.join(ROOT, "Program.cs")
VERB_LINE = re.compile(r'^if \((?P<cond>.*args\[0\]\.Equals\("(?P<name>[a-z0-9-]+)".*)\)\s*$')
VERB_LINE_ONE = re.compile(r'^if \((?P<cond>.*args\[0\]\.Equals\("(?P<name>[a-z0-9-]+)".*)\)\s*$')


def pascal(name):
    return "".join(part[:1].upper() + part[1:] for part in name.split("-"))


def main():
    lines = io.open(PROGRAM, encoding="utf-8").read().split("\n")
    # 1. usings
    usings = [l for l in lines if l.startswith("using ")]
    first_code = next(i for i, l in enumerate(lines) if l.strip() and not l.startswith("using "))
    # 2. where the type declarations begin (everything from there stays as is)
    types_at = next(i for i, l in enumerate(lines) if re.match(r"^(sealed |static |public |internal )?(class|record|static class|sealed class|sealed record) ", l))
    body = lines[first_code:types_at]
    tail_types = lines[types_at:]

    verbs = []          # (name, class_name, comment_lines, cond, body_lines)
    kept = []           # top-level lines that are not verb blocks (help check, default path)
    i = 0
    seen = {}
    while i < len(body):
        line = body[i]
        m = VERB_LINE.match(line)
        if m:
            # leading comment: contiguous // lines directly above (already appended to kept: pull them back)
            comment = []
            while kept and kept[-1].startswith("//"):
                comment.insert(0, kept.pop())
            # blank lines between comment and if were consumed as kept too; drop trailing blanks in kept
            while kept and kept[-1].strip() == "" and len(kept) > 0 and (len(comment) > 0 or True):
                # keep one blank as separator between kept items
                kept.pop(); break
            cond = m.group("cond")
            name = m.group("name")
            # body: next line is "{" at column 0, block ends at "}" at column 0; or one-liner "{ ...; return 1; }"
            j = i + 1
            if body[j].startswith("{ ") and body[j].rstrip().endswith("}"):
                block = [body[j][1:-1].strip()]
                end = j
            else:
                assert body[j] == "{", (i, body[j])
                depth = 0
                end = None
                for k in range(j, len(body)):
                    if body[k] == "{":
                        depth += 1
                    elif body[k] == "}":
                        depth -= 1
                        if depth == 0:
                            end = k
                            break
                assert end is not None, name
                block = body[j + 1:end]
            # a one-line block that only prints a usage line is the verb's usage pre-check, named for what it is
            is_usage_check = len(block) == 1 and "Usage:" in block[0] and "return 1;" in block[0]
            n = seen.get(name, 0) + 1
            seen[name] = n
            cls = pascal(name) + ("UsageVerb" if is_usage_check else "Verb")
            verbs.append((name, cls, comment, cond, block))
            i = end + 1
            continue
        kept.append(line)
        i += 1

    # 3. write the verb files
    verbs_dir = os.path.join(ROOT, "Verbs")
    os.makedirs(verbs_dir, exist_ok=True)
    for name, cls, comment, cond, block in verbs:
        out = []
        out.append("// The takeoff verb `" + name + "`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.")
        out.extend(comment)
        out.append(f"internal static class {cls}")
        out.append("{")
        out.append(f"    public static bool Matches(string[] args) => {cond};")
        out.append("")
        # a verb that awaited at top level (Program.cs's Main was async for it) awaits in an async method;
        # the registry runs it to completion, which is what a console app's top-level await did
        is_async = any(re.search(r"\bawait\b", l) for l in block)
        if is_async:
            out.append("    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();")
            out.append("")
            out.append("    private static async Task<int> RunAsync(string[] args)")
        else:
            out.append("    public static int Run(string[] args)")
        out.append("    {")
        for l in block:
            out.append(("    " + l) if l.strip() else "")
        out.append("    }")
        out.append("}")
        io.open(os.path.join(verbs_dir, cls + ".cs"), "w", encoding="utf-8").write("\n".join(out) + "\n")

    # 4. the registry, in the original order
    reg = [
        "// THE VERBS, IN THE ORDER PROGRAM.CS TRIED THEM (WP3, 2026-09-11): a usage pre-check for a verb sits",
        "// before the verb, as it did. Program.cs asks each in turn; the first that matches runs.",
        "internal static class TakeoffVerbs",
        "{",
        "    public static readonly IReadOnlyList<(string Name, Func<string[], bool> Matches, Func<string[], int> Run)> All =",
        "    [",
    ]
    for name, cls, comment, cond, block in verbs:
        reg.append(f'        ("{name}", {cls}.Matches, {cls}.Run),')
    reg.append("    ];")
    reg.append("}")
    io.open(os.path.join(ROOT, "TakeoffVerbs.cs"), "w", encoding="utf-8").write("\n".join(reg) + "\n")

    # 5. global usings
    io.open(os.path.join(ROOT, "GlobalUsings.cs"), "w", encoding="utf-8").write(
        "// Every verb file and Program.cs share these (WP3, 2026-09-11).\n" + "\n".join("global " + u for u in usings) + "\n")

    # 6. Program.cs: the kept top-level lines with the dispatch where the first verb was
    new_program = []
    dispatched = False
    for l in kept:
        new_program.append(l)
    # the help check is the first kept statement; put the dispatch right after its closing brace
    out = []
    inserted = False
    for l in new_program:
        out.append(l)
        if not inserted and l == "}":
            out.append("")
            out.append("// EVERY VERB IN ITS OWN FILE (Verbs/*.cs), asked in the order Program.cs always asked them (TakeoffVerbs).")
            out.append("foreach (var (_, matches, run) in TakeoffVerbs.All)")
            out.append("    if (matches(args)) return run(args);")
            inserted = True
    # trim leading blank lines
    while out and out[0].strip() == "":
        out.pop(0)
    io.open(PROGRAM, "w", encoding="utf-8").write("\n".join(out + [""] + tail_types))
    print(f"{len(verbs)} verb blocks -> Verbs/; Program.cs kept {len(out)} top-level lines + {len(tail_types)} lines of types")


if __name__ == "__main__":
    main()
