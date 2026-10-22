# Step 172 — a fill clipped to a slanted wall is that wall, not its box

## Written 2026-10-21, before the gate below has judged it.

**Knob:** `KOR_STEP172_OFF=1` restores the fill's own box as the wall (`GeometryFilterService`, the filled-wall rule).

**Target set:** 1 witness set, 31065-01 (the P2 parkade's west wall, P2 north sheet, read on P2, P1 and L1). Reach on the
corpus NOT yet known; the gate below measures it and this line is corrected with the figure.

## The witness

The P2 west wall runs 1.75 degrees off the page's axes along the property line, 300 mm thick (its two faces 3 mm apart
on the paper at 1:100). The hatch is drawn as an upright rectangle, 14.0 x 361.9 mm on the paper, shown only through a
129-segment clip that is the slanted band (page 16, path #27 clip / #28 fill). The reader took the fill's box: a vertical
wall 1,401 mm thick, 36 m long, on P2, P1 and L1 - 496 m3 where the wall is about 105.

The existing clip rule (`ClipPiecesOn`) cuts a wall ALONG its axis into piers; this clip narrows it ACROSS, and the
rule took it as one pier the whole length.

## What the change is

Where the fill's one clip is a band under three quarters of the box's thickness, at least 90% of its length, and no
thinner than the thinnest wall, the wall is the clip's band: its axis, its thickness. The fill's clip is the one drawn
immediately before it; failing that, the one clip whose box IS the fill's box. The P1 sheet (page 20) sets the clip
(#19, 8 points) and paints forty paths before the fill (#60): the first cut fixed P2 and missed P1, whose wall rises to
L1, and 193 m3 of 1,397 mm wall stayed on L1 until the second test was added.

## What must NOT move

- A wall with no clip, or a clip that cuts it into piers along its length (31168 p22's 74/118/41 in piers): the band is
  then as thick as the box and this does not apply.

## The measurement that decides it

1. 31065-01 whole: the P2/P1/L1 west wall 300 mm, slanted; nothing else moved but what 170/171 move.
2. The six-set gate and the corpus gate, `--bisect KOR_STEP172_OFF`.
