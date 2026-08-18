import { Injectable, inject, signal } from '@angular/core';
import { ExportApi } from './export-api';
import { HttpExportApi } from './http-export-api';
import { MockExportApi } from './mock-export-api';
import { AuthService } from './auth.service';
import {
  AuditReport,
  CreateRunRequest,
  Job,
  JobDetail,
  JobQuery,
  LogEntry,
  PagedResult,
  RunStatusInfo,
  RunSummary,
} from './models';

export type ApiMode = 'mock' | 'backend';

const MODE_KEY = 'wilken-api-mode';
const RUN_KEY = 'wilken-selected-run';

/**
 * Facade every page talks to. Switches between the in-browser mock engine
 * (frontend-only testing) and the .NET backend at runtime.
 */
@Injectable({ providedIn: 'root' })
export class ApiService implements ExportApi {
  private mockApi = inject(MockExportApi);
  private httpApi = inject(HttpExportApi);
  private auth = inject(AuthService);

  readonly mode = signal<ApiMode>((localStorage.getItem(MODE_KEY) as ApiMode) || 'mock');
  readonly selectedRunId = signal<string | null>(localStorage.getItem(RUN_KEY));

  setMode(mode: ApiMode): void {
    this.mode.set(mode);
    localStorage.setItem(MODE_KEY, mode);
    this.selectRun(null);
    this.auth.logout();
  }

  selectRun(runId: string | null): void {
    this.selectedRunId.set(runId);
    if (runId) localStorage.setItem(RUN_KEY, runId);
    else localStorage.removeItem(RUN_KEY);
  }

  private get api(): ExportApi {
    return this.mode() === 'mock' ? this.mockApi : this.httpApi;
  }

  listRuns(): Promise<RunSummary[]> { return this.api.listRuns(); }
  createRun(request: CreateRunRequest): Promise<RunSummary> { return this.api.createRun(request); }
  startRun(id: string): Promise<RunSummary> { return this.api.startRun(id); }
  pauseRun(id: string): Promise<RunSummary> { return this.api.pauseRun(id); }
  retryFailed(id: string): Promise<RunSummary> { return this.api.retryFailed(id); }
  getStatus(id: string): Promise<RunStatusInfo> { return this.api.getStatus(id); }
  getAudit(id: string): Promise<AuditReport> { return this.api.getAudit(id); }
  listJobs(query: JobQuery): Promise<PagedResult<Job>> { return this.api.listJobs(query); }
  getJob(id: string): Promise<JobDetail> { return this.api.getJob(id); }
  requeueJob(id: string): Promise<Job> { return this.api.requeueJob(id); }
  getLogs(runId?: string, jobId?: string, limit?: number): Promise<LogEntry[]> {
    return this.api.getLogs(runId, jobId, limit);
  }
}
