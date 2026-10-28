"""Condense a dis dump into one line per source line: tokens of names/consts/attrs/calls.
Usage: condense.py <dis.txt> [qualname-regex]
Prints sections whose code-object name matches the regex (default all)."""
import re, sys
sys.stdout.reconfigure(encoding='utf-8')
path = sys.argv[1]
want = re.compile(sys.argv[2]) if len(sys.argv) > 2 else None
lines = open(path, encoding='utf-8').read().split('\n')
hdr = re.compile(r'^Disassembly of <code object (\S+) at \S+, file "[^"]*", line (\d+)>:')
op = re.compile(r'^\s*(\d+)?\s+(?:L\d+:)?\s*([A-Z_]+)\s*(\d+)?\s*(?:\((.*)\))?\s*$')
cur = None
show = False
buf = []
curline = None
def flush():
    global buf
    if buf and show:
        print(f"  {curline}: " + ' '.join(buf))
    buf = []
for i, l in enumerate(lines):
    if l.startswith('# FULL DISASSEMBLY'):
        cur = '<module>'
        show = (want is None) or bool(want.search(cur))
        if show:
            print(f"\n=== <module> (dis line {i+1})")
        continue
    h = hdr.match(l)
    if h:
        flush()
        cur = h.group(1)
        show = (want is None) or bool(want.search(cur))
        if show:
            print(f"\n=== {cur} (src line {h.group(2)}, dis line {i+1})")
        continue
    if not show:
        continue
    m = op.match(l)
    if not m:
        continue
    ln, opname, _, arg = m.groups()
    if ln:
        flush()
        curline = ln
    tok = None
    if opname in ('LOAD_CONST', 'LOAD_SMALL_INT'):
        tok = arg if arg is not None else _
        if arg and arg.startswith('<code object'):
            tok = '<code ' + arg.split()[2] + '>'
    elif opname in ('LOAD_GLOBAL', 'LOAD_NAME', 'LOAD_FAST', 'LOAD_FAST_BORROW', 'LOAD_DEREF', 'LOAD_FAST_LOAD_FAST', 'LOAD_FAST_BORROW_LOAD_FAST_BORROW'):
        tok = (arg or '').replace(' + NULL', '')
    elif opname in ('LOAD_ATTR', 'LOAD_METHOD', 'LOAD_SUPER_ATTR'):
        tok = '.' + (arg or '').replace(' + NULL|self', '').replace(' + NULL', '')
    elif opname.startswith('STORE_'):
        tok = '=>' + (arg or '')
    elif opname in ('CALL', 'CALL_KW', 'CALL_FUNCTION_EX'):
        tok = '()'
    elif opname in ('COMPARE_OP', 'CONTAINS_OP', 'IS_OP'):
        tok = '?' + (arg or opname)
    elif opname == 'BINARY_OP':
        tok = '[' + (arg or '') + ']'
    elif opname == 'UNARY_NEGATIVE':
        tok = 'NEG'
    elif opname == 'UNARY_NOT':
        tok = 'NOT'
    elif opname == 'UNARY_INVERT':
        tok = 'INV'
    elif opname in ('RETURN_VALUE',):
        tok = 'RET'
    elif opname in ('RAISE_VARARGS',):
        tok = 'RAISE'
    elif opname in ('BUILD_MAP', 'BUILD_LIST', 'BUILD_TUPLE'):
        tok = opname[6:].lower() + '/' + str(_)
    elif opname in ('FOR_ITER',):
        tok = 'FOR'
    elif opname.startswith('POP_JUMP_IF'):
        tok = 'IF' + opname[11:]
    elif opname in ('IMPORT_NAME', 'IMPORT_FROM'):
        tok = 'import:' + (arg or '')
    if tok is not None:
        buf.append(tok)
flush()
