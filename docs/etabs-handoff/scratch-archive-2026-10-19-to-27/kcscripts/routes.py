import re, sys
lines = open(sys.argv[1], encoding='utf-8').read().split('\n')
end = next(i for i, l in enumerate(lines) if l.startswith('Disassembly of'))
start = next(i for i, l in enumerate(lines) if l.startswith('# FULL DISASSEMBLY'))
n = 0
pat = re.compile(r'LOAD_ATTR\s+\d+ \((get|post|put|delete|patch|on_event|mount|websocket|api_route|middleware|add_middleware|exception_handler|include_router|add_route) ')
for i in range(start, end):
    m = pat.search(lines[i])
    if m:
        c = None
        for j in range(i + 1, i + 6):
            c = re.search(r"LOAD_(?:CONST|NAME)\s+\d+ \((.*)\)$", lines[j])
            if c:
                break
        h = ''
        for k in range(i + 1, i + 25):
            hm = re.search(r'code object (\w+) at .*line (\d+)', lines[k])
            if hm and hm.group(1) != '__annotate__':
                h = hm.group(1) + '@' + hm.group(2)
                break
        n += 1
        print(f"dis{i+1}\t{m.group(1)}\t{c.group(1) if c else '?'}\t{h}")
print('count', n)
