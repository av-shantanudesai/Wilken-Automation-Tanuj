# Wilken CS/2 Asset Accounting Export Automation — Technical Analysis

Status: Design + working reference implementation (dummy adapter).
Scope: 78 clients × 23 fiscal years × 2 departments (Handelsrecht / Steuerrecht) = **3,588 export jobs**, unattended 24/7 operation.

The repository contains a runnable reference implementation:

- `backend/WilkenExport.Api` — .NET 8 export engine (job store, scheduler, worker, validation, checksums, audit) with a **simulated Wilken adapter** for dummy-data testing.
- `frontend` — Angular 21 operator console with a **mock mode** (runs entirely in the browser) and a **backend mode**.

Everything Wilken-specific is isolated behind one interface (`IWilkenAdapter`), so the production RPA/integration adapter can replace the simulator without touching job management, validation, persistence, or monitoring.

---

## 1. Requirement Understanding

**Business objective.** Export all Asset Accounting evaluations (additions, disposals, asset grids, account summaries, and related evaluations) from Wilken CS/2 for every combination of client, fiscal year, and accounting basis (Commercial Law / Tax Law), producing one validated, checksummed, immutable original export file per combination — as evidence-grade input for a migration (e.g. ViewBox import).

**Technical objective.** A restartable, state-aware, self-recovering, validated, auditable export *engine* with an RPA worker — not a desktop macro. It must:

- Generate the full job matrix dynamically from configuration (expected count = clients × years × departments; deviations must be visible before production).
- Process jobs sequentially with a single worker (version 1), driven by application-state detection, never fixed waits.
- Persist every state transition so crashes, restarts, power failures, and network/database outages never lose progress or repeat completed exports.
- Distinguish `SUCCESS_WITH_DATA` / `SUCCESS_EMPTY` / `FAILED_FINAL`, retry automatically (max 3 attempts, configurable), and never let one failed job stop the batch.
- Validate every export on four levels (file, job identity, structure, data), compute SHA-256, preserve originals unchanged, and never overwrite silently.
- Provide structured logging, live monitoring, a completeness check, and a final audit report that reconciles to the expected total.

---

## 2. Wilken Integration Assessment

The integration method **must be verified on the installed system before committing to GUI automation**. Neither the existence nor the absence of an API may be assumed.

| Option | Available/Unknown/Unavailable | Suitability | Investigation Required | Recommendation |
| ------ | ----------------------------- | ----------- | ---------------------- | -------------- |
| Official API | Unknown | Very high if present | Ask Wilken support/documentation; inspect installation directory for SDK/DLLs; check license scope | Prefer over everything else if it covers Asset Accounting exports |
| Web service (SOAP/REST) | Unknown | Very high if present | Inspect server components, IIS/apache configs, netstat on app server; ask vendor | Prefer if present and licensed |
| Command-line interface | Unknown | High | Inspect install folder for `.exe` utilities with `/help`, `-?`; check vendor batch documentation | Use if reports can be parameterized (client/year/basis) |
| Batch-processing interface | Unknown | High | Check for job/batch configuration inside CS/2 (many ERP-era systems have report batch queues) | Combine with file pickup + our engine if present |
| Scheduled processing | Unknown | Medium | Check internal scheduler; useful only if output location and parameters are controllable per job | Only as a building block |
| Database-supported export | Unknown (DB access likely exists) | Medium–High as **read-only verification**; risky as primary export | Identify DB vendor/schema; check vendor support stance on direct reads | Use read-only for cross-validation (record counts per client/year), NOT as the official export unless vendor-approved — exported evaluations contain calculation logic not necessarily reproducible from raw tables |
| Report/spool interface | Unknown | High | Determine how spools are stored (files? DB table? UI-only list?); whether spool entries carry client/year metadata | If spool output lands in files/DB, pick up there instead of GUI-exporting |
| Scriptable internal interface (macros/OLE) | Unknown | Medium | Check for OLE/COM registration (`OLEView`), internal macro language | Use if stable |
| Windows UI Automation (UIA/MSAA) | Unknown until inspected | Medium–High | Inspect with Accessibility Insights / Inspect.exe / FlaUInspect: does the UI expose AutomationIds, names, control types? UI technology (WinForms/Delphi/VB/Java?) decides detectability | Primary fallback; expected to work at least partially |
| Keyboard automation | Almost certainly available | Medium | Verify stable tab order, shortcuts, function keys per dialog | Second-level fallback per control |
| Window handle/class automation (Win32) | Available | Medium | Enumerate classes with Spy++/AutoIt Window Info; legacy toolkits often expose useful class names + control IDs | Combine with UIA |
| Image/text recognition (OCR) | Available | Low–Medium | Only for elements invisible to all APIs (owner-drawn grids are a known risk) | Third-level fallback, anchor-relative only |
| Fixed coordinates | Available | Very low | — | Last resort only, per-element, documented |

