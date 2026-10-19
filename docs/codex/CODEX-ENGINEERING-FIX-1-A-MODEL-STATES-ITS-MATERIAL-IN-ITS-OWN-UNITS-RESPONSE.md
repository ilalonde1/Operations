# Engineering fix 1 response

Implemented in the three named code files.

- Extracted `E2kDocument.InchesPerUnit(string)` with the original five unit factors;
  `LengthUnitInInches()` delegates to it with its existing unknown-unit behaviour.
- The shell rejects unsupported units with `ArgumentException` naming the unit.
  Stress uses k², weight per volume uses k³, and all three values use invariant `G9`.
  The 120-inch default height uses `120 / k` and the existing `Trim()` formatter.
  U, A and GRADE stay unchanged; the material comment cites ETABS export 30783-01.
- Scaled every listed E2kDocument threshold (#3-#8) by model units per inch.
  `FloorUnder` and `NearestFloorBelow` receive that factor from the instance caller.
- Extended the existing differential test with feet and every existing comparison,
  material values in psi and lb/in³ at 1e-6 relative tolerance, and named storey
  elevations and floor-below elevations at 1e-4 inch tolerance.
- Added the unsupported-yard fact and the three exact inch-material-line assertions.
  Updated the coverage remarks, including the requested #4-#8 limitation.
- DXF fixture coordinates use invariant `G17` so feet serialization does not lose
  precision before the existing four-decimal inch comparisons.

No additional unscaled inch-length literals were identified in the permitted ranges.
No other production literals were changed. Baselines were not regenerated.

No build, restore or test was run; execution verification remains with Claude.
Before reading the brief, the initial parallel call ran `git status --short` and
searched for guidance filenames. No further git calls or repository searches ran.
