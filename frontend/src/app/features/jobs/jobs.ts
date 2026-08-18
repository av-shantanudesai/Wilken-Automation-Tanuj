import { Component, OnDestroy, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { Job, JobDetail, JobStatus } from '../../core/models';
import { describeError } from '../../core/errors';

@Component({
  selector: 'app-jobs',
  imports: [DatePipe, DecimalPipe, FormsModule],
  templateUrl: './jobs.html',
  styleUrl: './jobs.scss',
})
export class JobsPage implements OnDestroy {
  readonly api = inject(ApiService);

  readonly statuses: JobStatus[] = [
    'Pending', 'Running', 'Retry', 'SuccessWithData', 'SuccessEmpty', 'FailedFinal',
  ];

  readonly jobs = signal<Job[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = 25;
  readonly detail = signal<JobDetail | null>(null);
  readonly error = signal<string | null>(null);

  statusFilter = signal<JobStatus | ''>('');
  clientFilter = signal('');
  yearFilter = signal<number | null>(null);

  private timer: ReturnType<typeof setInterval>;

  constructor() {
    this.load();
    this.timer = setInterval(() => this.load(), 3000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  async load(): Promise<void> {
    const runId = this.api.selectedRunId();
    if (!runId) {
      this.jobs.set([]);
      this.total.set(0);
      return;
    }
    try {
      const result = await this.api.listJobs({
        runId,
        status: this.statusFilter() || undefined,
        client: this.clientFilter() || undefined,
        fiscalYear: this.yearFilter() ?? undefined,
        page: this.page(),
        pageSize: this.pageSize,
      });
      this.jobs.set(result.items);
      this.total.set(result.total);
      this.error.set(null);
    } catch (e) {
      this.error.set(describeError(e));
    }
  }

  applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  totalPages(): number {
    return Math.max(1, Math.ceil(this.total() / this.pageSize));
  }

  goto(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.totalPages()));
    this.load();
  }

  async open(job: Job): Promise<void> {
    if (this.detail()?.job.id === job.id) {
      this.detail.set(null);
      return;
    }
    this.detail.set(await this.api.getJob(job.id));
  }

  async requeue(job: Job, event: Event): Promise<void> {
    event.stopPropagation();
    await this.api.requeueJob(job.id);
    await this.load();
  }

  shortSha(sha: string | null): string {
    return sha ? `${sha.slice(0, 12)}…` : '–';
  }
}