**Decision rule:** if any official/stable programmatic path (API, web service, CLI, batch, file-based spool pickup) covers the required evaluations, use it and keep the rest of this engine unchanged — only the adapter changes. Otherwise implement the adapter with UI Automation first, keyboard second, OCR third, coordinates last. **Document the investigation result before the pilot.**

---

## 3. Recommended Architecture

```text
┌────────────────────────────────────────────────────────────┐
│ Configuration (appsettings / run request; snapshotted      │
│ immutably per run)                                         │
└──────────────┬─────────────────────────────────────────────┘
               ▼
┌──────────────────────────┐    ┌───────────────────────────┐
│ Job Generator            │───▶│ Persistent Job Store      │
│ clients × years × depts  │    │ MySQL: runs, jobs, logs   │
└──────────────────────────┘    └────────────┬──────────────┘
                                             ▼
                              ┌──────────────────────────────┐
                              │ Job Scheduler (single worker │
                              │ loop; picks next PENDING/    │
                              │ RETRY by configured order)   │
                              └────────────┬─────────────────┘
                                           ▼
                              ┌──────────────────────────────┐
                              │ Wilken Worker (orchestrates  │
                              │ one job attempt end-to-end)  │
                              └────────────┬─────────────────┘
                                           ▼
                    ┌──────────────────────────────────────────┐
                    │ UI/Integration Adapter (IWilkenAdapter)  │
                    │  - SimulatedWilkenAdapter (dummy/test)   │
                    │  - future: UIA/API adapter (production)  │
                    │ includes Report/Spool Detector +         │
                    │ Export trigger + Session Recovery        │
                    └────────────┬─────────────────────────────┘
                                 ▼
        ┌────────────────┐  ┌──────────────────┐  ┌───────────────┐
        │ File Validator │─▶│ Checksum (SHA256)│─▶│ Export Manager│
        │ 4 levels       │  │                  │  │ atomic rename,│
        └────────────────┘  └──────────────────┘  │ no overwrite  │
                                                  └──────┬────────┘
                                                         ▼
                    ┌────────────────────────────────────────────┐
                    │ Audit/Logging (structured JobLog table)    │
                    │ Monitoring (REST status API + Angular UI)  │
                    └────────────────────────────────────────────┘
```

Component responsibilities (as implemented):

| Component | Code | Responsibility |
| --------- | ---- | -------------- |
| Configuration | `ExportOptions`, `RunConfig` | Defaults in `appsettings.json`; every run stores its own immutable `RunConfig` snapshot (JSON) so later config edits never affect an in-flight run |
| Job Generator | `JobGenerator` | Cross product, deterministic IDs, configurable ordering, expected-vs-generated deviation recorded on the run |
| Persistent Job Store | `ExportDbContext` (MySQL/Pomelo; in-memory provider for quick dev) | Runs, jobs, structured logs; state survives all restarts |
| Job Scheduler + Worker | `ExportWorker` (BackgroundService) | Single worker; stale-RUNNING recovery on startup; retry policy; run completion + completeness check |
| UI/Integration Adapter | `IWilkenAdapter`, `SimulatedWilkenAdapter` | All Wilken-specific interaction incl. spool readiness detection and session recovery |
| File Validator | `FileValidator` | Levels 1–4; distinguishes Valid / ValidEmpty / Invalid |
| Checksum Generator | `ChecksumService` | SHA-256 of the original file |
| Idempotency Guard | `RunVerificationService` | On resume, re-verifies completed jobs (file exists + checksum matches) before skipping |
| Audit/Logging | `JobLogEntry` + `RunsController` audit endpoints | Structured events, JSON + CSV audit report |
| Monitoring | `WorkerStatusService` + `/api/runs/{id}/status` + Angular dashboard | Progress, ETA from real runtimes, current job/action, session health |

