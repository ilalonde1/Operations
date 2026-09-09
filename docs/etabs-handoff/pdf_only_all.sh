#!/bin/bash
# Build every set's ETABS model from its stick file alone: DXFs off pdf-takeoff, levels off pdf-levels, no reference.
cd "C:/VIsual Studio Projects/Operations" || exit 1
H="$LOCALAPPDATA/Temp/kor-drawings/harness"; S="$LOCALAPPDATA/Temp/kor-drawings/stickfiles"
CLI=Kor.Operations.EngineeringTools.TakeoffCli/bin/Debug/net8.0/takeoff.exe
for spec in "31130-01 96" "31138-01 96" "31065-01 100" "31202-01 96" "31168-01 96"; do
  set -- $spec; job=$1; scale=$2
  E="$H/pdf-only-$job"; rm -rf "$E"; mkdir -p "$E/dxf"
  "$CLI" pdf-takeoff "$S/$job.pdf" "$E/dxf/$job.dxf" --pages 1-$(python -c "import pypdf,sys; print(len(pypdf.PdfReader(sys.argv[1]).pages))" "$S/$job.pdf" 2>/dev/null || echo 80) --scale $scale --kor-layers > "$E/takeoff.txt" 2>&1
  "$CLI" pdf-levels "$S/$job.pdf" "$E/levels.csv" > "$E/levels.txt" 2>&1
  "$CLI" dxf-to-etabs "$E/dxf" - "$E/out.e2k" --levels "$E/levels.csv" --levels-unit mm > "$E/console.txt" 2>&1
  echo "== $job: $(tail -1 "$E/takeoff.txt" | cut -c1-70) | $(tail -2 "$E/levels.txt" | head -1 | cut -c1-90)"
  sed -n '2,7p' "$E/console.txt" | tr '\n' ' ' | cut -c1-200; echo
  grep -cE "carry the same match line" "$E/console.txt" | sed 's/^/   joined pairs: /'
  grep -E "OUTER face" "$E/console.txt" | sed 's/^  - //' | cut -c1-150
  python docs/etabs-handoff/plate_by_storey.py "$E/out.e2k" 2>/dev/null | head -12 | sed 's/^/   /'
done
