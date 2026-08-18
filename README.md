# Wilken-Automation

A restartable, state-aware, self-recovering, validated, auditable export engine for Wilken CS/2 Asset Accounting data (78 clients × 23 fiscal years × 2 departments = 3,588 jobs), currently running against a **simulated (dummy) Wilken adapter** for end-to-end testing.

- Full technical analysis and design: [`ANALYSIS.md`](./ANALYSIS.md)
- Backend: **.NET 8** (`WilkenAutomation/` — current; `backend/WilkenExport.Api` — earlier prototype)
- Database: **MySQL** (SQLite in Development for local testing)
- Frontend: **Angular 21** (`frontend`) — dashboard, job queue, run creation, audit report

See [`WilkenAutomation/README.md`](./WilkenAutomation/README.md) for the current backend architecture and run instructions.

## Two ways to test with dummy data

### 1. Frontend only (no backend needed)

```bash
cd frontend
npm install
npm start          # http://localhost:4200
```

Keep **Data source = Mock (browser)**. The complete engine runs in the browser with `localStorage` persistence.

### 2. Frontend + backend (recommended)

```bash
# Terminal 1 — API
cd WilkenAutomation
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project WilkenAutomation.Api

# Terminal 2 — Worker
$env:DOTNET_ENVIRONMENT='Development'
dotnet run --project WilkenAutomation.Worker

# Terminal 3 — frontend
cd frontend
npm start
```

Switch the header toggle to **Backend API**. The worker runs mock Wilken automation, writes export files, validates them, and streams live updates via SignalR.

## Typical test flow

1. **New Run** → pilot preset (8 jobs) → Generate with Auto-start.
2. **Dashboard** → live progress, worker/session status, logs.
3. **Jobs** → filter, inspect, requeue failed jobs.
4. **Audit** → reconciliation report + CSV download.

## Configuration

Defaults live in `WilkenAutomation/WilkenAutomation.Worker/appsettings.json` and `WilkenAutomation/WilkenAutomation.Api/appsettings.json`. Each run stores an immutable configuration snapshot in the database.
