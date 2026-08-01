# Codex prompt — single-click commit on BD typeahead controls

**Goal:** In the three BD search typeahead controls, make a single left-click on a
result row commit the selection — exactly what double-click / Enter does today.
Right now a single click only *highlights* the row; the user has to double-click,
which is unintuitive and silently leaves the parent window's "Generate" buttons
disabled with no feedback.

**Files (identical structure in all three):**
- `Kor.Operations.App/Controls/OrgSearchTypeahead.xaml` + `.xaml.cs`
- `Kor.Operations.App/Controls/PersonSearchTypeahead.xaml` + `.xaml.cs`
- `Kor.Operations.App/Controls/ProjectSearchTypeahead.xaml` + `.xaml.cs`

Each has a `ResultsList` `ListBox` inside a `Popup`, wired
`MouseDoubleClick="ResultsList_MouseDoubleClick"` and `KeyDown="ResultsList_KeyDown"`,
both routing to a private `SelectCurrent()` that commits `ResultsList.SelectedItem`
and raises the control's `*Selected` event. `SearchAsync` sets
`ResultsList.SelectedIndex = 0` after every search load.

**Change — add single-click-to-commit, mirrored in all three controls:**
- XAML: add `PreviewMouseLeftButtonUp="ResultsList_PreviewMouseLeftButtonUp"` to the
  `ResultsList` ListBox. Keep the existing `MouseDoubleClick` and `KeyDown` handlers.
- Code-behind: add `ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)`.
  Walk up the visual tree from `e.OriginalSource` (`VisualTreeHelper.GetParent`) looking
  for a `ListBoxItem`. If one is found, call `SelectCurrent()` and nothing else — WPF
  already set `SelectedItem` to the clicked row on mouse-down, and `SelectCurrent()`
  already type-guards `SelectedItem`. If no `ListBoxItem` ancestor is found (click landed
  on the scrollbar or empty space), do nothing.

**Constraints:**
- Do **NOT** wire `Selector.SelectionChanged` to `SelectCurrent()`. `SearchAsync` sets
  `SelectedIndex = 0` on every keystroke-driven reload, and Down-arrow keyboard nav also
  moves selection — either would auto-commit the wrong row while the user is still typing.
  The commit must be driven by an actual mouse-up on a row.
- Keep double-click and Enter working — don't remove or alter the existing handlers.
- Apply the same change to all three controls; each keeps its own row type and
  `*Selected` event (no shared refactor).
- Match the existing code style in these files (`#nullable enable`, naming, brace style).

Don't build or run — I'll verify locally.