---

## 4. Job Data Model

**Run** (`ExportRun`): `Id` (RUN-YYYYMMDD-NNN), `Status` (Created/Running/Paused/Completed), `CreatedAt`, `StartedAt`, `CompletedAt`, `ExpectedJobCount`, `GeneratedJobCount`, `ConfigJson` (immutable snapshot), `Notes`.

**Job** (`ExportJob`):

| Field | Description |
| ----- | ----------- |
| Id | `{RunId}-M{Client}-{Year}-{HR\|ST}` — deterministic, traceable |
| RunId | Owning run |
| Client / FiscalYear / Department / DepartmentCode | Job identity |
| OrderIndex | Position in configured processing order |
| Status | PENDING / RUNNING / RETRY / SUCCESS_WITH_DATA / SUCCESS_EMPTY / FAILED_FINAL |
| AttemptCount | Executed attempts |
| StartTime / EndTime / DurationMs | Timing of the deciding attempt |
| FileName / FilePath / FileSizeBytes | Export artifact |
| Sha256 | Checksum of the original file |
| ValidationOutcome / ValidationDetail / RecordCount | Validation evidence |
| ErrorCode / ErrorMessage / ScreenshotPath | Structured failure evidence |
| CreatedAt / UpdatedAt | Audit timestamps |

**Log** (`JobLogEntry`): Timestamp, RunId, JobId, Client, Year, Department, Level, Action, Attempt, DurationMs, ErrorCode, Message — one row per automated action/error.

---

## 5. Job State Machine

```text
                 PENDING ◄──────────────────────────────┐
                    │                                   │
                    ▼                                   │ (requeue FAILED_FINAL /
                 RUNNING ──── interrupted by restart ─► RETRY   re-verification failed)
                    │                                   ▲ │
      ┌─────────────┼─────────────┐                     │ │
      ▼             ▼             ▼                     │ ▼
SUCCESS_WITH_   SUCCESS_      attempt FAILED ───────────┘ RUNNING (next attempt)
DATA            EMPTY           │ (attempts < max)          │
      │             │           │ (attempts = max)          │
      │             │           ▼                           │
      │             │      FAILED_FINAL ◄───────────────────┘
      ▼             ▼           │
   (terminal, skip on restart;  └── operator "retry-failed" → PENDING
    re-verified via checksum
    before skipping)
```

Improvements over the base machine from the specification:

- `FAILED` is transient (an attempt outcome), persisted immediately as `RETRY` or `FAILED_FINAL` — no job can be stranded in an ambiguous state.
- Restart recovery: stale `RUNNING` → `RETRY` (`INTERRUPTED`), so a power failure mid-job costs at most one attempt.
- Idempotency edge: `SUCCESS_*` → `PENDING` only when re-verification (file missing / checksum mismatch) fails on resume.
- A job is set to `SUCCESS_*` **only after validation and checksum succeed** — never because Export was clicked.

---

## 6. Detailed Automation Workflow

Per eligible job (PENDING/RETRY, lowest OrderIndex):

