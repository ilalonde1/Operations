# CODEX RESPONSE — audit of intake steps 131, 133, 134, 135 (2026-09-23, codex-cli 0.156.0)

Brief: CODEX-INTAKE-STEPS-131-135-AUDIT.md. Read-only, the code on stdin. Findings worst-first, with the geometry.


The strongest counterexample is in step 135: its “over” test uses the average of boundary vertices, which can lie outside a concave floor. A schedule rectangle in that empty space can qualify to replace the floor. The other concrete failure cases involve projected lower-level outlines, dimension lines, and short beam or reinforcement strokes admitted as edge geometry.

1. **Step 135: a completely disjoint schedule can replace a real floor. Test this first.**

   Coordinates below are metres. Give the walk this C-shaped floor:
   `(0,0), (60,0), (60,20), (20,20), (20,40), (60,40), (60,60), (0,60)`.
   Its area is **2,800 m²**, but its vertex average is `(35,30)`—inside the empty notch.

   Put a schedule rectangle at `(30,24)`–`(40,36)`, wholly inside that notch. Its area is 120 m². Let the real floor hold eight real columns and the schedule hold eight symbols misread as columns. Assume the arrangement recovers the schedule but fails to recover the real outline.

   The new outside-column condition runs the arrangement. `Over(schedule, walk)` returns **true despite zero intersection**, because the walk’s vertex average lies inside the schedule. Equal held-column counts then remove the walk floor. The resulting loss is **28,847 ft² per affected floor**.

   **Gates:** Both rings can exceed minimum area; scale the fixture if necessary. The schedule border is entirely drawn, so invention share is zero and its boundary has no loose ends. A slab-detail schedule can contain a thickness label. Misread symbols supply apparent structural support—the supplied comments already document that failure class. The missing function needed to settle final acceptance is `StandsIn`.

   **Cheapest experiment:** Feed these two polygons and sixteen column centres directly into the stand-down block, with the walk in `walkFloorsToReplace` and only the schedule in `fromArrangement`. Assert that a disjoint polygon cannot remove the walk. The shown code fails that assertion.

   **Four false columns alone also matter:** With eight real columns held and four schedule symbols outside, step 135 triggers where the old half-count test did not. An accepted schedule floor is then added even if it cannot replace the walk.

2. **Step 131: a short beam or reinforcement stroke can complete a projected lower-level outline.**

   Structural drafters put **beam stubs, corbel/shear-head arms, column-centred reinforcement bars and tendon segments** on grid axes. A 600 mm stroke with a different pen is not evidence of a slab perimeter. The classifier’s own comment explicitly identifies heavier grid strokes as tendons or beams. Moreover, the shown test is `!IsGridPen(...)`; it does not independently require a heavier pen.

   Construct a projected **40 × 30 m podium perimeter** around a current **12 × 20 m tower floor**. Leave a 1,800 mm interruption in one straight perimeter side lying on a grid axis. Centre a 600 mm beam stub in that interruption, leaving 600 mm at each end.

   Step 131 promotes that stub into `pieces`. Both gaps now fall below the stated 1,219 mm inline extension limit. Without its promotion, the uninterrupted gap between eligible pieces exceeds that limit. This can turn the projected podium into the current floor: **10,333 ft² excess per storey**.

   **Gates:** Minimum area passes. The enlarged ring contains the tower’s real columns and legitimate thickness note. Its boundary closes. Only 1.2 m of a roughly 140 m perimeter is invented—about **0.86%**; an invention-share limit would need to reject that amount under its actual accounting. Structural support inside the ring does not identify which level its perimeter depicts. No shown gate guarantees rejection; final acceptance needs `StandsIn`.

   **Cheapest experiment:** Run this fixture with `KOR_STEP131_OFF` on and off, retaining the 600 mm stroke in both runs. Compare recovered area and invented segments. Also try 299 mm and 300 mm stubs, adjusting the two gaps to keep the total interruption fixed.

3. **Step 133: a 190 mm jog can attach the floor to an external dimension frame.**

   Use `lineMinLengthMm = 250`. Draw three sides of a 30 × 30 m floor, with the upper edge ending at **A = `(30,30)`**. Add a 190 mm vertical stroke from A to **B = `(30,30.19)`**, using the upper edge’s pen.

   At B, start a thinner, same-colour dimension baseline running to `(50,30.19)`. Its associated drafting frame runs down to `(50,0)` and back to the floor’s bottom-right endpoint `(30,0)`. Supply these as untagged stroked paths.

   The short stroke now passes `JogsBetweenLongLines`: its own pen matches at A, and any pen of the same colour suffices at B. The resulting ring encloses **1,503.8 m² instead of 900 m²**, approximately **6,500 ft² excess**. The two horizontal lines flanking the jog are offset, so this is not simply an inline gap.

   **Gates:** Every boundary segment is drawn; invention share and loose boundary ends offer no protection. Minimum area passes, and the ring contains the real floor’s columns and thickness note. Annotation/furniture classification could prevent admission, but this function does not establish that the far line is structural. Final acceptance needs `StandsIn`.

   **Cheapest experiment:** First assert that the jog is retained only with step 133 enabled. Then arrange the fixture with bridge tolerance below 190 mm, ensuring an ordinary bridge cannot independently close the gap. Compare recovered area.

