# Engineering fix A2 response

- Ordinary mid-line title-block labels now require an attached or following colon.
  The comment cites A105's “DO NOT SCALE THE DRAWINGS.” sentence.
  Line-start labels and letter-spaced runs retain their existing behaviour.
- Moved the intake's stated-scale decision into public
  `SheetScaleReader.StatedDenominator(PageContent? page)`, preserving its conflict
  check, field fallback, conversion and denominator bounds.
  `DrawingIntake.StatedScaleDenominator` remains an internal one-line forward.
- The App tries the shared decision first and returns `TitleBlock` with the field's
  text as its note (falling back to the existing scale note when no field exists).
  The subsequent caption and conflict logic is unchanged.
- Added the three requested gates using the existing synthetic-page helpers;
  the mid-line gate covers both attached and separate colon tokens.

Review note: the existing `AnOfficeLabelEndsTheTitleBeforeTheNextFields` fixture
has a colonless mid-line `DRAWING NUMBER` (original line 35). The new rule rejects
that boundary, so its `PROJ. #` assertion may need revision. That fixture is unchanged.

No build or test was run; the authored gates await Claude's verification.
No git or repository search was run. Baselines were not modified.
