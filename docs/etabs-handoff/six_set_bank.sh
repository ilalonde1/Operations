#!/usr/bin/env bash
# Bank the six sets' current models AS a step, model and console together, so the next step can be
# diffed against them (six_set_diff.sh) and the stand-down counts that explain a diff are still there.
#
#     bash docs/etabs-handoff/six_set_bank.sh s42        # pdf-only-<job>/out.e2k -> pdf-only-<job>-s42.e2k
#                                                        # pdf-only-<job>/console.txt -> pdf-only-<job>-s42-console.txt
#
# Refuses to overwrite a step already banked (a step is banked once; re-banking would silently move
# the bar). Refuses a job whose out.e2k is missing (⛔ NO MODEL in the last run) rather than banking
# a stale one under a new name.
#
# Built 2026-09-11: steps 24–41 were banked by a hand-typed cp of the model only, and when the audit
# fixes returned 25 walls the s41 consoles that would have said which clause had taken them did not
# exist — one job had to be re-run with the fix switched off to find out.
set -u
H="$LOCALAPPDATA/Temp/kor-drawings/harness"; step=${1:?step to bank, e.g. s42}
banked=0
for job in 31130-01 31138-01 31065-01 31202-01 31168-01 31170-01-arch; do
  src="$H/pdf-only-$job/out.e2k"; dst="$H/pdf-only-$job-$step.e2k"
  [ -f "$src" ] || { echo "⛔ $job: no out.e2k to bank"; continue; }
  [ -f "$dst" ] && { echo "⛔ $job: $step already banked; not overwritten"; continue; }
  cp "$src" "$dst"
  [ -f "$H/pdf-only-$job/console.txt" ] && cp "$H/pdf-only-$job/console.txt" "$H/pdf-only-$job-$step-console.txt"
  echo "$job: banked $step ($(grep -c '^  AREAASSIGN' "$src") area assignments, $(grep -c '^  STORY ' "$src") storeys)"
  banked=$((banked + 1))
done
echo "$banked of 6 banked as $step"
