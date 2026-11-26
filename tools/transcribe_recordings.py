"""Transcribe each .mp4 in a folder with faster-whisper on CPU, writing <name>.txt beside it with
[h:mm:ss] timestamped lines. A file whose .txt already exists is skipped, so a stopped run resumes.

Usage:  python -I tools/transcribe_recordings.py <folder> [model]     model default: small.en

Needs faster-whisper and ffmpeg (both installed on KOR-1001; models cached in ~/.cache/huggingface/hub).
OneDrive Recordings are cloud placeholders: copy them into a local folder first (OneDrive must be running).
Used 26 Nov 2026 for the PM Tools spec (docs/KOR-PMTools-Redesign-Spec-2026-11-26.md): 8 Jim recordings,
134 minutes of audio, small.en on 12 threads.
"""
import os
import sys
import time


def main(argv):
    if len(argv) < 2 or not os.path.isdir(argv[1]):
        print(__doc__)
        return 2
    folder = argv[1]
    model_name = argv[2] if len(argv) > 2 else "small.en"

    from faster_whisper import WhisperModel

    threads = max(1, (os.cpu_count() or 2) - 2)
    model = WhisperModel(model_name, device="cpu", compute_type="int8", cpu_threads=threads)
    files = sorted(f for f in os.listdir(folder) if f.lower().endswith(".mp4"))
    for f in files:
        out = os.path.join(folder, f[:-4] + ".txt")
        if os.path.exists(out):
            print("skip", f, flush=True)
            continue
        t0 = time.time()
        segs, info = model.transcribe(os.path.join(folder, f), language="en", vad_filter=True, beam_size=1)
        lines = []
        for s in segs:
            m, sec = divmod(int(s.start), 60)
            h, m = divmod(m, 60)
            lines.append(f"[{h}:{m:02d}:{sec:02d}] {s.text.strip()}")
        with open(out + ".part", "w", encoding="utf-8") as fh:
            fh.write(f"# {f}\n# duration {info.duration / 60:.1f} min, model {model_name}\n" + "\n".join(lines) + "\n")
        os.replace(out + ".part", out)
        print(f"done {f}: {info.duration / 60:.1f} min audio in {(time.time() - t0) / 60:.1f} min, {len(lines)} segments", flush=True)
    print("ALL DONE", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