1. **Persist attempt start** — `AttemptCount++`, status RUNNING, StartTime; log `START`. (Crash-safe from the first moment.)
2. **PREPARE_SESSION** — start Wilken if not running, reuse healthy session, log in if required; confirm expected application state (main window ready), timeout-guarded.
3. **SELECT_CLIENT** — select job's client; **verify selection** before continuing (adapter throws `WRONG_CLIENT` on mismatch).
4. **OPEN_ASSET_ACCOUNTING** — navigate; do not continue until target screen detected.
5. **SET_FISCAL_YEAR** — set and re-read/verify the year.
6. **SELECT_DEPARTMENT** — Commercial Law or Tax Law; verify.
7. **EXECUTE_REPORT** — start the evaluation; capture a report/process identifier where possible.
8. **WAIT_SPOOL** — *state-based readiness*: poll report/spool status (interval ~250 ms–2 s) until ready or configurable timeout (`SpoolSeconds`, default 300 s; report execution cap 900 s). No fixed waits; 40 s and 8 min jobs both handled.
9. **IDENTIFY REPORT** — match the spool entry to the current client/year/department; never export "the newest spool" blindly.
10. **EXPORT** — trigger export, write to a **temporary file** (`*.attemptN.tmp`) in the target directory.
11. **VALIDATE** — Levels 1–4 (see §9) against the temp file; `Invalid` ⇒ attempt failed.
12. **CHECKSUM** — SHA-256 of the validated original.
13. **SAVE_FILE** — atomic move temp → final name; if the final name exists, a distinct `_rN` name is used (never silent overwrite).
14. **FINALIZE** — status `SUCCESS_WITH_DATA` or `SUCCESS_EMPTY` (validation outcome decides), EndTime/Duration persisted, log `SUCCESS`; worker immediately picks the next job.

**On any failure:** structured error code, diagnostics/screenshot saved, temp file deleted, attempt marked failed (RETRY or FAILED_FINAL), session recovered if `SessionLost`, queue continues.

---

## 7. UI Automation Strategy

Interaction priority (mandatory): **Control/ID → Keyboard → Image/Text Recognition → Fixed Coordinate.** Expected classification (to be confirmed by inspection, §POC):

| Interaction | Preferred method | Why |
| ----------- | ---------------- | --- |
| Main window detection | Window Handle/Class + title | Process/window class is the most stable signal; also drives health checks |
| Login fields | UI Control (UIA/control ID) | Standard edit controls are almost always exposed; keyboard fallback (Tab order) trivial |
| Client/company selection | UI Control; Keyboard fallback | Must be verifiable (read back selected value) — only control-level access allows that reliably |
| Menu navigation to Asset Accounting | UI Control (menu items) or Keyboard (Alt-mnemonics/function keys) | Menus expose names via UIA/Win32 even in legacy apps; mnemonics are stable across resolutions |
| Fiscal year field | UI Control; Keyboard fallback | Value must be written **and read back** for verification |
| Department (HR/ST) selector | UI Control (radio/combo); Keyboard arrows fallback | Selection state must be verifiable |
| Execute/Run button | UI Control; keyboard (Enter/F-key) fallback | Simple invoke |
| Report status / progress | UI Control text polling; OCR fallback | This drives state-based waiting; if status text is owner-drawn, OCR on a stable anchor region |
| Spool window/list | UI Control (list/grid rows); OCR fallback if owner-drawn grid | Row metadata needed to match client/year/department; owner-drawn grids are the highest-risk element |
| Export action | UI Control/menu; keyboard fallback | Simple invoke |
| Save/file dialog | UI Control on the **common Windows dialog** + direct path typing | Windows file dialogs are fully UIA-exposed; type full path instead of navigating folders |
| Error/unexpected dialogs | Window Class + title watcher (global) | A dialog watcher must detect *any* unexpected modal, log + screenshot it, and trigger recovery — never random clicking |
| Coordinates | Only per-element, documented, resolution-locked | Last resort; forbidden where any technical property exists |

---

## 8. Error and Recovery Matrix

