# DeepPCB-AOI Containerised Backend

This directory provides the `docker-compose.yml` to run the `DeepPCB-AOI` FastAPI backend on port `8000` with `DETECTOR_BACKEND=yolov8`.

## Quick Start

```bash
cd backend
docker compose up -d
curl http://localhost:8000/api/health
```

Expected response:

```json
{
  "status": "ok",
  "version": "1.2.0",
  "detector_backend": "yolov8",
  "execution_provider": "CPUExecutionProvider",
  "dataset_loaded": false
}
```
