# Brochure Builder — Code Quality Remediation

## Overview

Audit of the brochure builder feature following the multi-step wizard rewrite. Covers
`BrochureBuilderViewModel.cs`, `BrochureBuilderWindow.xaml`, `BrochureBuilderWindow.xaml.cs`,
`BrochureRenderer.cs`, and all `Core/Models/Brochure/` model files. Audit date: 2026-03-17.
No critical issues found.

---

## Priority Fix List

### [Priority 7] MEDIUM — ProjectTab_Click splits editing state
**File:** `BrochureBuilderWindow.xaml.cs` — `ProjectTab_Click`
**Issue:** Code-behind sets `IsEditingProject = true` and then calls `EditProjectCommand`, which also sets it. `SelectedProjectIndex` is set in code-behind but not in the command. The editing entry point is fragmented across two places.
**Fix:** Remove `IsEditingProject = true` from code-behind. Move `SelectedProjectIndex` assignment into `EditProjectCommand`. Code-behind should only identify which project was clicked and call the command.
**Status:** [ ] Open

---

### [Priority 8] MEDIUM — ConfigureAwait(false) missing in renderer
**File:** `BrochureRenderer.cs` — `RenderPreviewAsync`
**Issue:** Library async methods capture the calling synchronization context. This can cause deadlocks in non-WPF host contexts such as unit tests or future API use.
**Fix:** Add `.ConfigureAwait(false)` to all `await` calls in `BrochureRenderer` async methods.
**Status:** [ ] Open

---

### [Priority 9] MEDIUM — Missing FallbackValue on nested bindings
**File:** `BrochureBuilderWindow.xaml` — `Step3Panel`
**Issue:** Bindings such as `SelectedBlock.Section.Heading` log binding errors when `SelectedBlock` is null. This causes debug noise and potential display flicker when no block is selected.
**Fix:** Add `FallbackValue=""` to all text bindings that traverse `SelectedBlock`. Ensure the null-state hint panel is visible before the editor panels attempt to bind.
**Status:** [ ] Open

---

### [Priority 10] LOW — Magic number in ComposeContactPage
**File:** `BrochureRenderer.cs` — `ComposeContactPage`
**Issue:** Column gap `0.3f` is inline, inconsistent with the named constants used throughout the rest of the renderer.
**Fix:** Extract `private const float ContactColumnGapInches = 0.3f;`.
**Status:** [ ] Open

---

### [Priority 11] LOW — Cover year not configurable
**File:** `BrochureRenderer.cs` + `BrochureContent.cs`
**Issue:** `DateTime.Now.Year` is hardcoded on the cover page. A brochure generated in December for next-year distribution will display the wrong year.
**Fix:** Add `CoverYear` (`int?`) to `BrochureContent`. The renderer falls back to `DateTime.Now.Year` if null. Add an optional year field to the Step 1 UI.
**Status:** [ ] Open

---

### [Priority 12] LOW — Model classes not sealed
**File:** `BrochureBlock.cs`, `BrochureSection.cs`, `BrochureProject.cs`, `BrochurePerson.cs`, `BrochureOverviewSection.cs`
**Issue:** Plain data classes not intended for inheritance are not `sealed`, inconsistent with `BrochureContent` which is `sealed`.
**Fix:** Add the `sealed` modifier to all five classes.
**Status:** [ ] Open

---

### [Priority 13] LOW — ResolveLogoPath trivial wrapper
**File:** `BrochureRenderer.cs` — `ResolveLogoPath`
**Issue:** `ResolveLogoPath` only calls `ResolvePath` with no additional logic — pointless indirection.
**Fix:** Delete `ResolveLogoPath` and call `ResolvePath` directly in `ResolveDocumentAssets`.
**Status:** [ ] Open

---

### [Priority 14] LOW — _editingBlock not tracked alongside _editingProject
**File:** `BrochureBuilderViewModel.cs` — `SaveEditCommand`
**Issue:** The block containing `_editingProject` is found by LINQ search on every save. The reference could be stored directly when entering edit mode.
**Fix:** Add a `private BrochureBlock? _editingBlock` field. Set it alongside `_editingProject` in `EditProjectCommand`. Use it directly in `SaveEditCommand` and clear it in `CancelEditCommand`.
**Status:** [ ] Open

