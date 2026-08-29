#!/usr/bin/env python3
"""Ask a domain what it does, instead of guessing from its name.

WHY
    The whole "no architect on this team" finding depends on classifying every
    contact domain on a filing. Guessing from the name is how "kontexture.com"
    gets filed as unknown when it might be an architecture practice - which
    would turn an open seat into a closed one and put a wrong project in front
    of a client.

    So each unresolved domain is fetched and read: the title, the meta
    description and the visible headings usually say plainly what the firm is.
    Anything still ambiguous is reported AMBIGUOUS and must be checked by hand
    before the case it belongs to is used.

USAGE
    python classify_domain_role.py domain [domain ...]
"""
import re
import sys
import urllib.request

UA = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                    "AppleWebKit/537.36 (KHTML, like Gecko) "
                    "Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0"}

TESTS = [
    ("architect", r"(?i)\b(architect|architecture|architectural|arquitect)\b"),
    ("landscape", r"(?i)\blandscape architect"),
    ("counsel", r"(?i)\b(attorney|law firm|legal counsel|lawyers|p\.?l\.?l\.?c\.? "
                r"attorneys|land use law)\b"),
    ("civil", r"(?i)\b(civil engineer|engineering|surveying|geotechnical|"
              r"structural engineer)\b"),
    ("planning", r"(?i)\b(land planning|urban planning|entitlement|"
                 r"planning consultant)\b"),
    ("developer", r"(?i)\b(developer|development company|we develop|"
                  r"real estate development|homebuilder|home builder|"
                  r"communities|multifamily developer|investment)\b"),
]


def text_of(domain):
    for scheme in ("https://", "http://"):
        try:
            req = urllib.request.Request(scheme + domain, headers=UA)
            raw = urllib.request.urlopen(req, timeout=25).read()
            html = raw.decode("utf-8", "replace")
            title = " ".join(re.findall(r"(?is)<title[^>]*>(.*?)</title>", html)[:1])
            desc = " ".join(re.findall(
                r'(?is)<meta[^>]+name=["\']description["\'][^>]+content=["\'](.*?)["\']',
                html)[:1])
            heads = " ".join(re.findall(r"(?is)<h[12][^>]*>(.*?)</h[12]>", html)[:6])
            body = re.sub(r"<[^>]*>", " ", title + " " + desc + " " + heads)
            return re.sub(r"\s+", " ", body).strip()[:600]
        except Exception:
            continue
    return None


def classify(domain):
    t = text_of(domain)
    if not t:
        return "UNREACHABLE", ""
    hits = [role for role, pat in TESTS if re.search(pat, t)]
    if not hits:
        return "AMBIGUOUS", t[:150]
    # architect wins over the generic ones when both appear -- an architecture
    # practice often also says "development" or "planning" on its own site.
    for pref in ("architect", "landscape", "counsel", "civil", "planning"):
        if pref in hits:
            return pref, t[:150]
    return hits[0], t[:150]


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        raise SystemExit(2)
    for d in sys.argv[1:]:
        role, why = classify(d)
        print("%-26s %-12s %s" % (d, role, why[:110]))
