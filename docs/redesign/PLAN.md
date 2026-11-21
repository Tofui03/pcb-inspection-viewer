# Inspect UI plan

Direction: `01-calm-light.html` is the base. Errors use `02-control-room.html`'s full-width banner (red, amber for template fallback). A defect's detail uses `04-engineer-split.html` and stays closed until that defect is clicked.

This pass restyles Single Inspect and adds the banner. It does not recompute `board_disposition`. The word on screen is the server value.

This branch does not contain the capture host yet. That landed on `main` in #9 (`src/PcbInspectionViewer.App/CaptureFixtureHost.cs`, `scripts/capture-viewer-ui.ps1`, `.github/workflows/capture-ui.yml`). Implementation starts by rebasing onto `main`.

## 1. Screen spec

One primary button in the inspect side column. Its label is the only filled button. Test / Template / Diff stay as quiet text buttons, because `--cycle-layers` and `SelectLayerCommand` already depend on them. They are not a second primary action.

The banner is a single full-width bar under the title and above the tabs. Hidden when there is nothing to say. Red text sits on `#FF3B30`. Amber text sits on `#F5A524`.

| State | Seen first | Primary action | Where it goes |
| --- | --- | --- | --- |
| Empty | Blank page. One button. | Load images | Template picker, then test picker. Both paths set → Loaded. Cancelling a picker stays Empty. |
| Loaded | The test image on `DefectOverlayCanvas`, no boxes. File names only: `20085000_temp.png`, `20085000_test.png`. One quiet line: batch id and IPC class. | Inspect | Inspecting. F5 still fires this command. |
| Inspecting | The same image, with a 2px bar across the side column. | Cancel | Loaded. Esc still cancels. A successful response goes to Result without another click. |
| Result | The board with boxes, then the disposition as one word (`SCRAP` in the fixture). Rows: class, confidence (`0.92`, `0.82`, `0.75`), `IPC-A-600 2.2.2` / `2.2.1`. Detail pane closed. | Next | Empty. Clears the in-memory result and the paths. Does not delete the SQLite row. |

Footer under the tabs, 12px, muted: backend lamp text, batch id, IPC class, and stage-1 milliseconds when a result exists. No "SERVER BOARD DISPOSITION", no latency card, no per-defect paragraphs.

### Errors

| Error | Banner | What else is on screen | Primary action | Clears when |
| --- | --- | --- | --- | --- |
| Backend offline | Red. `Backend offline. The request failed after 3 attempts.` | Inspect stays on Empty or Loaded. No disposition word. | Retry | `MainShellViewModel.IsBackendOnline` becomes true. Retry calls the existing health check. If the backend is up and both images are set, it continues into Inspect. If it is still down, the banner stays. |
| Mismatched pair | Red. The existing sentence `Inspection API error (400): …` from `InspectViewModel.StatusOrErrorMessage`. | Loaded. Image shown, no boxes, no disposition. | Inspect | Either path changes, or the next inspect returns a result (`HasError` false). A repeat 400 leaves the banner up. |
| SqlGuard reject | Red. The existing sentence `Execution rejected by SqlGuard: …` from `AskViewModel.StatusMessage`. | Ask is otherwise unchanged. | The Ask screen's existing Run Query. This pass does not give Ask a new button. | The next Generate or Run replaces `StatusMessage`. |
| Template fallback | Amber. `Template fallback.` The disposition word does not change. | Result, detail pane still closed. | Next | Next clears the result, or a later result has `used_fallback` false. |

`IsLineHalted` reuses this same red banner with the text `LINE HALTED`. It does not get its own card. The fixture has `line_halted: false`, so the result capture does not show it. Offline wins if both are true.

Clicking a defect row, or a box on the canvas, sets the selection and opens the detail pane under the rows. The pane shows that defect only: severity, confidence, bbox, area, the full `IpcStandardClause`, electrical risk, root cause, and corrective action. Clicking the same defect again closes it. Default selection is none.

## 2. Code mapping

All inspect markup lives in `src/PcbInspectionViewer.App/MainWindow.xaml`. There is no per-tab view file. Do not add one.

