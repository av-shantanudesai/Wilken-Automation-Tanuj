import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { describeError } from '../../core/errors';
import { shortSha } from '../../core/format';
import { API_BASE_URL } from '../../core/http-export-api';
import { AuditReport } from '../../core/models';

@Component({
  selector: 'app-audit',
  imports: [DatePipe, DecimalPipe],
  templateUrl: './audit.html',
  styleUrl: './audit.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditPage {
  readonly api = inject(ApiService);
  private http = inject(HttpClient);
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
      // HttpClient (instead of raw fetch) so the auth interceptor attaches the
      // Bearer token and transparently refreshes it after expiry.
      void firstValueFrom(
        this.http.get(`${API_BASE_URL}/runs/${report.runId}/audit.csv`, { responseType: 'blob' }),
      ).then((blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${report.runId}_audit.csv`;
        a.click();
        URL.revokeObjectURL(url);
      }).catch((e) => this.error.set(describeError(e)));
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

  readonly shortSha = shortSha;
}
