import { Injectable, inject } from '@angular/core';
import { AuthService } from './auth.service';
import { ExportApi } from './export-api';
import {
  AuditReport,
  CreateRunRequest,
  Job,
  JobDetail,
  JobQuery,
  JobStatus,
  LogEntry,
  PagedResult,
  RunStatusInfo,
  RunSummary,
  SimulationOptions,
  StatusCounts,
} from './models';

interface MockRunConfig {
  maxAttempts: number;
  simulation: SimulationOptions;
}

interface MockRun {
  id: string;
  userId: number;
  status: RunSummary['status'];
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  expectedJobCount: number;
  generatedJobCount: number;
  notes: string | null;
  config: MockRunConfig;
  jobs: Job[];
}

interface MockState {
  runs: MockRun[];
  logs: LogEntry[];
  logSeq: number;
}

interface ActiveWork {
  runId: string;
  jobId: string;
  startedAtMs: number;
  durationMs: number;
  outcome: 'success' | 'empty' | 'failure' | 'crash';
}

const STORAGE_KEY = 'wilken-mock-state-v1';
const DEPARTMENTS_DEFAULT = ['Handelsrecht', 'Steuerrecht'];

/**
 * Frontend-only simulation of the complete export engine. Runs entirely in the
 * browser and persists its job state to localStorage, so refreshing the page
 * demonstrates the same restart/recovery semantics as the real backend:
 * interrupted RUNNING jobs are re-queued and completed jobs are never repeated.
 */
@Injectable({ providedIn: 'root' })
export class MockExportApi implements ExportApi {
  private auth = inject(AuthService);
  private state: MockState;
  private active: ActiveWork | null = null;
  private sessionStatus = 'NotStarted';
  private currentAction: string | null = null;
  private lastSuccessJobId: string | null = null;
  private lastSuccessAt: string | null = null;
  private lastError: string | null = null;
  private lastErrorAt: string | null = null;
  private recovering = false;

  constructor() {
    this.state = this.load();
    this.recoverStaleJobs();
    setInterval(() => this.tick(), 250);
  }

  // ---------------------------------------------------------------- engine

  private tick(): void {
    if (this.recovering) return;

    const run = this.state.runs.find((r) => r.status === 'Running');
    if (!run) {
      this.currentAction = null;
      this.active = null;
      return;
    }
    if (this.sessionStatus !== 'Ready' && this.sessionStatus !== 'Crashed') {
      this.sessionStatus = 'Ready';
    }

    if (this.active) {
      this.progressActiveJob(run);
      return;
    }

    const next = run.jobs
      .filter((j) => j.status === 'Pending' || j.status === 'Retry')
      .sort((a, b) => a.orderIndex - b.orderIndex)[0];

    if (!next) {
      if (!run.jobs.some((j) => j.status === 'Running')) this.completeRun(run);
      return;
    }

    this.startJob(run, next);
  }

  private startJob(run: MockRun, job: Job): void {
    const sim = run.config.simulation;
    job.attemptCount++;
    job.status = 'Running';
    job.startTime = new Date().toISOString();
    job.endTime = null;
    job.errorCode = null;
    job.errorMessage = null;
    job.updatedAt = job.startTime;

    const durationMs =
      (sim.minJobSeconds + Math.random() * Math.max(0, sim.maxJobSeconds - sim.minJobSeconds)) * 1000;

    const roll = Math.random();
    let outcome: ActiveWork['outcome'];
    if (roll < sim.crashRate) outcome = 'crash';
    else if (roll < sim.crashRate + sim.failureRate) outcome = 'failure';
    else if (roll < sim.crashRate + sim.failureRate + sim.emptyRate) outcome = 'empty';
    else outcome = 'success';

    this.active = { runId: run.id, jobId: job.id, startedAtMs: Date.now(), durationMs, outcome };
    this.log(run.id, job, 'INFO', 'START', `Attempt ${job.attemptCount}/${run.config.maxAttempts} started.`);
    this.save();
  }

