"""Harvest og:title / og:description from every BuildSD project page.

BuildSD renders project detail client-side, but each page's server-rendered
<head> carries a one-line status summary in og:description. That is enough
for a stage snapshot of San Diego's tracked pipeline.
"""
import concurrent.futures as cf
import html
import re
import urllib.request

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0 Safari/537.36")

slugs = [s.strip() for s in open("slugs.txt", encoding="utf-8") if s.strip()]


def grab(slug):
    url = "https://buildsd.org/projects/" + slug
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    try:
        with urllib.request.urlopen(req, timeout=25) as r:
            s = r.read().decode("utf-8", "replace")
    except Exception as e:
        return slug, "ERROR", str(e)[:60]
    t = re.search(r'property="og:title" content="(.*?)"', s)
    d = re.search(r'name="og:description" content="(.*?)"', s)
    if not d:
        d = re.search(r'property="og:description" content="(.*?)"', s)
    title = html.unescape(t.group(1)) if t else ""
    desc = html.unescape(d.group(1)) if d else ""
    return slug, title, desc


rows = []
with cf.ThreadPoolExecutor(max_workers=8) as ex:
    for slug, title, desc in ex.map(grab, slugs):
        rows.append((slug, title, desc))

rows.sort()
with open("buildsd_projects.txt", "w", encoding="utf-8") as f:
    for slug, title, desc in rows:
        f.write(f"{slug}\t{title}\t{desc}\n")

print("wrote", len(rows), "rows")