| Failure Scenario | Detection | Recovery | Retry? | Final Behavior |
| ---------------- | --------- | -------- | ------ | -------------- |
| Wilken crash | Process gone / window vanished / adapter exception `WILKEN_CRASH` (SessionLost) | Kill remnants, restart app, re-login, navigate to start state | Yes | RETRY → FAILED_FINAL after max attempts; queue continues |
| Wilken frozen/not responding | Window not responding (hung test), watchdog timeout on step | Graceful close, then force-kill, restart session | Yes | Same as crash |
| Session timeout / login screen appears | Login window detected mid-flow | Re-authenticate, resume from job start (safe checkpoint) | Yes | RETRY |
| Network interruption | DB/app errors, adapter step timeout | Wait/backoff, verify session health, restart session if needed | Yes | RETRY; engine keeps state locally, resumes when connectivity returns |
| Database interruption (job store) | EF Core exceptions in worker loop | Worker loop catches, waits, retries loop iteration; job state consistent because transitions are single SaveChanges | Yes (loop-level) | Processing resumes automatically |
| Missing spool | Spool poll finds no matching entry before timeout | Classified `SPOOL_TIMEOUT`; session health-checked | Yes | RETRY → FAILED_FINAL |
| Report timeout | `ReportExecutionSeconds` / `SpoolSeconds` exceeded | Cancel/close report state, recover session if unclear | Yes | RETRY → FAILED_FINAL |
| Export failure | Export action error / file never appears (`FileCreationSeconds`) | Cleanup temp, classify `EXPORT_FAILED` | Yes | RETRY |
| Invalid file (empty/truncated/corrupt) | Validator Level 1/3/4 fails | Temp deleted, `VALIDATION_FAILED` | Yes | RETRY → FAILED_FINAL |
| Wrong client/year/department | Verification after each selection + Level-2 content validation | Abort attempt immediately (`WRONG_*`) | Yes | RETRY; prevents silently exporting wrong data |
| Empty export (legitimate) | Level-4: structure valid, 0 records, no app error | None — this is success | n/a | `SUCCESS_EMPTY` |
| Unexpected dialog | Global dialog watcher (unknown window class/title) | Log + screenshot + safe stop of current action → session recovery | Yes | RETRY; never random clicking |
| Existing file at target path | Export Manager checks before move | Write under distinct `_rN` name; validated successes are skipped upstream anyway | n/a | No silent overwrite ever |
| Windows restart / power failure | On service start: stale RUNNING jobs found | Stale RUNNING → RETRY (`INTERRUPTED`); completed jobs re-verified (existence + SHA-256) then skipped | Yes | Continues exactly where it stopped; never from job 1 |
| Automation service crash | Same as Windows restart (service auto-restart via Task Scheduler/Windows Service recovery) | Same | Yes | Same |

---

## 9. Validation Matrix

| Validation | Mandatory | Method | Failure Result |
| ---------- | --------- | ------ | -------------- |
| L1: File exists | Yes | `FileInfo.Exists` after export | Attempt failed (`VALIDATION_FAILED`) |
| L1: Size > 0 | Yes | `FileInfo.Length` | Attempt failed |
| L1: Not locked / fully written | Yes | Exclusive-open probe | Attempt failed (re-checked; export may still be writing) |
| L1: Readable/parsable format | Yes | Open + parse | Attempt failed |
| L2: Content client = job client | Yes, where content exposes it | Parse header/content fields (not just filename) | Attempt failed (`WRONG_CLIENT` class) |
| L2: Content year = job year | Yes, where exposed | Same | Attempt failed |
| L2: Content department = job department | Yes, where exposed | Same | Attempt failed |
| L3: Expected sheets/sections present | Yes, once real export structure is known | Structure inspection (e.g. OpenXML sheet list for xlsx) | Attempt failed |
| L3: Expected headers/totals present | Recommended | Header/totals probe | Attempt failed |
| L3: Not truncated (end marker/totals row) | Yes | End-of-report detection | Attempt failed |
| L4: Record count | Yes | Count rows; compare against declared totals where available | Mismatch ⇒ failed; 0 records ⇒ **`SUCCESS_EMPTY`, not failure** |
| Checksum stored | Yes | SHA-256 of original | Job cannot reach SUCCESS without it (when enabled) |
| Re-verification on resume | Yes | File exists + SHA-256 matches stored value | Job re-queued (PENDING), logged `REVERIFY_REQUEUED` |

Note on the dummy adapter: the simulator emits a structured CSV embedding Client/FiscalYear/Department/RecordCount and an end-of-report marker precisely so all four levels are exercised end-to-end. For the real Wilken `.xlsx` exports, the same validator slots take an OpenXML-based implementation once the real structure is inspected (technical unknown).

