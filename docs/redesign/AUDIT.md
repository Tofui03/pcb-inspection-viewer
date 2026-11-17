# UI audit

Source: `MainWindow.xaml` (every tab is in this one window), the tab view models, `InspectionEntities`, SQLite views `v_inspections` / `v_defects` / `v_batches`, and `StationSettings`. `docs/viewer-inspect.png` is not in the repo; the XAML is the source of truth.

The window is already a light slate (`#F8FAFC`) with a few rounded cards. It still reads as default WPF: stock buttons, text boxes, combo boxes, date pickers, and data grids, each with its own label. Verdict colors exist (`PASS` `#10B981`, `REWORK` `#F59E0B`, `SCRAP` `#EF4444`) but the disposition is a 24px word under the label "SERVER BOARD DISPOSITION".

Fixture used below: `tests/fixtures/upload_triage_sample.json`. `UPLOAD:20085000_test`, batch `BATCH-FIXTURE`, IPC Class 2, board disposition `SCRAP`, line not halted, stage-1 86.3 ms. Three defects, all `CRITICAL` / `SCRAP`: Short 0.92 at (177,101)–(226,196) citing IPC-A-600 Section 2.2.2; Open 0.82 and Open 0.75 citing Section 2.2.1. Stage 3 ran with no external VLM (`used_fallback: true`). The client must show `board_disposition` verbatim and must not recompute it.

## Single Inspect (F1)

Today: a 320px form, the image, a 380px verdict card. The form is golden-template browse, clean-board checkbox, test-image browse, batch id, IPC class, colorize, simulate-rogue-VLM, then Inspect (F5) and Cancel (Esc). The image has Test / Template / Diff, defect boxes, and reset zoom. The card has the disposition, a line-halted banner, stage-1 and round-trip, then one card per defect with severity, bbox, IPC clause, electrical risk, root cause, corrective action, and guardrail override.

Operator needs: the board, the disposition, and which defects forced it. One action: inspect this pair, then the next board.

Noise: two browse buttons plus a drop target before the only action that matters. Clean-board, colorize, and rogue-VLM are station setup, not the pass. Per-defect paragraphs repeat the same Open text twice. Bbox coordinates and both latency numbers are engineer telemetry.

## Batch (F2)

Today: folder path, DeepPCB auto-pair checkbox, global template path, batch id, IPC class, concurrency, colorize, Start and Cancel, a progress sentence (done / failed / total / elapsed / ETA), then a grid of index, file name, status (`Queued` `Running` `Completed` `Failed` `Cancelled`), verdict, defect count, stage-1 latency, notes.

Operator needs: the folder, start, and each board's verdict as it finishes.

Noise: concurrency, colorize, and the single-template path sit in the same bar as Start. The progress sentence restates the grid.

## History (F3)

Today: verdict, batch, from, to, file name, defect-class pills (`Short`, `Open`, `Mousebite`, `Spur`, `Spurious Copper`, `Pin-hole`), Search, Clear, Export CSV, Export PDF, then a grid (time, file, batch, verdict, defect count, class summary, stage-1, round-trip). Double-click opens that board on Single Inspect. A status line and Open Data Folder sit underneath. Header text says "3. History" and the next tab also says "3. Dashboard".

Operator needs: find a board and open it.

Noise: six filters and three buttons before a single row is useful. Round-trip and stage-1 do not help a lookup. Export is real, and it is not the reason this screen exists.

## Dashboard (F4)

Today: a day picker and Refresh Metrics, then five cards (boards inspected, pass, rework, scrap, yield = pass / inspected), top three defect classes, halted-batch count and ids, and a 24-row hourly table (inspected, pass, rework, scrap).

Operator needs: today's pass, rework, scrap, and yield.

Noise: the hourly grid is 24 rows of zeros on a quiet day. "Quality Guardrail Status" plus a separate halted-id line duplicates `LineHalted` already stored on `v_batches`. The tab index label collides with History.

## Ask (NL → SQL)

Today: a paragraph of instructions, a question box, Generate SQL, a read-only SQL preview, an explanation, Run Query, then an auto-generated grid. Run is a second primary. SqlGuard rejects anything that is not a single `SELECT`/`WITH` against `v_inspections`, `v_defects`, or `v_batches` (no comments, no DDL/DML, max 500 rows). The status line is `Execution rejected by SqlGuard: …`.

Operator needs: an answer from history. The engineer also needs to see the SQL before it runs. That review step is the product, not decoration.

Noise: the instruction paragraph, and the italic explanation under the SQL.

## Settings (F7)

Today: a labeled form. API base URL (`http://localhost:8000`), timeout seconds, batch concurrency, default IPC class (`Class 2` / `Class 3`), default batch id (`BATCH-UPLOAD`), colorize, LLM provider (`None` / `Gemini` / `OpenAI`), model name (`gemini-2.5-flash`), summary language (`en` / `zh` / `en+zh`), Save Settings, Check Backend Health. Keys stay in `GEMINI_API_KEY` / `OPENAI_API_KEY`. The status bar already shows backend online/offline, detector and version, the SQLite path, and `LLM: None (Template fallback)` when the provider is `None` or the key is missing.

Operator needs: the URL, whether the backend answers, and which explainer is active. Verdicts stay rule-based when the LLM falls back to `TemplateSummaryProvider`.

Noise: a label on every field ("Request Timeout (seconds, 1–300)"), and a second health button when the status bar already polls.

## Status bar

Backend lamp, `detector · version`, database path, LLM provider. This is the right place for offline and template-fallback. It is easy to miss because the inspect form is louder.
