#!/bin/bash
# Every set against a banked step, in one line each: the reading that follows every pdf_only_all.sh run.
#
#     bash docs/etabs-handoff/six_set_diff.sh s37        # each job's out.e2k against pdf-only-<job>-s37.e2k
#
# Prints, per job: byte-identical, or plates moved / column changes / walls lost / walls gained (members_diff.py,
# with the model's re-derived offset taken out), or NO MODEL when the composer refused to build one. A set that
# prints anything but "byte-identical" gets rendered and LOOKED at before the step is called landed.
cd "C:/VIsual Studio Projects/Operations" || exit 1
H="$LOCALAPPDATA/Temp/kor-drawings/harness"; step=${1:?banked step, e.g. s37}
for job in 31130-01 31138-01 31065-01 31202-01 31168-01 31170-01-arch; do
  a="$H/pdf-only-$job-$step.e2k"; b="$H/pdf-only-$job/out.e2k"
  [ -f "$a" ] || { echo "$job: no banked $step"; continue; }
  [ -f "$b" ] || { echo "$job: NO MODEL ($(head -1 "$H/pdf-only-$job/console.txt" 2>/dev/null | cut -c1-100))"; continue; }
  if cmp -s "$a" "$b"; then echo "$job: byte-identical to $step"; continue; fi
  d=$(python docs/etabs-handoff/members_diff.py "$a" "$b" 2>&1)
  echo "$job: plates moved $(python docs/etabs-handoff/plate_diff.py "$a" "$b" mm 2>&1 | grep -c moved), columns $(echo "$d" | grep -cE 'LOST column|GAINED column') changed, walls $(echo "$d" | grep -c 'LOST wall') lost / $(echo "$d" | grep -c 'GAINED wall') gained$(echo "$d" | grep -oE '\(the second model sits [^)]*\)' | head -1 | sed 's/^/  /')"
done
