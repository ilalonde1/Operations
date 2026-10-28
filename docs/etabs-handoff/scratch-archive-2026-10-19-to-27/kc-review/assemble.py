# House page head (style block) from the latest web-PDF source + this review's body.
import sys

ref, body, out = sys.argv[1], sys.argv[2], sys.argv[3]
with open(ref, encoding='utf-8') as f:
    html = f.read()
head = html[:html.index('</head><body>') + len('</head><body>')]
head = head.replace('<title>KOR Operating Plan</title>', '<title>KorConcrete Review</title>')
assert '<title>KorConcrete Review</title>' in head
with open(body, encoding='utf-8') as f:
    b = f.read()
with open(out, 'w', encoding='utf-8') as f:
    f.write(head + '\n' + b)
print(len(head), len(b))
