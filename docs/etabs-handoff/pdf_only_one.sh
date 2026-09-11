#!/bin/bash
# One job of pdf_only_all.sh, for characterising a change before the six-set run: the same commands, the
# same folders, one set.   bash docs/etabs-handoff/pdf_only_one.sh <job> [scale]      e.g. 31130-01 96
cd "C:/VIsual Studio Projects/Operations" || exit 1
H="$LOCALAPPDATA/Temp/kor-drawings/harness"; S="$LOCALAPPDATA/Temp/kor-drawings/stickfiles"
CLI=Kor.Operations.EngineeringTools.TakeoffCli/bin/Debug/net8.0/takeoff.exe
job=$1; scale=${2:-96}
E="$H/pdf-only-$job"; rm -rf "$E"; mkdir -p "$E/dxf"
"$CLI" pdf-takeoff "$S/$job.pdf" "$E/dxf/$job.dxf" --pages 1-$(python -c "import pypdf,sys; print(len(pypdf.PdfReader(sys.argv[1]).pages))" "$S/$job.pdf" 2>/dev/null || echo 80) --scale $scale --kor-layers > "$E/takeoff.txt" 2>&1
"$CLI" pdf-levels "$S/$job.pdf" "$E/levels.csv" > "$E/levels.txt" 2>&1
"$CLI" dxf-to-etabs "$E/dxf" - "$E/out.e2k" --levels "$E/levels.csv" --levels-unit mm > "$E/console.txt" 2>&1
echo "== $job: $(tail -1 "$E/takeoff.txt" | cut -c1-70) | FAILED pages $(grep -c FAILED "$E/takeoff.txt")"
sed -n '2,7p' "$E/console.txt" | tr '\n' ' ' | cut -c1-200; echo
