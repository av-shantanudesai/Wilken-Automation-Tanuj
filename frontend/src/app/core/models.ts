export type RunStatus = 'Created' | 'Running' | 'Paused' | 'Completed';

export type JobStatus =
  | 'Pending'
  | 'Running'
  | 'Retry'
  | 'SuccessWithData'
  | 'SuccessEmpty'
  | 'Failed'
  | 'FailedFinal';

export type ValidationOutcome = 'NotValidated' | 'Valid' | 'ValidEmpty' | 'Invalid';

export interface StatusCounts {
  total: number;
  pending: number;
  running: number;
  retry: number;
  successWithData: number;
  successEmpty: number;
  failedFinal: number;
  terminal: number;
  open: number;
}

export interface RunSummary {
  id: string;
  status: RunStatus;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  expectedJobCount: number;
  generatedJobCount: number;
  jobCountDeviation: boolean;
  notes: string | null;
  counts: StatusCounts;
}

export interface RunStatusInfo {
  runId: string;
  runStatus: RunStatus;
  expectedJobCount: number;
  counts: StatusCounts;
  progressPercent: number;
  averageDurationMs: number | null;
  estimatedRemainingMs: number | null;
  workerState: string;
  wilkenSessionStatus: string;
  currentJobId: string | null;
  currentJobLabel: string | null;
  currentAttempt: number | null;
  currentAction: string | null;
  currentJobStartedAt: string | null;
  lastSuccessJobId: string | null;
  lastSuccessAt: string | null;
  lastError: string | null;
  lastErrorAt: string | null;
}

export interface Job {
  id: string;
  runId: string;
  client: string;
  fiscalYear: number;
  department: string;
  departmentCode: string;
  orderIndex: number;
  status: JobStatus;
  attemptCount: number;
  startTime: string | null;
  endTime: string | null;
  durationMs: number | null;
  fileName: string | null;
  filePath: string | null;
  fileSizeBytes: number | null;
  sha256: string | null;
  validationOutcome: ValidationOutcome;
  validationDetail: string | null;
  recordCount: number | null;
  errorCode: string | null;
  errorMessage: string | null;
  screenshotPath: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface LogEntry {
  id: number;
  timestamp: string;
  runId: string;
  jobId: string | null;
  client: string | null;
  fiscalYear: number | null;
  department: string | null;
  level: string;
  action: string;
  attempt: number | null;
  durationMs: number | null;
  errorCode: string | null;
  message: string;
}

export interface AuditReport {
  runId: string;
  runStatus: RunStatus;
  startedAt: string | null;
  completedAt: string | null;
  expectedJobs: number;
  accountedJobs: number;
  reconciles: boolean;
  counts: StatusCounts;
  successRatePercent: number;
  failedFinalJobs: Job[];
  jobs: Job[];
}

export interface PagedResult<T> {
  total: number;
  page: number;
  pageSize: number;
  items: T[];
}

export interface SimulationOptions {
  minJobSeconds: number;
  maxJobSeconds: number;
  failureRate: number;
  crashRate: number;
  emptyRate: number;
}

export interface CreateRunRequest {
  clients?: string[];
  clientCount?: number;
  years?: number[];
  yearFrom?: number;
  yearTo?: number;
  departments?: string[];
  jobOrder?: string;
  maxAttempts?: number;
  enableContentValidation?: boolean;
  enableChecksum?: boolean;
  simulation?: SimulationOptions;
  notes?: string;
  autoStart?: boolean;
}

export interface JobQuery {
  runId?: string;
  status?: JobStatus;
  client?: string;
  fiscalYear?: number;
  department?: string;
  page?: number;
  pageSize?: number;
}

export interface JobDetail {
  job: Job;
  logs: LogEntry[];
}
