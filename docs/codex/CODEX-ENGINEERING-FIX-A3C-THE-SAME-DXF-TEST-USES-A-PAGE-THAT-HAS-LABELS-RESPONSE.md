# Engineering fix A3c response

- Regent retains its nonempty-polyline assertion and both equality assertions;
  removed only its assumption that markup text is present.
- Added `BothRoutesWriteTheSameMarkupLabels` for both colour-layer choices,
  comparing nonempty App labels with CLI labels and comparing polylines.
- The synthetic PDF uses the specified content stream and labelled polygon,
  six objects, and an xref/trailer sized from the actual object count.
- Updated the remarks to distinguish Regent shapes from synthetic shapes and labels.

Only the specified test file and this response were changed in this follow-up.
No build or test was run; verification remains with Claude.
