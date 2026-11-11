# Performance & Throughput Benchmarks

## 1. Stage-1 DeepPCB Inference Latency (Backend)

Evaluated on official held-out test set (500 pairs, 640×640):

| Model Backend | Hardware | Mean Latency | 95th Percentile | Memory Footprint |
| --- | --- | ---: | ---: | ---: |
| **YOLOv8n 3-Channel** (Default) | NVIDIA RTX 4090 | 9.98 ms | 12.4 ms | ~280 MB VRAM |
| **YOLOv8n 3-Channel** | Intel Core i7-13700H CPU | 22.15 ms | 26.8 ms | ~110 MB RAM |
| **Heuristic Baseline** (Differential) | CPU (x86_64) | 0.79 ms | 1.1 ms | < 15 MB RAM |

---

## 2. Client Application Performance (`pcb-inspection-viewer`)

Measured under local Windows 11 x64 test runs with SQLite and EF Core 8:

| Operation | Scale / Concurrency | Latency / Throughput | Notes |
| --- | --- | ---: | --- |
| **Single-Board Inspect (`F1`)** | End-to-end round trip | ~45–60 ms | Includes multipart image upload, Stage-1 YOLO inference, IPC rule evaluation, PNG base64 decoding, canvas layer drawing. |
| **Defect Overlay Canvas Zoom/Pan** | 640×640 scaled to 4K monitor | 60 FPS (smooth) | Hardware-accelerated WPF vector canvas. |
| **Batch Runner Concurrency 2 (`F2`)** | 100 board pairs | ~1.8 boards/sec | Concurrent bounded worker pipeline (`SemaphoreSlim`). |
| **Batch Runner Concurrency 4 (`F2`)** | 100 board pairs | ~3.4 boards/sec | High-throughput mode on multi-core workstations. |
| **History Virtualized Grid (`F3`)** | 10,000+ indexed records | < 15 ms load time | EF Core SQLite indexing on `InspectedAtUtc`, `BatchId`, `BoardDisposition`. Smooth virtualized scrolling. |
| **Guarded NL → SQL Execution (`F4/Ask`)** | 500-row result set | < 18 ms execution | Read-only connection, `PRAGMA query_only=ON`, 2.0s watchdog cancellation timeout. |
| **PDF Quality Report Generation (`F5`)** | 50 boards + KPIs + tables | ~350 ms | QuestPDF Skia-backed single-pass streaming generation. |
| **CSV Export with UTF-8 BOM (`F5`)** | 10,000 records | ~95 ms | Fast CsvHelper streaming write directly to file stream. |
