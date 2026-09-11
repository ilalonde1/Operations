#!/bin/bash
# Build every set's ETABS model from its stick file alone: DXFs off pdf-takeoff, levels off pdf-levels, no reference.
#
#     bash docs/etabs-handoff/pdf_only_all.sh            # all six, IN PARALLEL, then the per-job summaries in a fixed order
#
# The six jobs are independent and run at once (2026-09-10: one after another took ~9 min, and every rule needs
# this run before and after — "another harness, another suite. This is TEDIOUS" — Ian). Bounded by the biggest
# set now, about 3 min. Each job writes only into its own folder; the summaries are printed afterwards, in the
# order below, so the output reads the same as it always did.
cd "C:/VIsual Studio Projects/Operations" || exit 1
H="$LOCALAPPDATA/Temp/kor-drawings/harness"; S="$LOCALAPPDATA/Temp/kor-drawings/stickfiles"
CLI=Kor.Operations.EngineeringTools.TakeoffCli/bin/Debug/net8.0/takeoff.exe
# 31170-01-arch is the ARCHITECT's set (Vectorworks), not a KOR stick file: the sixth job since 2026-09-10,
# and the only one from another office. What it reads with no code change is the measure of the tool.
JOBS=("31130-01 96" "31138-01 96" "31065-01 100" "31202-01 96" "31168-01 96" "31170-01-arch 96")

build_one() {
  local job=$1 scale=$2
  local E="$H/pdf-only-$job"; rm -rf "$E"; mkdir -p "$E/dxf"
  local pages; pages=$(python -c "import pypdf,sys; print(len(pypdf.PdfReader(sys.argv[1]).pages))" "$S/$job.pdf" 2>/dev/null || echo 80)
  "$CLI" pdf-takeoff "$S/$job.pdf" "$E/dxf/$job.dxf" --pages 1-$pages --scale $scale --kor-layers > "$E/takeoff.txt" 2>&1
  "$CLI" pdf-levels "$S/$job.pdf" "$E/levels.csv" > "$E/levels.txt" 2>&1
  "$CLI" dxf-to-etabs "$E/dxf" - "$E/out.e2k" --levels "$E/levels.csv" --levels-unit mm > "$E/console.txt" 2>&1
}

for spec in "${JOBS[@]}"; do set -- $spec; build_one "$1" "$2" & done
wait

for spec in "${JOBS[@]}"; do
  set -- $spec; job=$1
  E="$H/pdf-only-$job"
  # A page the intake throws on prints FAILED and the sheet is simply absent from the model; the counts then
  # look like a rule that lost sheets. Said here so it can never pass as a result (2026-09-10: 53 of 67 pages
  # failed on an index bug and the first reading of the run was "the rule moved every set").
  failed=$(grep -c FAILED "$E/takeoff.txt"); [ "$failed" -gt 0 ] && echo "⛔ $job: $failed page(s) FAILED in pdf-takeoff - the model below is missing them; fix that before reading any number"
  # And a model the composer refused to build (KorStandards unreachable over the VPN, 2026-09-10, four of six
  # jobs in one run) leaves no out.e2k at all; the diff scripts then compare against nothing.
  grep -q "^Cannot build a model" "$E/console.txt" && echo "⛔ $job: NO MODEL - $(head -1 "$E/console.txt" | cut -c1-140)"
  echo "== $job: $(tail -1 "$E/takeoff.txt" | cut -c1-70) | $(tail -2 "$E/levels.txt" | head -1 | cut -c1-90)"
  sed -n '2,7p' "$E/console.txt" | tr '\n' ' ' | cut -c1-200; echo
  grep -cE "carry the same match line" "$E/console.txt" | sed 's/^/   joined pairs: /'
  grep -E "OUTER face" "$E/console.txt" | sed 's/^  - //' | cut -c1-150
  python docs/etabs-handoff/plate_by_storey.py "$E/out.e2k" 2>/dev/null | head -12 | sed 's/^/   /'
done
