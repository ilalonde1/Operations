import fitz
doc = fitz.open(r"C:\Users\ilalonde\Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
p = doc[5]
print("page mediabox", doc.xref_get_key(p.xref, "MediaBox"), "cropbox", doc.xref_get_key(p.xref, "CropBox"), "UserUnit", doc.xref_get_key(p.xref, "UserUnit"))
shown = 0
for a in p.annots():
    n = doc.xref_get_key(a.xref, "AP/N")
    s = doc.xref_stream(int(n[1].split()[0])).decode("latin-1")
    if "BT" in s and a.info.get("content") in ("4,436 sf", "3'-0\"", "14\" wall"):
        i = s.find("BT")
        print("----", a.type[1], repr(a.info.get("content")), "Rect(pdf)", doc.xref_get_key(a.xref, "Rect")[1])
        print(s[max(0, i - 300):i + 200])
