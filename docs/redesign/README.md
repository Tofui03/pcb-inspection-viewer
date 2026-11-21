# Redesign prototypes

Four static directions for the WPF operator station. No app code changes. Open a file in a browser at 1280×720. The black **PROTOTYPE** bar is not part of the product: it jumps the Single Inspect flow (empty → images loaded → inspecting → result) and the four error states. **Notes** states, in plain words, what is seen first, the one primary action, where that action goes, and how errors look.

| File | Direction |
| --- | --- |
| [01-calm-light.html](01-calm-light.html) | Calm light. Linear / Apple Notes. |
| [02-control-room.html](02-control-room.html) | Dark industrial control room. |
| [03-verdict-station.html](03-verdict-station.html) | Verdict-first operator station. |
| [04-engineer-split.html](04-engineer-split.html) | Dense split pane for an engineer. |

All four use the fixture `UPLOAD:20085000_test`: batch `BATCH-FIXTURE`, Class 2, disposition `SCRAP`, Short 0.92, Open 0.82, Open 0.75, IPC-A-600 §2.2.2 and §2.2.1, stage-1 86.3 ms. The client shows that disposition verbatim. Other tabs are sketches on the same nav: Batch, History, Dashboard, Ask, Settings.

## Comparison

| | 01 Calm light | 02 Control room | 03 Verdict station | 04 Engineer split |
| --- | --- | --- | --- | --- |
| Seen first | The board. SCRAP is one word beside it. | Status lamps, then the monitor, then SCRAP. | The disposition, full screen. | The board and the selected defect, side by side. |
| Primary action | One black button: Load images, then Inspect, then Next. | One RUN key. NEXT after a result. | One full-width bar: LOAD, INSPECT, NEXT. | One toolbar button: Load images, Inspect, then Next board. |
| Flow | Load shows the pair. Inspect runs the pass and lands on SCRAP. Next clears the board. | RUN inspects and shows SCRAP. NEXT clears the monitor. | LOAD, then INSPECT, then SCRAP, then NEXT. | Inspect fills the property pane from the fixture decision. Next board clears both panes. |
| Error style | One sentence in the side margin. | Full-width alarm. Offline turns the lamp red. | The huge word becomes OFFLINE, PAIR, or REJECT. FALLBACK keeps SCRAP. | Status-bar sentence. Ask replaces the grid with the SqlGuard rejection. |
| Strengths | Closest to "cut the chrome." The board stays primary and the form disappears. Easy to scan on a day shift. | Readable across a line at a glance. Offline and a bad pair cannot be missed. Lamps match the status bar the app already has. | A gloved operator gets the disposition without reading. Touch targets are the whole bar. One board, one decision. | Keeps the data the current right-hand card already shows: bbox, confidence, IPC clause, risk, cause, action. Ask still generates SQL, then runs it, so SqlGuard review stays a step. |
| Weaknesses / risks | Easy to hide a line-down or a backend outage in a quiet sentence. IPC text and root cause are one click away from gone; an engineer will ask for them back. | High contrast fatigue on a long shift. Amber-on-black can look like an alarm when nothing is wrong. Dense lamps compete with SCRAP if the rack grows. | Hides the image, the clause, and the defect list. A Short and an Open become one red word. Wrong for anyone who must explain the call. | Still a lot of type. Closest to today's density, so it may not answer "this looks ten years old" if the only change is a tighter grid. The toolbar can grow back into the old form. |
| WPF effort | New `Inspect` layout (image + side word, no form column). Custom `Button` and `TabControl` templates to leave the default chrome. History and Batch grids get the same type ramp. Medium: views and a resource dictionary, view models stay. | Same layout work plus a dark brush set, lamp header bound to `IsBackendOnline` / `LlmProviderStatusText`, and high-contrast `DataGrid` row templates. Medium-high: WPF selection and grid chrome fight dark themes. | Replace the three-column inspect view with a single disposition and a 96px action. `DefectOverlayCanvas` becomes a thumbnail. Other tabs keep their data and drop to a few large rows. Medium: mostly XAML, little view-model change. Touch sizing needs a pass on every tab. | Lowest layout delta. Move load/inspect into a toolbar, keep the overlay, and retarget the existing decision fields into a property pane. Ask keeps Generate then Run. Still needs control templates or the grey chrome remains. Low-to-medium. |

## Errors the bar can show

- **Offline.** `Inspection API error (503): Backend request failed after 3 attempts.` Status text matches `MainShellViewModel` ("Backend offline").
- **Bad pair.** `Inspection API error (400): template and test images differ in size.` Same shape as `InspectViewModel` (`Inspection API error ({status}): {detail}`).
- **Fallback.** Provider `None`. Verdict stays `SCRAP`. Status matches `LLM: None (Template fallback)`.
- **SqlGuard.** Question that targets `Inspections`. Rejection: access to that table is not allowed; only `v_inspections`, `v_defects`, and `v_batches`.

## What this is not

These files do not call the API, open SQLite, or change `MainWindow.xaml`. Pick a direction before any XAML work.