| Path | This pass |
| --- | --- |
| `src/PcbInspectionViewer.App/MainWindow.xaml` | Replace the Single Inspect grid (the form column, the layer card, the verdict card). Add the banner. Title becomes `PCB Inspection Viewer` (drop the operator-station tagline). Inspect tab header becomes `Single Inspect`. Other tab headers stay. |
| `src/PcbInspectionViewer.App/MainWindow.xaml.cs` | Keep `BrowseAndAssignImage`. The Load images button calls it twice (template, then test). Drop the two `Browse…` click handlers only if nothing else calls them. Drag-drop and Ctrl+O stay, unlabeled. |
| `src/PcbInspectionViewer.App/App.xaml` | Merge the new dictionary. Converters stay registered. |
| `src/PcbInspectionViewer.App/Themes/CalmLight.xaml` | New. Resource dictionary only. |
| `src/PcbInspectionViewer.Core/ViewModels/InspectViewModel.cs` | New: selected defect index (null by default), `UsedTemplateFallback`, a clear command for Next, and a single command whose label is Load images / Inspect / Cancel / Next. `ShowRecordedResult` and `ApplyInspectionResult` stay the path that sets `VerdictText` from `BoardDisposition`. Read `used_fallback` from `UploadTriageResponseDto.Trace`. Do not invent a disposition. |
| `src/PcbInspectionViewer.Core/ViewModels/MainShellViewModel.cs` | Banner reads `IsBackendOnline` and `BackendStatusText`. No new health logic. |
| `src/PcbInspectionViewer.Core/ViewModels/AskViewModel.cs` | No change. The banner binds to the existing `StatusMessage` when it starts with `Execution rejected by SqlGuard`. |
| `src/PcbInspectionViewer.App/Controls/DefectOverlayCanvas.cs` | Placeholder fill uses the paper brush instead of `#0F172A`. A click that is not a drag hit-tests the box and raises the detection index. Pan and zoom stay. |
| `src/PcbInspectionViewer.App/Converters/VerdictConverters.cs` | Leave both converters. History and batch chips still use `VerdictToBrushConverter`. The inspect word binds to `VerdictText` directly and does not use `VerdictToDisplayLabelConverter` (that converter prefixes `✓` / `⚠` / `✕`). |
| `src/PcbInspectionViewer.App/CaptureFixtureHost.cs` | On `main`. Add `--capture-state` and `--select-defect`. Default remains today's result path. |
| `scripts/capture-viewer-ui.ps1` | On `main`. Loop the states below. Keep writing `viewer-inspect.png` for the result. |
| `.github/workflows/capture-ui.yml` | On `main`. No new workflow. It already runs on `cursor/**` pushes and ignores `docs/**`. |

Unchanged files: `HistoryViewModel.cs`, `DashboardViewModel.cs`, `DashboardService.cs`, `BatchInspectViewModel.cs`, `SettingsViewModel.cs`, `SqlGuard.cs`, `AskViewModel.cs`, `BatchStatusConverter.cs`, `GeneralConverters.cs`, `OnlineToStatusBrushConverter.cs`, `ExportService.cs`. Nothing is deleted.

`App.xaml.cs` already calls `CaptureFixtureHost.ApplyIfRequestedAsync` after `InitializeAsync`. That call stays.

## 3. Tokens

New dictionary `src/PcbInspectionViewer.App/Themes/CalmLight.xaml`, merged from `App.xaml`. Segoe UI. No font install. No third-party UI library: one would restyle every control, and this pass only changes Inspect plus one banner.

| Key | Value | Use |
| --- | --- | --- |
| `Brush.Paper` | `#FCFCFB` | Window |
| `Brush.Canvas` | `#F6F5F2` | Empty stage, side column |
| `Brush.Ink` | `#1C1B19` | Text, primary button fill |
| `Brush.OnInk` | `#FFFFFF` | Button label |
| `Brush.Muted` | `#6F6B64` | File names, footer, IPC column |
| `Brush.Line` | `#E4E0D8` | Hairline under tabs, between rows |
| `Brush.Scrap` | `#8D2F2F` | Disposition word when the value is SCRAP |
| `Brush.Pass` | `#1F7A4D` | Disposition word when PASS |
| `Brush.Rework` | `#8A5A12` | Disposition word when REWORK |
| `Brush.Alarm` | `#FF3B30` | Offline, bad pair, SqlGuard, line halted |
| `Brush.AlarmInk` | `#1A0000` | Text on the red banner |
| `Brush.Amber` | `#F5A524` | Template fallback |
| `Brush.AmberInk` | `#1A1204` | Text on the amber banner |

