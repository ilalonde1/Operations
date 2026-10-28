"""Render a DXF as CAD opens it (off layers hidden) next to the PDF page crop. usage: look.py <in.dxf> <out.png>"""
import sys
import ezdxf
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.image import imread
from ezdxf.addons.drawing import RenderContext, Frontend
from ezdxf.addons.drawing.matplotlib import MatplotlibBackend
from ezdxf.addons.drawing.config import Configuration, BackgroundPolicy

doc = ezdxf.readfile(sys.argv[1])
fig, (a, b) = plt.subplots(1, 2, figsize=(14, 13), dpi=110)
b.imshow(imread(r"C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\omar\p6-plan.png"))
b.set_title("the sheet (page 6)"); b.axis("off")
ctx = RenderContext(doc)
Frontend(ctx, MatplotlibBackend(a), config=Configuration(background_policy=BackgroundPolicy.WHITE)).draw_layout(doc.modelspace())
a.set_title("the DXF, as CAD opens it"); a.set_aspect("equal")
fig.tight_layout(); fig.savefig(sys.argv[2])
print("layers off:", [l.dxf.name for l in doc.layers if l.is_off()])