---

## 10. File Naming and Storage Strategy

```text
<OutputRoot>/Wilken_Export/
    <RUN-ID>/                          (keeps runs separate and traceable)
        Mandant_001/
            2003/
                Mandant_001_2003_Handelsrecht.xlsx
                Mandant_001_2003_Steuerrecht.xlsx
```

- Filename always contains client + fiscal year + department; run id is carried by the directory (and in the job record, which maps file ⇄ job ⇄ run ⇄ attempts ⇄ checksum).
- Export writes to `*.attemptN.tmp`, then atomic rename after validation + checksum — a crash never leaves a plausible-looking half file under a final name.
- Existing final file ⇒ new file gets `_rN` suffix; **nothing is ever overwritten silently**. Validated successes are skipped upstream (idempotency), so collisions occur only in unusual retry situations and remain visible.
- **Original preservation:** the engine never opens the export for writing; validation is read-only; checksum is computed on the original; any ViewBox/migration transformation must produce a *derived copy elsewhere*.
- SHA-256 stored with filename, size, timestamps, job + run id; re-checked before skipping a completed job on restart, enabling later archive verification.
- Failure evidence (screenshots/diagnostics) goes to a separate diagnostics directory, never into the export tree.

---

## 11. Monitoring Design

Exposed via `GET /api/runs/{id}/status` and rendered live on the Angular dashboard (poll ~1.5 s):

- Progress: terminal/total + percent, counts per status (with data / empty / failed-final / open / retry).
- Current: Run ID, Job ID, human label (Client 041 / 2014 / Handelsrecht), attempt number, current action (SELECT_CLIENT … CHECKSUM), job start time.
- Performance: average runtime **from real completed jobs**, estimated remaining = average × open jobs (never the 3-minute assumption).
- Health: worker state, Wilken session status (Ready/Crashed/Restarting), last successful export, last error with timestamp.
- Event stream: latest structured log entries.

Operators supervise entirely from this surface; watching Wilken itself is never required.

---

## 12. Audit Report Design

**Summary** (`GET /api/runs/{id}/audit`): Run ID, run status, start/end, expected jobs, accounted jobs, reconciliation flag, counts per status, success rate. Reconciliation rule enforced: `SUCCESS_WITH_DATA + SUCCESS_EMPTY + FAILED_FINAL + PENDING + RUNNING + RETRY = EXPECTED`; any mismatch is flagged in the report and logged as ERROR at run completion. Completeness is computed from persistent job records, never by counting files.

**Detail rows** (JSON + downloadable CSV `audit.csv`, one row per job): Run ID, Job ID, Client, Fiscal Year, Department, Final Status, File Name, File Path, File Size, SHA-256, Export/Start/End Time, Runtime, Attempt Count, Validation Result + detail, Error Code + Message. `FAILED_FINAL` jobs are additionally listed in a dedicated section (and separately in the UI).

---

## 13. Pilot/Test Plan (8 jobs)

Scope: 2 clients × 2 fiscal years × 2 departments = 8 jobs (one click via the "Pilot preset" in the UI; expected count shown before generation).

1. Create pilot run, confirm expected = generated = 8; auto-start.
2. Let it run unattended to completion.
3. Manual comparison against Wilken for each file: correct client, year, department, evaluations included, plausible values.
4. Verify: file integrity (opens, structure), naming convention, empty-period handling (`SUCCESS_EMPTY` where Wilken really has no data), SHA-256 recorded and reproducible.
5. Fault injection:
   - Kill Wilken mid-job → session recovery, attempt retried, queue continues.
   - Kill the automation service mid-job → on restart the interrupted job is `RETRY`, completed jobs untouched (verify "does not restart from job 1").
   - Delete one completed export → resume re-queues exactly that job (`REVERIFY_REQUEUED`).
   - Force a job to exhaust retries → `FAILED_FINAL`, batch continues, job appears in audit's failed section, "retry failed" endpoint reprocesses it.
