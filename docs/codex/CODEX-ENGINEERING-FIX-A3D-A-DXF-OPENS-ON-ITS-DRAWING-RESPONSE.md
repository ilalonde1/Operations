# Engineering fix A3d response

- Added the specified R12 VPORT table before LTYPE, with one `*ACTIVE` entry.
- Its centre uses the existing extents; height is
  `1.1 * max(extents height, extents width / 1.6)`, with aspect ratio 1.6.
- Numeric values use the existing invariant `Num` formatter.
  The comment explains A105's empty opening view and AutoCAD's use of `*ACTIVE`.
- Geometry, `$INSBASE` and extents are unchanged.
- Authored `ADxfOpensOnItsDrawingTests` for the requested offset slab,
  checking the single active entry, centre, minimum height and table order.

Only the exporter, the new test and this response were changed in this follow-up.
No build or test was run; verification remains with Claude.