  private progressActiveJob(run: MockRun): void {
    const work = this.active!;
    const job = run.jobs.find((j) => j.id === work.jobId);
    if (!job) {
      this.active = null;
      return;
    }

    const fraction = (Date.now() - work.startedAtMs) / work.durationMs;
    this.currentAction =
      fraction < 0.08 ? 'PREPARE_SESSION'
      : fraction < 0.14 ? 'SELECT_CLIENT'
      : fraction < 0.2 ? 'SET_FISCAL_YEAR'
      : fraction < 0.26 ? 'SELECT_DEPARTMENT'
      : fraction < 0.32 ? 'EXECUTE_REPORT'
      : fraction < 0.7 ? 'WAIT_SPOOL'
      : fraction < 0.82 ? 'EXPORT'
      : fraction < 0.92 ? 'VALIDATE'
      : 'CHECKSUM';

    if (work.outcome === 'crash' && fraction >= 0.45) {
      this.sessionStatus = 'Crashed';
      this.failAttempt(run, job, 'WILKEN_CRASH', 'Simulated Wilken CS/2 crash during report processing.');
      this.recoverSession(run, job);
      return;
    }

    if (fraction < 1) return;

    if (work.outcome === 'failure') {
      this.failAttempt(run, job, 'REPORT_FAILED', 'Simulated Wilken report generation error (spool marked faulty).');
      return;
    }

    // Success path: validate, checksum, finalize.
    const records = work.outcome === 'empty' ? 0 : 10 + Math.floor(Math.random() * 490);
    const suffix = job.attemptCount > 1 ? `_r${job.attemptCount}` : '';
    job.fileName = `Mandant_${job.client}_${job.fiscalYear}_${job.department}${suffix}.csv`;
    job.filePath = `Wilken_Export/${run.id}/Mandant_${job.client}/${job.fiscalYear}/${job.fileName}`;
    job.fileSizeBytes = 420 + records * 92;
    job.sha256 = this.fakeSha256();
    job.recordCount = records;
    job.validationOutcome = records === 0 ? 'ValidEmpty' : 'Valid';
    job.validationDetail =
      records === 0
        ? 'All validation levels passed; period legitimately contains no data.'
        : `All validation levels passed; ${records} records.`;
    job.status = records === 0 ? 'SuccessEmpty' : 'SuccessWithData';
    job.endTime = new Date().toISOString();
    job.durationMs = Math.round(work.durationMs);
    job.updatedAt = job.endTime;

    this.log(run.id, job, 'INFO', 'VALIDATE', job.validationDetail);
    this.log(run.id, job, 'INFO', 'CHECKSUM', `SHA-256 ${job.sha256}`);
    this.log(run.id, job, 'INFO', 'SUCCESS',
      `${job.status} in ${job.durationMs} ms, file ${job.fileName} (${job.fileSizeBytes} bytes).`);
    this.lastSuccessJobId = job.id;
    this.lastSuccessAt = job.endTime;
    this.active = null;
    this.save();
  }

  private failAttempt(run: MockRun, job: Job, code: string, message: string): void {
    job.errorCode = code;
    job.errorMessage = message;
    job.endTime = new Date().toISOString();
    job.durationMs = Date.now() - (this.active?.startedAtMs ?? Date.now());
    job.updatedAt = job.endTime;
    job.screenshotPath = `Wilken_Diagnostics/${job.id}_attempt${job.attemptCount}.png`;

    const retryAllowed = job.attemptCount < run.config.maxAttempts;
    job.status = retryAllowed ? 'Retry' : 'FailedFinal';
    this.log(run.id, job, 'ERROR', retryAllowed ? 'FAILED_ATTEMPT' : 'FAILED_FINAL', `[${code}] ${message}`, code);
    this.lastError = `${job.id}: ${message}`;
    this.lastErrorAt = job.endTime;
    this.active = null;
    this.save();
  }

  private recoverSession(run: MockRun, job: Job): void {
    this.recovering = true;
    this.currentAction = 'RECOVER_SESSION';
    this.log(run.id, job, 'WARN', 'RECOVER_SESSION', 'Session lost - restarting Wilken session.');
    this.sessionStatus = 'Restarting';
    setTimeout(() => {
      this.sessionStatus = 'Ready';
      this.recovering = false;
    }, 1200);
  }

  private completeRun(run: MockRun): void {
    run.status = 'Completed';
    run.completedAt = new Date().toISOString();
    const counts = this.countsOf(run);
    const reconciles = counts.total === run.expectedJobCount;
    this.log(run.id, null, reconciles ? 'INFO' : 'ERROR', 'RUN_COMPLETED',
      `Run finished. Expected=${run.expectedJobCount}, Accounted=${counts.total}. ` +
      (reconciles ? 'Completeness check passed.' : 'COMPLETENESS MISMATCH - investigate!'));
    this.save();
  }

