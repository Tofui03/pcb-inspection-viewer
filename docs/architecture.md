# Architecture Overview — PCB Inspection Viewer

## High-Level System Architecture

```mermaid
flowchart TD
    subgraph UI ["Desktop Client (C# .NET 8 WPF)"]
        Shell["MainShellViewModel (Tabs Host)"]
        F1["InspectView (F1: DefectOverlayCanvas, Zoom/Pan)"]
        F2["BatchView (F2: Bounded Concurrency Runner)"]
        F3["HistoryView (F3: Virtualized History Grid)"]
        F4["DashboardView (F4: Daily KPIs & Throughput)"]
        Ask["AskView (P2: Guarded NL → SQL)"]
        Settings["SettingsView (F7: API, Model & Batch Options)"]
    end

    subgraph Core ["PcbInspectionViewer.Core (Platform-Independent)"]
        ApiClient["InspectionApiClient (Polly Resilience)"]
        BatchEngine["BatchInspectionRunner (SemaphoreSlim)"]
        SqlGuardEngine["SqlGuard (AST & View Security Filter)"]
        ExportEngine["ExportService (CSV BOM & QuestPDF)"]
        LlmEngine["SummaryCoordinator (Facts Validation & Fallback)"]
    end

    subgraph Storage ["Local Storage"]
        Sqlite[("SQLite (inspections.db)")<br/>Batches, Inspections, Defects, Views]
        ImageCache[("Local PNG Cache")<br/>SHA-256 Content-Addressed]
    end

    subgraph Backend ["DeepPCB-AOI Backend (FastAPI / Docker)"]
        FastApi["/api/upload-triage & /api/health"]
        YoloDetector["YOLOv8n 3-Channel Detector (ONNX)"]
        RuleEngine["Deterministic IPC-A-600 Rule Engine"]
    end

    Shell --> F1 & F2 & F3 & F4 & Ask & Settings
    F1 --> ApiClient & Storage
    F2 --> BatchEngine --> ApiClient & Storage
    F3 --> Storage & ExportEngine
    F4 --> Storage
    Ask --> SqlGuardEngine --> Sqlite
    ApiClient --> FastApi
    FastApi --> YoloDetector --> RuleEngine
```

## Architectural Principles & Security Boundary

1. **Deterministic Rule Supremacy**:
   - Board disposition (`PASS`, `REWORK`, `SCRAP`) is decided strictly by deterministic rule algorithms evaluated by `DeepPCB-AOI`.
   - The LLM only explains and assists with natural-language exploration; it **never** overrides or computes verdicts.

2. **Defense-in-Depth SQL Guard (`ISqlGuard`)**:
   - Connection-level protection: `Mode=ReadOnly` with `PRAGMA query_only = ON;`.
   - Allowlisted Views: Users can only query `v_inspections`, `v_defects`, and `v_batches`. Physical tables containing raw logs or file paths are concealed.
   - Prohibited Tokens: DDL, DML, `PRAGMA`, comments (`--`, `/*`), extension loading, and multi-statement queries (`;`) are rejected upfront by regular expression AST scanning.
   - Strict Execution Watchdog: All queries are capped at `LIMIT 500` with a 2.0-second execution cancellation timeout.

3. **Content-Addressed Local Cache (`IImageCache`)**:
   - Inspection images are saved locally as content-addressed `<sha256>.png` files, decoupling client memory from large base64 strings and enabling instant historical drill-down without repeating server inference.
