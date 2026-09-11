#!/bin/bash
# Every storey of an .e2k on one sheet, as a PNG you can open — the render-and-look step of CLAUDE.md rule 9.
#
#     bash docs/etabs-handoff/render_storeys.sh <model.e2k> <out.png> "<title>" [columns] [cell px]
#
# plan_sheet.py draws the SVG; Edge headless rasterises it. Edge is given ITS OWN PROFILE DIRECTORY: without one,
# a running Edge window takes the call, opens nothing, writes nothing, and exits 0 — which is why "the screenshot
# is flaky" was written three times on 2026-09-10 before the cause was found. With --user-data-dir it has not
# failed once. The profile lives beside the harness, so nothing is written into the repo.
set -e
E2K="$1"; OUT="$2"; TITLE="${3:-$(basename "$E2K")}"; COLS="${4:-3}"; CELL="${5:-600}"
[ -f "$E2K" ] || { echo "no such model: $E2K" >&2; exit 1; }
# Every path Edge sees is forward-slash form: $LOCALAPPDATA arrives with backslashes and Edge silently writes
# nothing for a mixed "C:\Users\...\Local/Temp/..." path.
H="$(cygpath -m "$LOCALAPPDATA")/Temp/kor-drawings/harness"; mkdir -p "$H"
OUT="$(cygpath -m "$OUT")"; SVG="${OUT%.png}.svg"
cd "C:/VIsual Studio Projects/Operations"
python docs/etabs-handoff/plan_sheet.py "$E2K" "$SVG" "$TITLE" "$COLS" "$CELL"
# Two tries: a headless Edge from the previous call can still hold the profile lock for a second or two after
# it has written its file, and the next call then hands off to it and writes nothing (seen once, 2026-09-10).
for attempt in 1 2; do
    "/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" --headless=new --disable-gpu --hide-scrollbars --no-first-run \
        --user-data-dir="$H/edge-profile" --window-size=1900,1900 --screenshot="$OUT" "file:///$SVG" >/dev/null 2>&1 || true
    [ -s "$OUT" ] && break
    sleep 3
done
[ -s "$OUT" ] || { echo "Edge wrote nothing for $OUT" >&2; exit 2; }
echo "$OUT"