---

## Completed

### [Priority 1] HIGH — EditPersonButton_Click UI staleness
**File:** `BrochureBuilderWindow.xaml.cs` — `EditPersonButton_Click`
**Issue:** `_viewModel.SelectedBlock.People.Remove(person)` removes from `List<BrochurePerson>` directly. `People` is not an `ObservableCollection` so the `ItemsControl` does not update. The person stays visible in the list while being edited in the form.
**Fix:** Move person edit logic to the ViewModel. Add `BeginEditPersonCommand` that loads form fields, removes the person from the list, and calls `RefreshBlock`. Code-behind calls the command only.
**Status:** [x] Complete

---

### [Priority 2] HIGH — async void ProduceBrochureCommand
**File:** `BrochureBuilderViewModel.cs` — `ProduceBrochureCommand`
**Issue:** The `RelayCommand` lambda is `async void`. Unhandled exceptions after the first `await` propagate to `Dispatcher.UnhandledException`. The inner try/catch mitigates the current code, but future edits outside that block will silently crash the dispatcher.
**Fix:** Introduce `AsyncRelayCommand` that accepts `Func<object?, Task>` and schedules execution via `Dispatcher.InvokeAsync`.
**Status:** [x] Complete

---

### [Priority 3] MEDIUM — Preview page labels start at 0
**File:** `BrochureBuilderWindow.xaml` — `PreviewSidebar` thumbnail strip
**Issue:** `AlternationIndex` is zero-based so thumbnails are labeled "Page 0", "Page 1", etc.
**Fix:** Add `AddOneConverter` (`IValueConverter`, returns `(int)value + 1`) and apply it to the page label binding.
**Status:** [x] Complete

---

### [Priority 4] HIGH — RefreshBlock fires excessive notifications
**File:** `BrochureBuilderViewModel.cs` — `RefreshBlock`
**Issue:** `RemoveAt` and `Insert` each fire `Blocks_CollectionChanged` (9 notifications each). Eight explicit `OnPropertyChanged` calls follow, five of which duplicate what the handler already fired. Every add/edit triggers 17+ binding re-evaluations.
**Fix:** Add a `_suppressCollectionNotifications` bool flag. Set `true` before `RemoveAt`, `false` after `Insert`. Check the flag in `Blocks_CollectionChanged` and return early if set. Fire one targeted batch of notifications after `Insert`.
**Status:** [x] Complete

---

### [Priority 5] HIGH — Double image I/O on generate
**File:** `BrochureRenderer.cs` + `BrochureBuilderViewModel.cs` — `ProduceBrochureCommand`
**Issue:** `ProduceBrochureCommand` calls `RenderAsync` then `RenderPreviewAsync`. Both call `ResolveDocumentAssets` independently, reading all images from disk twice on every generate.
**Fix:** Expose a single `RenderWithPreviewAsync` method that generates the PDF and preview images in one pass, reusing the same resolved assets.
**Status:** [x] Complete

---

### [Priority 6] HIGH — SelectedSection incorrectly reassigned in RefreshBlock
**File:** `BrochureBuilderViewModel.cs` — `RefreshBlock`
**Issue:** `SelectedSection = block.Section` runs unconditionally for any section block refresh. If `RefreshBlock` is called for a block other than the one currently being edited, `SelectedSection` is silently redirected to the wrong section.
**Fix:** Only reassign `SelectedSection` if `_selectedSection` is `null` or `ReferenceEquals(block.Section, _selectedSection)`.
**Status:** [x] Complete

---

---

## Notes

- Audit performed against the brochure builder feature as built through the wizard rewrite (Steps 1–4, all block types, preview sidebar).
- No critical issues found.
- The async void risk (#2) is partially mitigated by the comprehensive try/catch in the current lambda body.
- Image I/O double-read (#5) affects generation time only, not correctness.
