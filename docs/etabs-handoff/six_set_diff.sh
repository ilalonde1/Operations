#!/bin/bash
# Every set against a banked step, in one line each: the reading that follows every pdf_only_all.sh run.
#
#     bash docs/etabs-handoff/six_set_diff.sh s41        # each job's out.e2k against pdf-only-<job>-s41.e2k
#
# Prints, per job: byte-identical (cmp), or plates moved / columns lost+gained / walls lost+gained, or NO MODEL when
# the composer refused to build one. The member counts are SUMMED FROM THE STOREY LINES' bracketed counts, not from
# the printed positions, which members_diff.py caps at twelve a kind (Codex audit 2026-09-11, F24). A set that
# prints anything but "byte-identical" gets rendered and LOOKED at before the step is called landed. What this
# cannot see: a member whose section or spans changed with its position unchanged, and a plate translated without
# a change of area (plate_diff.py compares areas) — the cmp line is the whole truth, this line is the summary.
cd "C:/VIsual Studio Projects/Operations" || exit 1
H="$LOCALAPPDATA/Temp/kor-drawings/harness"; step=${1:?banked step, e.g. s41}
for job in 31130-01 31138-01 31065-01 31202-01 31168-01 31170-01-arch; do
  a="$H/pdf-only-$job-$step.e2k"; b="$H/pdf-only-$job/out.e2k"
  [ -f "$a" ] || { echo "$job: no banked $step"; continue; }
  [ -f "$b" ] || { echo "$job: NO MODEL ($(head -1 "$H/pdf-only-$job/console.txt" 2>/dev/null | cut -c1-100))"; continue; }
  if cmp -s "$a" "$b"; then echo "$job: byte-identical to $step"; continue; fi
  d=$(python docs/etabs-handoff/members_diff.py "$a" "$b" 2>&1)
  sum() { echo "$d" | grep -oE "$1 [0-9]+" | awk '{s+=$NF} END {print s+0}'; }
  echo "$job: plates moved $(python docs/etabs-handoff/plate_diff.py "$a" "$b" mm 2>&1 | grep -c moved), columns lost $(sum 'lost columns') / gained $(sum 'gained columns'), walls lost $(sum 'lost walls') / gained $(sum 'gained walls')$(echo "$d" | grep -oE '\(the second model sits [^)]*\)' | head -1 | sed 's/^/  /')"
done
