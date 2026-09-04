import io

tpl = io.open('tools/BdDocTemplate/reference-handbook.html', encoding='utf-8').read()
a = tpl.index('<style>')
b = tpl.index('</style>') + len('</style>')
style = tpl[a:b]

body = io.open('docs/island-pipeline/whoswho-v4-body.html', encoding='utf-8').read()

head = (
    '<!DOCTYPE html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n'
    '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
    "<title>KOR Structural &mdash; Who's who: Greater Victoria &amp; Nanaimo (v4)</title>\n"
)
out = head + style + '\n</head>\n<body>\n' + body + '\n</body>\n</html>\n'
io.open('docs/island-pipeline/whoswho-v4.html', 'w', encoding='utf-8').write(out)

print('style bytes:', len(style), '| body bytes:', len(body), '| page bytes:', len(out))
for needle in ['--maxread', '@page', '.hero strong', 'break-after']:
    print(needle, '->', style.count(needle))
