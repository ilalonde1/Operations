import fitz, re
doc = fitz.open(r"C:\Users\ilalonde\Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
p = doc[5]
for a in p.annots():
    x = a.xref
    ap = doc.xref_get_key(x, "AP")
    flags = a.flags
    content = a.info.get("content", "")
    # Normal appearance stream xref
    n = doc.xref_get_key(x, "AP/N")
    stream = b""
    if n[0] == "xref":
        stream = doc.xref_stream(int(n[1].split()[0])) or b""
    s = stream.decode("latin-1")
    bt = s.count("BT")
    tj = re.findall(r"\((.*?)\)\s*Tj|\[(.*?)\]\s*TJ", s)
    tm = re.findall(r"([-\d.]+ [-\d.]+ [-\d.]+ [-\d.]+ [-\d.]+ [-\d.]+) Tm", s)
    bbox = doc.xref_get_key(int(n[1].split()[0]), "BBox")[1] if n[0] == "xref" else ""
    mtx = doc.xref_get_key(int(n[1].split()[0]), "Matrix")[1] if n[0] == "xref" else ""
    print(f"{a.type[1]:8} {content!r:14} flags={flags} BT={bt} text={[t[0] or t[1][:20] for t in tj][:3]} Tm={tm[:1]} bbox={bbox} matrix={mtx} rot={doc.xref_get_key(x,'Rotate')[1]} IT={doc.xref_get_key(x,'IT')[1]}")