4. **Step 135: a second, wall-supported floor with three columns remains invisible.**

   Put two floor views on a sheet. The walk closes the first, holding twenty columns. The second is a **20 × 20 m wall-supported slab with three columns**, whose interrupted outline needs arrangement bridges.

   `held = 20`, total columns `= 23`. Neither condition fires:
   `40 < 23` is false, and three outside columns are fewer than four. The arrangement remains skipped. Potential loss: **4,306 ft² per represented floor**. Two views with fewer than six total columns also never enter this recovery condition.

   **Gates:** Minimum area, thickness and structural support cannot rescue a floor whose arrangement is never built. The missing function needed to establish that this second ring would otherwise qualify is `StandsIn`.

   **Cheapest experiment:** Run the same two-view fixture with three and then four columns in the second view, without changing its outline, walls or thickness note. A floor appearing solely when the fourth column is added demonstrates the blind spot.

5. **Step 134: the independent T can close a large outline against a dimension extension.**

   Use a 150 mm bridge tolerance and a join tolerance below 40 mm. Let a horizontal upper boundary end at **P = `(39.9,30)`**. A dimension extension at **x = 40**, extending from y = 0 to 35, provides the wrong right-hand boundary.

   Add an endpoint **Q = `(39.9,30.04)`**, whose line runs horizontally left. P and Q have a mutually preferred 40 mm end-to-end bridge. P also has a 100 mm perpendicular T to the dimension extension.

   With step 134 off, the cheaper P–Q proposal suppresses that T. With it on, both are selected. Given drawn left and bottom boundaries, the T closes the large rectangle against the dimension line. For a real 30 × 30 m floor beneath this 40 × 30 m projected/drafting outline, the excess is **3,229 ft²**.

   **Gates:** The T code checks distance, an interior perpendicular foot, uniqueness and conflicts. It does **not** check the target’s structural meaning or require continuation along the incoming edge’s direction. The candidate ring exceeds minimum area, contains real columns and a thickness note, and has a closed boundary. The principal invented closure is just 100 mm. An isolated dimension tick cannot create a large floor by itself, but it can supply the final connection to existing linework. A door swing is a candidate only if its segments reach this mesh.

   **Cheapest experiment:** Run this mesh through `PlanarRings` with step 134 off/on and inspect the selected bridges and large bounded face. Then test acceptance; the missing function needed for that verdict is `Holds`. No semantic veto against the dimension extension appears in `Bridges`.
tokens used
33,250
1. **Step 135: a completely disjoint schedule can replace a real floor. Test this first.**

   Coordinates below are metres. Give the walk this C-shaped floor:
   `(0,0), (60,0), (60,20), (20,20), (20,40), (60,40), (60,60), (0,60)`.
   Its area is **2,800 m²**, but its vertex average is `(35,30)`—inside the empty notch.

   Put a schedule rectangle at `(30,24)`–`(40,36)`, wholly inside that notch. Its area is 120 m². Let the real floor hold eight real columns and the schedule hold eight symbols misread as columns. Assume the arrangement recovers the schedule but fails to recover the real outline.

   The new outside-column condition runs the arrangement. `Over(schedule, walk)` returns **true despite zero intersection**, because the walk’s vertex average lies inside the schedule. Equal held-column counts then remove the walk floor. The resulting loss is **28,847 ft² per affected floor**.

   **Gates:** Both rings can exceed minimum area; scale the fixture if necessary. The schedule border is entirely drawn, so invention share is zero and its boundary has no loose ends. A slab-detail schedule can contain a thickness label. Misread symbols supply apparent structural support—the supplied comments already document that failure class. The missing function needed to settle final acceptance is `StandsIn`.

   **Cheapest experiment:** Feed these two polygons and sixteen column centres directly into the stand-down block, with the walk in `walkFloorsToReplace` and only the schedule in `fromArrangement`. Assert that a disjoint polygon cannot remove the walk. The shown code fails that assertion.

   **Four false columns alone also matter:** With eight real columns held and four schedule symbols outside, step 135 triggers where the old half-count test did not. An accepted schedule floor is then added even if it cannot replace the walk.

