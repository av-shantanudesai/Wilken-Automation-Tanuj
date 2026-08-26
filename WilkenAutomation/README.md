# WilkenAutomation – Wilken CS/2 Asset Accounting Export Engine

.NET 8 backend that automates the Wilken CS/2 desktop application, processes the
complete export queue (Client × Fiscal Year × Department), persists all state in
MySQL, recovers automatically from crashes/restarts, validates every export,
calculates SHA-256 checksums, keeps a full audit trail, and streams real-time
updates to the existing Angular 21 dashboard via SignalR.

## Architecture

```text
Angular 21 Dashboard (frontend/)
        ↑  REST (http://localhost:5210/api) + SignalR (/hubs/job-monitoring)
        │
WilkenAutomation.Api          ← REST, SignalR hub, run/job management, audit, monitoring
        │
        ├── MySQL (source of truth: AutomationRuns, ExportJobs, JobAttempts, AutomationLogs)
        │
WilkenAutomation.Worker       ← separate process in the interactive Windows session
        │                       (job loop, restart recovery, screenshots, session recovery)
        ↓
IWilkenAutomationService      ← Mock (simulation) | Windows (FlaUI / UIA3 desktop automation)
        ↓
Wilken CS/2 → report/spool → export file → validation → SHA-256 → SUCCESS / RETRY / FAILED_FINAL
```

Key decisions:

- **API and Worker are separate processes.** Desktop automation requires an
  interactive Windows session; the API can run as a service/IIS. The worker owns
  Wilken, the API owns HTTP. They share only the database (via
  `WilkenAutomation.Infrastructure`) and the SignalR hub.
- **Database first, SignalR second.** Every state change is persisted before the
  corresponding event is published. SignalR is purely a notification channel; a
  lost connection never loses state.
- **The worker publishes through the hub.** It connects to
  `/hubs/job-monitoring` as a SignalR *client* and invokes `PublishEvent` /
  `PublishWorkerStatus`; the hub relays to dashboard clients and caches the
  worker heartbeat for `GET /api/worker/status`.
- **All Wilken interaction is behind `IWilkenAutomationService`.**
  `MockWilkenAutomationService` (full simulation incl. failures/crashes/empty
  periods) and `WindowsWilkenAutomationService` (FlaUI UIA3) are interchangeable;
  the job executor and worker loop are identical for both.

## Projects

| Project | Responsibility |
| ------- | -------------- |
| `WilkenAutomation.Application` | Enums, entities, DTOs, interfaces, job state machine, job generator, run statistics, file validator, mock automation, **JobExecutor** (16-step workflow), startup recovery |
| `WilkenAutomation.Infrastructure` | EF Core DbContext (MySQL via Pomelo / SQLite for local testing), repositories with transactions, SHA-256 service |
| `WilkenAutomation.Api` | Controllers, SignalR hub, worker status registry, DTO wire format |
| `WilkenAutomation.Worker` | Job loop, heartbeat, SignalR, FlaUI, screenshots |
| `WilkenAutomation.TestDesktop` | Dummy Win UI for FlaUI testing (not Wilken) |
| `WilkenAutomation.Tests` | 32 tests on the job engine (mock, no UI) |

## Running

Two processes, in this order:

```powershell
# 1. API (creates the database schema on first start)
cd WilkenAutomation
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project WilkenAutomation.Api

# 2. Worker agent (interactive session)
$env:DOTNET_ENVIRONMENT='Development'
dotnet run --project WilkenAutomation.Worker
```

Then run the Angular app (`cd frontend; npm start`) and switch the header toggle
to **backend** mode.

### Database

- **Production:** `Database:Provider = "MySql"` (appsettings.json) with the
  connection string pointing at your MySQL server. Schema is created
  automatically on API start.
- **Local testing:** the `Development` environment uses
  `Database:Provider = "Sqlite"` (`wilken_automation.db` in the solution folder)
  so API + Worker can be tested without a MySQL server. An in-memory database is
  not possible here because API and worker are separate processes.

### Automation mode (switch here)

The job executor is the same in all three modes. Only `IWilkenAutomationService` changes.

| Mode | Config | What runs |
|------|--------|-----------|
| **Mock** | `DOTNET_ENVIRONMENT=Development` (default) and `Worker:AutomationMode=Mock` | No desktop UI. Simulated Wilken. |
| **DesktopTest** | `DOTNET_ENVIRONMENT=DesktopTest` | FlaUI drives `WilkenAutomation.TestDesktop` (dummy Win UI with Client / Year / Department + export). |
| **Wilken** | `Worker:AutomationMode=Wilken` plus real exe + selectors | FlaUI drives real Wilken CS/2. |

**Desktop UI test (dummy app):**

```powershell
# Terminal 1 — API (unchanged)
cd WilkenAutomation
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project WilkenAutomation.Api

# Terminal 2 — Worker against dummy desktop
$env:DOTNET_ENVIRONMENT='DesktopTest'
dotnet run --project WilkenAutomation.Worker
```

Visual Studio: start Worker profile **Worker (DesktopTest UI)**.

Dummy login is `tester` / `tester`. Client `002` + department `Steuerrecht` exports a valid empty report (`SUCCESS_EMPTY`).

To go back to mock: run Worker with `DOTNET_ENVIRONMENT=Development` (or set `Worker:AutomationMode` to `Mock`).

To go to real Wilken: set `AutomationMode` to `Wilken`. Do **not** launch Wilken from the worker.

