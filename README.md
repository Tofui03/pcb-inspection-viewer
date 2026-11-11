# PCB Inspection Viewer

C# .NET 8 WPF modern operator station client for the [DeepPCB-AOI](https://github.com/Tofui03/DeepPCB-AOI) bare-board defect inspection system.

![PCB Inspection Viewer](docs/inspect-loop.gif)

## Key Features

- **Single-Board Inspection (`F1`)**: Interactive canvas with 640×640 coordinate alignment, smooth hardware-accelerated zoom/pan, layer toggles (Test image, Golden template, Diff heatmap, Bounding boxes), and instant IPC-A-600 severity breakdown.
- **Bounded-Concurrency Batch Runner (`F2`)**: Folder scanning with automatic DeepPCB pair resolution (`<id>_test.jpg` ↔ `<id>_temp.jpg`), configurable 1–8 worker concurrency (`SemaphoreSlim`), and real-time progress reporting.
- **Local History & Persistence (`F3`, `F6`)**: EF Core SQLite database with indexing, virtualized data grid capable of scrolling 10,000+ records, SHA-256 local image caching, and double-click drill-down inspection.
- **Daily Quality Dashboard (`F4`)**: Aggregate daily yield percentages, total inspected boards, rework and scrap counts, top 3 defect class distributions, and 24-hour hourly throughput breakdown.
- **CSV & PDF Quality Reports (`F5`)**: One-click export to CSV with UTF-8 BOM encoding (Excel compatible with English & Chinese text) and single-page A4 PDF quality audit summaries via QuestPDF.
- **Guarded NL → SQL Assistant (`Ask`)**: Query historical defect distributions and trends using plain English or Chinese. Secured by code-enforced `SqlGuard` (read-only views, keyword ban, query timeout, and complete audit logging).

---

## Quick Start

### 1. Prerequisites
- Windows 10/11 (or Linux/macOS with .NET 8 SDK for building Core libraries)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- DeepPCB-AOI Backend running locally or via Docker:
  ```bash
  cd backend
  docker compose up -d
  ```
  Verify the backend is live at `http://localhost:8000/api/health`.

### 2. Build & Run Client
```bash
# Clone the repository
git clone https://github.com/Tofui03/pcb-inspection-viewer.git
cd pcb-inspection-viewer

# Build solution
dotnet build -c Release

# Run WPF Application
dotnet run --project src/PcbInspectionViewer.App
```

### 3. Run Unit & Integration Tests
```bash
# Run all Core tests
dotnet test tests/PcbInspectionViewer.Core.Tests/PcbInspectionViewer.Core.Tests.csproj

# Run App tests
dotnet test tests/PcbInspectionViewer.App.Tests/PcbInspectionViewer.App.Tests.csproj

# Run Integration tests
dotnet test tests/PcbInspectionViewer.IntegrationTests/PcbInspectionViewer.IntegrationTests.csproj
```

---

## Architecture & Security

- **Rule Supremacy**: Verdicts (`PASS`, `REWORK`, `SCRAP`) are strictly computed by deterministic rules. LLMs only provide summary explanations and translation.
- **SqlGuard Safety**: Natural language SQL generation executes with `PRAGMA query_only = ON` over allowlisted views (`v_inspections`, `v_defects`, `v_batches`) with a 2-second timeout watchdog.
- More details: see [docs/architecture.md](docs/architecture.md) and [docs/benchmarks.md](docs/benchmarks.md).