Type: disposition 48, body 14, quiet and footer 12, banner 13 bold. Spacing: page 16, side 24, row padding 9 vertical, banner padding 8 by 12. Primary button height 36, horizontal padding 18, corner radius 8. The 2px inspect bar uses `Brush.Ink` on `Brush.Line`.

History, batch, and dashboard chips keep the brushes inside `VerdictToBrushConverter`. They do not pick up `Brush.Scrap`.

## 4. Scope

Restyled: Single Inspect, the window title, and the shared banner.

Left as they are: Batch, History, Dashboard (the daily report), Ask (NL → SQL), and Settings. Ask gains the banner and nothing else. The instruction paragraph under the Ask title stays, because that screen is out of this pass.

Removed from the inspect surface only (properties stay on the view model so tests and the API request still work): the golden-template and test-image labels, both Browse buttons, the batch id and IPC editors, Clean-Board Mode, Colorize PCB, Simulate Rogue VLM, the stage-1 / round-trip card, the "IPC-A-600 Defect Decisions" heading, and the always-open risk / cause / action cards. Batch id and IPC class keep coming from `ApplySettingsDefaults`. Colorize remains editable on Settings, where it already is.

Layer names, zoom, and pan stay. "Show Defect Boxes" and "Reset Zoom (100%)" lose their labels' extra words: the checkbox text becomes `Boxes`, the button becomes `100%`.

## 5. Steps

Each step is one commit, rebased on `main`, and is done only if the step before it is green.

1. Rebase onto `main`. Proof: `.github/workflows/build-test.yml` on `windows-latest` (`dotnet test -c Release --filter "FullyQualifiedName!~IntegrationTests"`) passes. `.github/workflows/capture-ui.yml` still uploads `viewer-inspect.png` of the current layout, produced by `--fixture tests/fixtures/upload_triage_sample.json --window-size 1280x720`.

2. Add `Themes/CalmLight.xaml` and point the window background and a primary button style at it. Do not move the inspect layout. Proof: the same unit-test job passes with no test edits. A new capture PNG shows the old layout on `#FCFCFB`.

3. Rebuild the inspect tab to the empty / loaded / inspecting / result spec, including Next. Proof: existing `InspectViewModelTests` still pass, including `ShowRecordedResult_AppliesFixtureDispositionAndDefectsWithoutCallingApi` and the cancel test. Add one test that Next sets `HasResult` false and clears the disposition. Capture `viewer-inspect.png` shows the fixture SCRAP word, three rows, and the IPC-A-600 refs, and does not show `SERVER BOARD DISPOSITION`.

4. Add the banner and `--capture-state`. Values: `result` (default), `offline`, `pair`, `fallback`, `sql`. The script launches the published exe once per state and writes `viewer-inspect.png`, `viewer-error-offline.png`, `viewer-error-pair.png`, `viewer-error-fallback.png`, and `viewer-error-sql.png`. Proof: unit tests for "offline hides the disposition", "pair keeps `HasError` and does not set a verdict", and "fallback keeps `VerdictText` equal to `BoardDisposition`". The capture artifact contains all five PNGs. `build-test.yml` stays green.

5. Detail pane, closed by default. `--select-defect 0` opens it before the ready file is written. Proof: a unit test that the selected index starts null and that index 0 exposes the Short clause `IPC-A-600 Section 2.2.2 (Short / Bridging)`. Default `viewer-inspect.png` has no root-cause sentence. `viewer-inspect-detail.png` shows that sentence for defect 0. `build-test.yml` stays green.

`capture-ui.yml` ignores `docs/**` and `**.md`, so this plan does not trigger a capture. The first XAML commit does.

## 6. Open questions

1. Clean-Board Mode and Simulate Rogue VLM exist only on the inspect form. This plan takes them off that form and does not add them to Settings. Is that acceptable, or do they have to move in this pass?

2. With LLM provider `None` (the shipped default) and the fixture's `used_fallback: true`, the amber banner would show on every result. Should it appear only after a configured provider fails, or also when the provider is `None`?

3. `--capture-state` is a new flag on the existing host, and the script launches the exe once per PNG. Is one launch per state acceptable, or should one process emit all five PNGs before it signals ready?