### Real Citrix test environment (attach-only)

Wilken test is opened by the user from Citrix Workspace in the browser. After login,
the worker only automates — it never opens Citrix, never starts Wilken, and never
types the Wilken password.

1. In the browser, open Citrix Workspace and start the **Test Environment** desktop.
2. Log in to Wilken yourself.
3. On **that same desktop**, start `WilkenAutomation.Worker` (`AutomationMode=Wilken`).
   The worker must share Wilken's Windows session. A worker on the browser PC can
   only see a Citrix picture and cannot automate.
4. Inspect the live UI (the stack may be WinForms, WPF, Win32, or Java):

```powershell
dotnet run --project WilkenAutomation.Worker -- --inspect
dotnet run --project WilkenAutomation.Worker -- --inspect "Wilken CS/2"
```

5. Map `Wilken:Selectors` from the dump (`AutomationId:`, `Name:`, `ClassName:`, or `NameContains:`).
   Real mode refuses to start until the core selectors are mapped (`INSPECT_REQUIRED`).
6. Start a run from the dashboard. The worker attaches to the already-open window.

If `--inspect` shows almost no controls on a Java window, enable Java Access Bridge
inside the Citrix session (`jabswitch -enable`), restart Wilken, and inspect again.

`AttachOnly`, `SkipLogin`, and `RequireInspectedSelectors` default to `true` in
`appsettings.json`. Replica/DesktopTest sets them false so the dummy exe can still launch.

### Credentials

Not used for Citrix test login (the user logs in). `Wilken:Username` / `WILKEN_PASSWORD`
remain available only if `SkipLogin` is later turned off.

## REST API

Frontend-compatible endpoints (unchanged contract) plus the new ones:

```text
GET  /api/runs                          GET  /api/runs/{runId}
POST /api/runs                          POST /api/runs/{runId}/start
POST /api/runs/{runId}/pause            POST /api/runs/{runId}/retry-failed
GET  /api/runs/{runId}/status           GET  /api/runs/{runId}/summary   (alias)
GET  /api/runs/{runId}/audit            GET  /api/runs/{runId}/audit.csv
GET  /api/runs/{runId}/jobs

GET  /api/jobs?runId&status&client&fiscalYear&department&page&pageSize
GET  /api/jobs/current                  GET  /api/jobs/{jobId}
POST /api/jobs/{jobId}/requeue          POST /api/jobs/{jobId}/retry     (alias)

GET  /api/logs?runId&jobId&limit
GET  /api/worker/status
```

## SignalR

Hub: `/hubs/job-monitoring`. Events:
`JobStarted, JobStatusChanged, JobApplicationStateChanged, JobCompleted,
JobFailed, JobRetrying, RunProgressChanged, DashboardSummaryChanged,
WorkerStatusChanged, WilkenSessionChanged, LastErrorChanged, LastSuccessChanged,
RunsChanged`.

Job events carry the same JSON shape as the REST `Job` model; the application
state events additionally carry `applicationState` (UPPER_SNAKE, e.g.
`WAITING_FOR_REPORT`) and `runtimeSeconds`. Runtime ticks are SignalR-only —
no per-second database writes.

## Frontend contract alignment (documented decisions)

The existing Angular models were inspected first; the backend adopted them:

- **Job/run status wire values** stay PascalCase (`SuccessWithData`,
  `FailedFinal`, …) as the frontend's TypeScript unions expect — these map 1:1
  to the specification's `SUCCESS_WITH_DATA` / `FAILED_FINAL` etc.
- **Field naming** follows the frontend (`fileSizeBytes`, `validationOutcome`,
  `screenshotPath`); the database columns follow the specification
  (`FileSize`, `ValidationStatus`, `LastScreenshotPath`) and are mapped in DTOs.
- **Departments** keep the frontend values `Handelsrecht` (Commercial Law) and
  `Steuerrecht` (Tax Law).
- **Application states / worker states** are sent UPPER_SNAKE per specification;
  the dashboard displays them as free text, no change required.
- **Frontend change made (additive only):** `@microsoft/signalr` client +
  `RealtimeService`; the dashboard now refreshes immediately on hub events in
  backend mode. Polling remains as fallback. No visual redesign.

## Reliability model

- Job lifecycle `PENDING → RUNNING → SUCCESS_WITH_DATA | SUCCESS_EMPTY | FAILED →
  RETRY → … → FAILED_FINAL` enforced by a state machine; max attempts
  configurable per run (default 3). A FAILED_FINAL job never stops the queue.
- State-based waiting everywhere (`WaitUntilAsync`, file-stability detection);
  no fixed sleeps for report readiness.
- Session recovery: crash/not-responding detection → screenshot → attempt marked
  failed → close/restart Wilken → retry from a defined checkpoint. Unknown
  modal dialogs abort the attempt safely (never random keypresses).
- Restart recovery on worker start: stale RUNNING jobs → RETRY (attempt marked
  `Interrupted`); previously successful jobs are re-verified (file exists +
  SHA-256 matches) and only re-queued if verification fails. Validated exports
  are never regenerated and never silently overwritten (retries get
  attempt-suffixed filenames).
- Exports land in `Exports/Mandant_<client>/<year>/Mandant_<client>_<year>_<department>.<ext>`
  and are never modified after checksum calculation.

## Note on the previous prototype

`backend/WilkenExport.Api` is the earlier single-process prototype and is
superseded by this solution. It is no longer needed for running the system (it
also binds the same port 5210 — do not run both at once).
