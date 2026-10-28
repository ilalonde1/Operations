#!/usr/bin/env bash
# Which of steps 149-168 fails the DXF-route tests on 31138 and 31168: run them with each listed knob group off.
# Args: one group per argument, knob numbers comma-separated, e.g. 149,150,151,152,153
cd "/c/VIsual Studio Projects/Operations" || exit 1
F="FullyQualifiedName~LiveProjectBaselineTests.TheModelStillCarriesTheSameStructure|FullyQualifiedName~ModelIntegrityTests.OpeningsAndHeadersAreAttachedToWhatTheyBelongTo|FullyQualifiedName~EveryDrawnMemberIsModelledOrAlreadyThere"
for g in "$@"; do
  envs=()
  for n in ${g//,/ }; do envs+=("KOR_STEP${n}_OFF=1"); done
  out=$(env "${envs[@]}" timeout 560 dotnet test Kor.Operations.EngineeringTools.Core.Tests --no-build -nologo --filter "$F" 2>&1)
  echo "== off $g: $(echo "$out" | grep -oE "Failed: +[0-9]+, Passed: +[0-9]+" | head -1)"
  echo "$out" | grep -E "^\s+Failed " -A2 | grep -vE "Error Message|^--" | sed -E 's/.*Tests\.//' | cut -c1-220
done
