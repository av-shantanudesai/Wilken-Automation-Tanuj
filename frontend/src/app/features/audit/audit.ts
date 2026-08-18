import { Component, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { AuditReport } from '../../core/models';
import { API_BASE_URL } from '../../core/http-export-api';
import { describeError } from '../../core/errors';

@Component({
  selector: 'app-audit',
  imports: [DatePipe, DecimalPipe],
  templateUrl: './audit.html',
  styleUrl: './audit.scss',
})
export class AuditPage {
  readonly api = inject(ApiService);
  readonly report = signal<AuditReport | null>(null);
  readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  async load(): Promise<void> {
    const runId = this.api.selectedRunId();
    if (!runId) return;
    try {
      this.report.set(await this.api.getAudit(runId));
      this.error.set(null);
    } catch (e) {
      this.error.set(describeError(e));
    }
  }

  downloadCsv(): void {
    const report = this.report();
    if (!report) return;

    if (this.api.mode() === 'backend') {
      window.open(`${API_BASE_URL}/runs/${report.runId}/audit.csv`, '_blank');
      return;
    }

    // Mock mode: build the CSV client-side.
    const header =
      'RunId;JobId;Client;FiscalYear;Department;FinalStatus;AttemptCount;FileName;FilePath;FileSizeBytes;Sha256;StartTime;EndTime;DurationMs;ValidationOutcome;RecordCount;ErrorCode;ErrorMessage';
    const rows = report.jobs.map((j) =>
      [
        j.runId, j.id, j.client, j.fiscalYear, j.department, j.status, j.attemptCount,
        j.fileName ?? '', j.filePath ?? '', j.fileSizeBytes ?? '', j.sha256 ?? '',
        j.startTime ?? '', j.endTime ?? '', j.durationMs ?? '', j.validationOutcome,
        j.recordCount ?? '', j.errorCode ?? '', (j.errorMessage ?? '').replace(/;/g, ','),
      ].join(';'),
    );
    const blob = new Blob([[header, ...rows].join('\n')], { type: 'text/csv' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `${report.runId}_audit.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  }

  shortSha(sha: string | null): string {
    return sha ? `${sha.slice(0, 12)}…` : '–';
  }
}
