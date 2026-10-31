# CODEX — Halifax v4 adversarial audit 3 of 3: GATES THAT CANNOT FAIL

Reading set: about 100 KB of source (measured 30 Oct at 8dcaeeb). Reasoning: medium. Expected time: under 20 minutes.
Repo: `C:\VIsual Studio Projects\KOR.Drafter`, commit `8dcaeeb` (branch standard-details-rendered-image-store; the
working tree is at that commit). Read the files as they are in that tree.

## Paste this to Codex

You are auditing the CHECKS of a pipeline that turns an engineer's marked-up PDF into Revit and ETABS models and a
drawing set. Every check below reported PASS on a package that the engineer then found faults in within minutes. Your
job is to find checks that cannot fail, or cannot fail on the fault they are named for. Assume the author is confident
and mistaken. No praise, no summary, no refactors.

THE QUESTION: which gates in these lines would pass on a wrong drawing set, and on what concrete wrong input?

The class we already know (so you can find more of it, not re-report it):
- A check that compares our output with our own input in the same frame agreed with a shared mistake: pad footings were
  sent to Revit 90 degrees off, and the angle and footprint checks passed because they read the requests the same way the
  writer wrote them. The fix added a check on Revit's own readback.
- A markup check fitted each sheet from words equal to a grid name; two extra "1"s in a note sheared the fit and it read
  the engineer's dimensions off our notes.
- A dimension-placement step approximated text by a box while the check measured real glyphs, so a leader could clip
  its own text and the placer thought it clear.

Open ONLY these files, ONLY these line ranges. Do not grep, list, search or open anything else. Do not build or run tests.
| file (tools/) | lines |
|---|---|
| virtual_drafter.py | 413-551 (stage_revit and its gates) |
| virtual_drafter.py | 742-980 (plot_checks, stage_gates) |
| virtual_drafter.py | 1409-1520 (check_plot) |
| plot_vs_markup.py | all |
| settle_dims.py | all |

Hunt specifically for:
1. A gate whose expected value comes from the same function, file or run as the value it checks.
2. A gate that passes when its input is empty, missing, or unreadable (0 == 0, "no findings" from no data, a
   try/except that turns an error into a pass, a sample or `head` standing in for the whole).
3. "Known" or allow-lists (job.json known_differences, stated counts) that can absorb a NEW fault of the same shape.
   Say how a new fault would be counted as known.
4. Tolerances, pads and thresholds wide enough to pass the fault the gate is named for. Give the fault they admit.
5. Gate order: a later stage or a resume path that skips a gate an earlier run would have stopped on.
6. A registration or fit (page to model, plot to markup) that can be pulled off by ordinary drawing content, as the grid
   fit was. Say what content pulls it and what the gate then misreads.

For each finding give: file:line, the gate's printed name, the concrete wrong input, why it passes, and a severity
(CRITICAL = a wrong set ships with every gate green; MAJOR = possible on another job; MINOR).
If you find nothing in a category, write the category and "none found in the lines read", nothing more.

Write your answer to
`C:\VIsual Studio Projects\Operations\docs\codex\CODEX-HALIFAX-V4-AUDIT-3-GATES-THAT-CANNOT-FAIL-RESPONSE.md`,
at most 80 lines. Do not edit any other file.
