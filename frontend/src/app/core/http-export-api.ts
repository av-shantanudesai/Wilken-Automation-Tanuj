import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ExportApi } from './export-api';
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

export const API_BASE_URL = 'http://localhost:5210/api';

@Injectable({ providedIn: 'root' })
export class HttpExportApi implements ExportApi {
  private http = inject(HttpClient);
  private base = API_BASE_URL;

  listRuns(): Promise<RunSummary[]> {
    return firstValueFrom(this.http.get<RunSummary[]>(`${this.base}/runs`));
  }

  createRun(request: CreateRunRequest): Promise<RunSummary> {
    return firstValueFrom(this.http.post<RunSummary>(`${this.base}/runs`, request));
  }

  startRun(id: string): Promise<RunSummary> {
    return firstValueFrom(this.http.post<RunSummary>(`${this.base}/runs/${id}/start`, {}));
  }

  pauseRun(id: string): Promise<RunSummary> {
    return firstValueFrom(this.http.post<RunSummary>(`${this.base}/runs/${id}/pause`, {}));
  }

  retryFailed(id: string): Promise<RunSummary> {
    return firstValueFrom(this.http.post<RunSummary>(`${this.base}/runs/${id}/retry-failed`, {}));
  }

  getStatus(id: string): Promise<RunStatusInfo> {
    return firstValueFrom(this.http.get<RunStatusInfo>(`${this.base}/runs/${id}/status`));
  }

  getAudit(id: string): Promise<AuditReport> {
    return firstValueFrom(this.http.get<AuditReport>(`${this.base}/runs/${id}/audit`));
  }

  listJobs(query: JobQuery): Promise<PagedResult<Job>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return firstValueFrom(this.http.get<PagedResult<Job>>(`${this.base}/jobs`, { params }));
  }

  getJob(id: string): Promise<JobDetail> {
    return firstValueFrom(this.http.get<JobDetail>(`${this.base}/jobs/${id}`));
  }

  requeueJob(id: string): Promise<Job> {
    return firstValueFrom(this.http.post<Job>(`${this.base}/jobs/${id}/requeue`, {}));
  }

  getLogs(runId?: string, jobId?: string, limit = 100): Promise<LogEntry[]> {
    let params = new HttpParams().set('limit', limit);
    if (runId) params = params.set('runId', runId);
    if (jobId) params = params.set('jobId', jobId);
    return firstValueFrom(this.http.get<LogEntry[]>(`${this.base}/logs`, { params }));
  }
}