  /** Restart capability: jobs interrupted by a page reload are re-queued. */
  private recoverStaleJobs(): void {
    let recovered = 0;
    for (const run of this.state.runs) {
      for (const job of run.jobs) {
        if (job.status === 'Running') {
          job.status = job.attemptCount >= run.config.maxAttempts ? 'FailedFinal' : 'Retry';
          job.errorCode = 'INTERRUPTED';
          job.errorMessage = 'Job was interrupted by an automation restart and has been re-queued.';
          job.updatedAt = new Date().toISOString();
          this.log(run.id, job, 'WARN', 'RECOVERED_STALE', job.errorMessage);
          recovered++;
        }
      }
    }
    if (recovered > 0) this.save();
  }

  // ---------------------------------------------------------------- ExportApi

  async listRuns(): Promise<RunSummary[]> {
    return [...this.ownedRuns()]
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
      .map((r) => this.toSummary(r));
  }

  async createRun(request: CreateRunRequest): Promise<RunSummary> {
    const clients =
      request.clients?.length
        ? request.clients
        : Array.from({ length: request.clientCount ?? 78 }, (_, i) => String(i + 1).padStart(3, '0'));

    let years: number[];
    if (request.years?.length) years = request.years;
    else {
      const from = request.yearFrom ?? 2003;
      const to = request.yearTo ?? 2025;
      years = Array.from({ length: to - from + 1 }, (_, i) => from + i);
    }

    const departments = request.departments?.length ? request.departments : DEPARTMENTS_DEFAULT;
    const dedupe = <T>(arr: T[]) => [...new Set(arr)];
    const c = dedupe(clients);
    const y = dedupe(years);
    const d = dedupe(departments);

    const today = new Date().toISOString().slice(0, 10).replace(/-/g, '');
    const seq = this.state.runs.filter((r) => r.id.startsWith(`RUN-${today}-`)).length + 1;
    const runId = `RUN-${today}-${String(seq).padStart(3, '0')}`;
    const now = new Date().toISOString();

    const jobs: Job[] = [];
    let index = 0;
    for (const client of c) {
      for (const year of y) {
        for (const department of d) {
          const code = department === 'Handelsrecht' ? 'HR' : department === 'Steuerrecht' ? 'ST'
            : department.slice(0, 2).toUpperCase();
          jobs.push({
            id: `${runId}-M${client}-${year}-${code}`,
            runId,
            client,
            fiscalYear: year,
            department,
            departmentCode: code,
            orderIndex: index++,
            status: 'Pending',
            attemptCount: 0,
            startTime: null,
            endTime: null,
            durationMs: null,
            fileName: null,
            filePath: null,
            fileSizeBytes: null,
            sha256: null,
            validationOutcome: 'NotValidated',
            validationDetail: null,
            recordCount: null,
            errorCode: null,
            errorMessage: null,
            screenshotPath: null,
            createdAt: now,
            updatedAt: now,
          });
        }
      }
    }

    const run: MockRun = {
      id: runId,
      userId: this.auth.user()?.id ?? 0,
      status: request.autoStart ? 'Running' : 'Created',
      createdAt: now,
      startedAt: request.autoStart ? now : null,
      completedAt: null,
      expectedJobCount: c.length * y.length * d.length,
      generatedJobCount: jobs.length,
      notes: request.notes ?? null,
      config: {
        maxAttempts: request.maxAttempts ?? 3,
        simulation: request.simulation ?? {
          minJobSeconds: 2,
          maxJobSeconds: 6,
          failureRate: 0.08,
          crashRate: 0.03,
          emptyRate: 0.1,
        },
      },
      jobs,
    };

    this.state.runs.push(run);
    this.log(runId, null, 'INFO', 'RUN_CREATED',
      `Run generated with ${jobs.length} jobs (expected ${run.expectedJobCount}).`);
    this.save();
    return this.toSummary(run);
  }

  async startRun(id: string): Promise<RunSummary> {
    const run = this.require(id);
    // Idempotency: completed jobs stay completed; only non-terminal work resumes.
    run.status = 'Running';
    run.startedAt ??= new Date().toISOString();
    run.completedAt = null;
    this.log(id, null, 'INFO', 'RUN_STARTED', 'Run started/resumed; previously completed jobs are skipped.');
    this.save();
    return this.toSummary(run);
  }

  async pauseRun(id: string): Promise<RunSummary> {
    const run = this.require(id);
    run.status = 'Paused';
    this.log(id, null, 'INFO', 'RUN_PAUSED', 'Run paused by operator.');
    this.save();
    return this.toSummary(run);
  }