6. Review logs (every action present per job), monitoring accuracy (ETA from real runtimes), audit reconciliation = 8.
7. **Acceptance sign-off before production.** (The dummy-adapter version of this pilot has been executed successfully; the same script re-runs against the real adapter.)

## 14. Production Execution Plan

1. Freeze pilot-accepted configuration; create the production run: 78 × 23 × 2 → engine displays expected = generated = 3,588 (any deviation blocks the start decision).
2. Machine preparation: dedicated Windows machine/VM, auto-logon + automation as auto-restarting service (Task Scheduler "on failure" / Windows Service recovery), screen lock/screensaver/power saving disabled (GUI automation), Windows Update deferred for the run window, disk space forecast (3,588 × expected size × safety factor).
3. Ramp-up: first ~50 jobs supervised loosely via dashboard; check average runtime against the 3-minute planning estimate to recalibrate the ETA (~180 h pure export time at 3 min/job, single worker).
4. Steady state: unattended 24/7; operators check dashboard periodically; FAILED_FINAL jobs reviewed daily and re-queued after cause analysis (targeted `retry-failed`, never full restarts).
5. Interruptions (Windows updates forced, power, network): no action needed beyond machine up + service running — engine resumes automatically, verified in pilot.
6. Completion: completeness check green (all 3,588 accounted), audit CSV archived together with the export tree and a checksum manifest; spot re-verification of SHA-256 on the archive.

## 15. Parallelization Readiness

Already built in (safe now):

- Stateless worker against a shared persistent queue — N workers is a scheduling change, not a redesign.
- Deterministic job IDs/filenames cannot collide between workers; per-run directory tree is partitionable.
- `WorkerCount` exists in configuration (enforced to 1 in v1).

Deliberately **not** enabled until verified in the real environment:

- Wilken multi-session capability on one machine (or need for multiple VMs) and license coverage for concurrent sessions.
- Database behavior under concurrent report generation; spool attribution when two sessions produce reports simultaneously (job-to-spool matching must stay unambiguous).
- Cross-session interference on client/year selection (some legacy apps keep such state per user profile, not per session).
- Job claiming (`SELECT ... FOR UPDATE`/optimistic concurrency) — trivial to add, pointless to risk in v1. Version 1 = one worker, reliability over throughput (~179 h theoretical at 3 min/job; 2 workers ≈ 90 h, 4 ≈ 45 h once verified).

## 16. Risks and Open Questions

**Confirmed requirements** — see list below.
**Technical unknowns / Wilken questions** — see list below.
**Environmental dependencies:** dedicated always-on Windows machine, no screen lock for GUI automation, stable DB connectivity, MySQL instance for the job store, disk capacity, deferred OS updates.
**Licensing dependencies:** Wilken license must permit long-running automated usage; concurrent sessions (future parallelization) need explicit license/vendor confirmation; DB read access may need vendor approval.

---

# Additional Output

## Confirmed Requirements

- Scope 78 clients × 23 fiscal years × 2 departments = 3,588 jobs; count computed dynamically from configuration.
- One export job per client+year+department; Commercial Law and Tax Law exported separately.
- Default order Client → Year → HR → ST; order configurable.
- Single worker in v1; architecture parallel-ready but not enabled.
- Persistent job state (MySQL) surviving all restart classes; completed validated jobs never regenerated; re-verified (existence + SHA-256) before skipping.
- Max 3 automatic attempts (configurable); FAILED_FINAL never stops the queue and stays reprocessable.
- State-based readiness detection; all timeouts configurable; no fixed 3-minute waits.
- Four-level validation; SUCCESS only after validation; SUCCESS_EMPTY distinct from failure and from FAILED.
- SHA-256 for every validated original; originals immutable; derived processing on copies only.
- No silent overwrites; unique deterministic Run/Job IDs; full traceability file ⇄ job ⇄ run.
- Structured logging of every action/error; monitoring without watching Wilken; completeness reconciliation; final audit report with FAILED_FINAL shown separately.
- 8-job pilot (2×2×2) with acceptance before production.

## Technical Unknowns (to investigate on the installed Wilken CS/2)

