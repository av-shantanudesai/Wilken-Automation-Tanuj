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

/**
 * Contract used by every page. Implemented twice:
 *  - HttpExportApi   -> talks to the .NET backend
 *  - MockExportApi   -> full in-browser simulation for frontend-only testing
 */
export interface ExportApi {
  listRuns(): Promise<RunSummary[]>;
  createRun(request: CreateRunRequest): Promise<RunSummary>;
  startRun(id: string): Promise<RunSummary>;
  pauseRun(id: string): Promise<RunSummary>;
  retryFailed(id: string): Promise<RunSummary>;
  getStatus(id: string): Promise<RunStatusInfo>;
  getAudit(id: string): Promise<AuditReport>;
  listJobs(query: JobQuery): Promise<PagedResult<Job>>;
  getJob(id: string): Promise<JobDetail>;
  requeueJob(id: string): Promise<Job>;
  getLogs(runId?: string, jobId?: string, limit?: number): Promise<LogEntry[]>;
}