  async retryFailed(id: string): Promise<RunSummary> {
    const run = this.require(id);
    for (const job of run.jobs) {
      if (job.status === 'FailedFinal') {
        job.status = 'Pending';
        job.attemptCount = 0;
        job.updatedAt = new Date().toISOString();
        this.log(id, job, 'INFO', 'REQUEUED', 'FAILED_FINAL job re-queued for targeted reprocessing.');
      }
    }
    if (run.status === 'Completed') {
      run.status = 'Running';
      run.completedAt = null;
    }
    this.save();
    return this.toSummary(run);
  }

  async getStatus(id: string): Promise<RunStatusInfo> {
    const run = this.require(id);
    const counts = this.countsOf(run);
    const durations = run.jobs
      .filter((j) => (j.status === 'SuccessWithData' || j.status === 'SuccessEmpty') && j.durationMs != null)
      .map((j) => j.durationMs!);
    const avg = durations.length ? durations.reduce((a, b) => a + b, 0) / durations.length : null;
    const activeJob = this.active && this.active.runId === id
      ? run.jobs.find((j) => j.id === this.active!.jobId) ?? null
      : null;

    return {
      runId: run.id,
      runStatus: run.status,
      expectedJobCount: run.expectedJobCount,
      counts,
      progressPercent: counts.total ? Math.round((10000 * counts.terminal) / counts.total) / 100 : 0,
      averageDurationMs: avg,
      estimatedRemainingMs: avg != null ? avg * counts.open : null,
      workerState: activeJob ? 'Processing' : run.status === 'Running' ? 'Idle' : 'Idle',
      wilkenSessionStatus: this.sessionStatus,
      currentJobId: activeJob?.id ?? null,
      currentJobLabel: activeJob
        ? `Client ${activeJob.client} / ${activeJob.fiscalYear} / ${activeJob.department}`
        : null,
      currentAttempt: activeJob?.attemptCount ?? null,
      currentAction: activeJob ? this.currentAction : null,
      currentJobStartedAt: activeJob?.startTime ?? null,
      lastSuccessJobId: this.lastSuccessJobId,
      lastSuccessAt: this.lastSuccessAt,
      lastError: this.lastError,
      lastErrorAt: this.lastErrorAt,
    };
  }

  async getAudit(id: string): Promise<AuditReport> {
    const run = this.require(id);
    const counts = this.countsOf(run);
    const jobs = [...run.jobs].sort((a, b) => a.orderIndex - b.orderIndex);
    return {
      runId: run.id,
      runStatus: run.status,
      startedAt: run.startedAt,
      completedAt: run.completedAt,
      expectedJobs: run.expectedJobCount,
      accountedJobs: counts.total,
      reconciles: counts.total === run.expectedJobCount,
      counts,
      successRatePercent: counts.total
        ? Math.round((10000 * (counts.successWithData + counts.successEmpty)) / counts.total) / 100
        : 0,
      failedFinalJobs: jobs.filter((j) => j.status === 'FailedFinal'),
      jobs,
    };
  }

  async listJobs(query: JobQuery): Promise<PagedResult<Job>> {
    let jobs = this.ownedRuns().flatMap((r) => r.jobs);
    if (query.runId) jobs = jobs.filter((j) => j.runId === query.runId);
    if (query.status) jobs = jobs.filter((j) => j.status === query.status);
    if (query.client) jobs = jobs.filter((j) => j.client === query.client);
    if (query.fiscalYear != null) jobs = jobs.filter((j) => j.fiscalYear === Number(query.fiscalYear));
    if (query.department) jobs = jobs.filter((j) => j.department === query.department);

    jobs.sort((a, b) => a.orderIndex - b.orderIndex);
    const page = Math.max(query.page ?? 1, 1);
    const pageSize = Math.min(Math.max(query.pageSize ?? 50, 1), 500);
    return {
      total: jobs.length,
      page,
      pageSize,
      items: jobs.slice((page - 1) * pageSize, page * pageSize),
    };
  }

  async getJob(id: string): Promise<JobDetail> {
    const job = this.ownedRuns().flatMap((r) => r.jobs).find((j) => j.id === id);
    if (!job) throw new Error(`Job ${id} not found`);
    return {
      job,
      logs: this.state.logs.filter((l) => l.jobId === id).sort((a, b) => a.id - b.id),
    };
  }

  async requeueJob(id: string): Promise<Job> {
    const job = this.ownedRuns().flatMap((r) => r.jobs).find((j) => j.id === id);
    if (!job) throw new Error(`Job ${id} not found`);
    job.status = 'Pending';
    job.attemptCount = 0;
    job.updatedAt = new Date().toISOString();
    this.log(job.runId, job, 'INFO', 'REQUEUED', 'Job manually re-queued by operator.');
    this.save();
    return job;
  }