1. Existence/scope of API, web service, CLI, batch interface, internal scheduler (and license coverage).
2. UI technology (WinForms/Delphi/Java/…) and whether controls expose AutomationIds/names/classes — per screen: main window, client selection, Asset Accounting, year field, department field, execute, report status, spool list, export, save dialog, error dialogs.
3. Authentication flow, session timeout behavior, re-login procedure.
4. Report/spool mechanics: where spools live (UI-only? files? DB?), what identifiers/metadata they carry, how job→spool matching works, whether spools can be missed/purged.
5. Real export format(s) and internal structure: sheets, headers, totals, whether client/year/department appear inside the content (drives Level-2/3 validator implementation).
6. Which exact Asset Accounting evaluations must be included per export and whether one export covers all (additions, disposals, grids, account summaries) or several export actions per job are needed (would multiply steps per job, not jobs).
7. Whether an evaluation for a data-less period produces a file, an empty report, or an error dialog (drives SUCCESS_EMPTY detection).
8. Database vendor/schema and vendor stance on read-only cross-validation queries.
9. Concurrent session support + licensing (future parallelization).
10. Typical report runtimes per year size (calibrates timeouts and ETA).
11. Behavior of the save/export dialog (path typing allowed? overwrite prompts?).

## Proof-of-Concept Tasks (technician checklist)

1. Inspect Wilken controls with Accessibility Insights / Inspect.exe / FlaUInspect / AutoIt Window Info; record per-screen findings (AutomationId/name/class/handle) in the integration assessment table.
2. Automate application start + login + session reuse detection.
3. Select one test client programmatically and **read back** the selection.
4. Navigate to Asset Accounting; detect target screen by property, not by sleep.
5. Set one fiscal year; verify by read-back.
6. Select Commercial Law; verify.
7. Execute one report.
8. Detect report/spool readiness purely by state polling (log the polling trace).
9. Export without any fixed wait; save via typed path into the file dialog.
10. Validate the file (levels 1–4 as far as the real format allows) + SHA-256.
11. Repeat for Tax Law.
12. Kill Wilken mid-report; verify automatic session recovery and job retry.
13. Kill the automation process mid-job; restart; verify persistence (interrupted job RETRY, done jobs skipped).

## Risks

| Risk | Level | Mitigation |
| ---- | ----- | ---------- |
| Wilken UI not exposed to UI Automation (owner-drawn legacy controls, esp. spool grid) | HIGH | POC inspection first; fallback chain keyboard → OCR-with-anchor; coordinates only as documented last resort; if UI proves unautomatable, escalate to vendor for batch/CLI path |
| Spool cannot be reliably matched to job (wrong report exported) | HIGH | Never take "newest spool" blindly; match on metadata; Level-2 content validation as final gate (WRONG_* abort); serialize report execution (v1 single worker guarantees one in-flight report) |
| Export content lacks client/year/department markers → Level-2 impossible | MEDIUM | Fall back to controlled-context guarantee (verified selections + serialized execution) + structural/record validation; document residual risk in audit |
| Long multi-day run interrupted by Windows updates/reboots | MEDIUM | Restart-safe engine (proven); defer updates; auto-start service; machine monitoring |
| Session degradation over hundreds of jobs (memory leaks, slowdown) | MEDIUM | Session health checks; proactive scheduled session restart every N jobs (configurable); runtime trend visible in monitoring |
| Legitimate-empty vs. failed-empty misclassification | MEDIUM | Explicit rule: empty is success only if structure valid and app reported no error; pilot includes known-empty periods; audit lists SUCCESS_EMPTY separately for review |
| Disk exhaustion mid-run | LOW | Capacity forecast at ramp-up; free-space check before each export; alert in monitoring |
| Job-store DB outage | LOW | Worker loop degrades gracefully and resumes; transitions are atomic single-row updates |
| Checksum/archive tampering undetected | LOW | SHA-256 stored in DB + re-verified on resume; final manifest for the archive |
| License violation through 24/7 automation or later parallel sessions | MEDIUM | Clarify with Wilken/vendor before production; parallelization gated on explicit confirmation |
