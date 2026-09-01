"""Transcribe the Teams recordings locally with faster-whisper. No API spend."""
import sys
import time

from faster_whisper import WhisperModel

model_name = sys.argv[1]
paths = sys.argv[2:]

print(f"loading {model_name} ...", flush=True)
t0 = time.time()
model = WhisperModel(model_name, device="cpu", compute_type="int8")
print(f"loaded in {time.time() - t0:.0f}s", flush=True)

# Terms this drawing/analysis vocabulary needs; whisper drifts on them without a hint.
PROMPT = (
    "ETABS, e2k, DXF, Revit, mezzanine, diaphragm, spandrel, pier label, shear wall, "
    "slab edge, storey, level, podium, parkade, transfer slab, drop panel, column, "
    "opening, shell element, stiffness modifier, gravity, lateral, BLDG, YMCA Langara."
)

for path in paths:
    print(f"\n{'=' * 70}\nFILE: {path}\n{'=' * 70}", flush=True)
    segments, info = model.transcribe(
        path,
        language="en",
        beam_size=5,
        vad_filter=True,
        initial_prompt=PROMPT,
    )
    print(f"[duration {info.duration:.0f}s]", flush=True)
    for seg in segments:
        mm, ss = divmod(int(seg.start), 60)
        print(f"[{mm:02d}:{ss:02d}] {seg.text.strip()}", flush=True)