  async getLogs(runId?: string, jobId?: string, limit = 100): Promise<LogEntry[]> {
    const ownedIds = new Set(this.ownedRuns().map((r) => r.id));
    let logs = this.state.logs.filter((l) => ownedIds.has(l.runId));
    if (runId) logs = logs.filter((l) => l.runId === runId);
    if (jobId) logs = logs.filter((l) => l.jobId === jobId);
    return [...logs].sort((a, b) => b.id - a.id).slice(0, limit);
  }

  async listExportDefinitions() {
    return [
      { name: 'Zugangsliste', type: 'SPOOL', module: 'Asset Accounting', displayName: 'Zugangsliste', requires: ['CLIENT', 'YEAR', 'ACCOUNTING_LAW'], format: 'XLSX' },
      { name: 'Anlagenspiegel', type: 'SPOOL', module: 'Asset Accounting', displayName: 'Anlagenspiegel nach Anlagen', requires: ['CLIENT', 'YEAR', 'ACCOUNTING_LAW'], format: 'XLSX' },
      { name: 'AlleAnlagenNachKontenVerdichtet', type: 'SPOOL', module: 'Asset Accounting', displayName: 'Alle Anlagen nach Konten verdichtet', requires: ['CLIENT', 'YEAR', 'ACCOUNTING_LAW'], format: 'XLSX' },
      { name: 'MasterData', type: 'VIEW', module: 'Asset Accounting', displayName: 'Asset master data', requires: ['CLIENT'], format: 'CSV' },
      { name: 'Bookings', type: 'VIEW', module: 'Asset Accounting', displayName: 'Bookings by period', requires: ['CLIENT', 'YEAR', 'PERIOD'], format: 'CSV' },
    ];
  }

  // ---------------------------------------------------------------- helpers

  private toSummary(run: MockRun): RunSummary {
    return {
      id: run.id,
      status: run.status,
      createdAt: run.createdAt,
      startedAt: run.startedAt,
      completedAt: run.completedAt,
      expectedJobCount: run.expectedJobCount,
      generatedJobCount: run.generatedJobCount,
      jobCountDeviation: run.expectedJobCount !== run.generatedJobCount,
      notes: run.notes,
      counts: this.countsOf(run),
    };
  }

  private countsOf(run: MockRun): StatusCounts {
    const of = (s: JobStatus) => run.jobs.filter((j) => j.status === s).length;
    const successWithData = of('SuccessWithData');
    const successEmpty = of('SuccessEmpty');
    const failedFinal = of('FailedFinal');
    const pending = of('Pending');
    const running = of('Running');
    const retry = of('Retry');
    return {
      total: run.jobs.length,
      pending,
      running,
      retry,
      successWithData,
      successEmpty,
      failedFinal,
      terminal: successWithData + successEmpty + failedFinal,
      open: pending + running + retry,
    };
  }

  private log(runId: string, job: Job | null, level: string, action: string, message: string, errorCode?: string): void {
    this.state.logs.push({
      id: ++this.state.logSeq,
      timestamp: new Date().toISOString(),
      runId,
      jobId: job?.id ?? null,
      client: job?.client ?? null,
      fiscalYear: job?.fiscalYear ?? null,
      department: job?.department ?? null,
      level,
      action,
      attempt: job?.attemptCount ?? null,
      durationMs: null,
      errorCode: errorCode ?? null,
      message,
    });
    if (this.state.logs.length > 5000) this.state.logs.splice(0, this.state.logs.length - 5000);
  }

  private require(id: string): MockRun {
    const run = this.ownedRuns().find((r) => r.id === id);
    if (!run) throw new Error(`Run ${id} not found`);
    return run;
  }

  private ownedRuns(): MockRun[] {
    const userId = this.auth.user()?.id;
    if (userId == null) return [];
    return this.state.runs.filter((r) => r.userId === userId);
  }

  private fakeSha256(): string {
    return Array.from({ length: 64 }, () => '0123456789abcdef'[Math.floor(Math.random() * 16)]).join('');
  }

  private load(): MockState {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const state = JSON.parse(raw) as MockState;
        for (const run of state.runs) run.userId ??= 0;
        return state;
      }
    } catch {
      /* corrupted state -> start fresh */
    }
    return { runs: [], logs: [], logSeq: 0 };
  }

  private save(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.state));
    } catch {
      /* quota exceeded -> keep running in memory */
    }
  }
}