2. **Step 131: a short beam or reinforcement stroke can complete a projected lower-level outline.**

   Structural drafters put **beam stubs, corbel/shear-head arms, column-centred reinforcement bars and tendon segments** on grid axes. A 600 mm stroke with a different pen is not evidence of a slab perimeter. The classifier’s own comment explicitly identifies heavier grid strokes as tendons or beams. Moreover, the shown test is `!IsGridPen(...)`; it does not independently require a heavier pen.

   Construct a projected **40 × 30 m podium perimeter** around a current **12 × 20 m tower floor**. Leave a 1,800 mm interruption in one straight perimeter side lying on a grid axis. Centre a 600 mm beam stub in that interruption, leaving 600 mm at each end.

   Step 131 promotes that stub into `pieces`. Both gaps now fall below the stated 1,219 mm inline extension limit. Without its promotion, the uninterrupted gap between eligible pieces exceeds that limit. This can turn the projected podium into the current floor: **10,333 ft² excess per storey**.

   **Gates:** Minimum area passes. The enlarged ring contains the tower’s real columns and legitimate thickness note. Its boundary closes. Only 1.2 m of a roughly 140 m perimeter is invented—about **0.86%**; an invention-share limit would need to reject that amount under its actual accounting. Structural support inside the ring does not identify which level its perimeter depicts. No shown gate guarantees rejection; final acceptance needs `StandsIn`.

   **Cheapest experiment:** Run this fixture with `KOR_STEP131_OFF` on and off, retaining the 600 mm stroke in both runs. Compare recovered area and invented segments. Also try 299 mm and 300 mm stubs, adjusting the two gaps to keep the total interruption fixed.

3. **Step 133: a 190 mm jog can attach the floor to an external dimension frame.**

   Use `lineMinLengthMm = 250`. Draw three sides of a 30 × 30 m floor, with the upper edge ending at **A = `(30,30)`**. Add a 190 mm vertical stroke from A to **B = `(30,30.19)`**, using the upper edge’s pen.

   At B, start a thinner, same-colour dimension baseline running to `(50,30.19)`. Its associated drafting frame runs down to `(50,0)` and back to the floor’s bottom-right endpoint `(30,0)`. Supply these as untagged stroked paths.

   The short stroke now passes `JogsBetweenLongLines`: its own pen matches at A, and any pen of the same colour suffices at B. The resulting ring encloses **1,503.8 m² instead of 900 m²**, approximately **6,500 ft² excess**. The two horizontal lines flanking the jog are offset, so this is not simply an inline gap.

   **Gates:** Every boundary segment is drawn; invention share and loose boundary ends offer no protection. Minimum area passes, and the ring contains the real floor’s columns and thickness note. Annotation/furniture classification could prevent admission, but this function does not establish that the far line is structural. Final acceptance needs `StandsIn`.

   **Cheapest experiment:** First assert that the jog is retained only with step 133 enabled. Then arrange the fixture with bridge tolerance below 190 mm, ensuring an ordinary bridge cannot independently close the gap. Compare recovered area.

4. **Step 135: a second, wall-supported floor with three columns remains invisible.**

   Put two floor views on a sheet. The walk closes the first, holding twenty columns. The second is a **20 × 20 m wall-supported slab with three columns**, whose interrupted outline needs arrangement bridges.

   `held = 20`, total columns `= 23`. Neither condition fires:
   `40 < 23` is false, and three outside columns are fewer than four. The arrangement remains skipped. Potential loss: **4,306 ft² per represented floor**. Two views with fewer than six total columns also never enter this recovery condition.

   **Gates:** Minimum area, thickness and structural support cannot rescue a floor whose arrangement is never built. The missing function needed to establish that this second ring would otherwise qualify is `StandsIn`.

   **Cheapest experiment:** Run the same two-view fixture with three and then four columns in the second view, without changing its outline, walls or thickness note. A floor appearing solely when the fourth column is added demonstrates the blind spot.

5. **Step 134: the independent T can close a large outline against a dimension extension.**

   Use a 150 mm bridge tolerance and a join tolerance below 40 mm. Let a horizontal upper boundary end at **P = `(39.9,30)`**. A dimension extension at **x = 40**, extending from y = 0 to 35, provides the wrong right-hand boundary.

   Add an endpoint **Q = `(39.9,30.04)`**, whose line runs horizontally left. P and Q have a mutually preferred 40 mm end-to-end bridge. P also has a 100 mm perpendicular T to the dimension extension.

   With step 134 off, the cheaper P–Q proposal suppresses that T. With it on, both are selected. Given drawn left and bottom boundaries, the T closes the large rectangle against the dimension line. For a real 30 × 30 m floor beneath this 40 × 30 m projected/drafting outline, the excess is **3,229 ft²**.

   **Gates:** The T code checks distance, an interior perpendicular foot, uniqueness and conflicts. It does **not** check the target’s structural meaning or require continuation along the incoming edge’s direction. The candidate ring exceeds minimum area, contains real columns and a thickness note, and has a closed boundary. The principal invented closure is just 100 mm. An isolated dimension tick cannot create a large floor by itself, but it can supply the final connection to existing linework. A door swing is a candidate only if its segments reach this mesh.

   **Cheapest experiment:** Run this mesh through `PlanarRings` with step 134 off/on and inspect the selected bridges and large bounded face. Then test acceptance; the missing function needed for that verdict is `Holds`. No semantic veto against the dimension extension appears in `Bridges`.
