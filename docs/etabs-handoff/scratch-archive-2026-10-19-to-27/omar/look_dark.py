"""Render a DXF the way CAD opens it: dark background, off layers hidden. usage: look_dark.py <in.dxf> <out.png>"""
import sys
import ezdxf
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from ezdxf.addons.drawing import RenderContext, Frontend
from ezdxf.addons.drawing.matplotlib import MatplotlibBackend
from ezdxf.addons.drawing.config import Configuration, BackgroundPolicy

doc = ezdxf.readfile(sys.argv[1])
fig = plt.figure()
ax = fig.add_axes([0, 0, 1, 1])
Frontend(RenderContext(doc), MatplotlibBackend(ax),
         config=Configuration(background_policy=BackgroundPolicy.BLACK)).draw_layout(doc.modelspace())
ax.set_aspect("equal")
fig.set_size_inches(7, 14)
fig.savefig(sys.argv[2], dpi=110, facecolor="black")
print("layers off:", [l.dxf.name for l in doc.layers if l.is_off()])
