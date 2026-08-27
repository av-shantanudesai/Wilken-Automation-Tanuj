import { ChangeDetectionStrategy, Component, OnDestroy, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { RealtimeService } from '../../core/realtime.service';
import { describeError } from '../../core/errors';
import { shortSha } from '../../core/format';
import { Job, JobDetail, JobStatus } from '../../core/models';

@Component({
  selector: 'app-jobs',
  imports: [DatePipe, DecimalPipe, FormsModule],
  templateUrl: './jobs.html',
  styleUrl: './jobs.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobsPage implements OnDestroy {
  readonly api = inject(ApiService);
  readonly realtime = inject(RealtimeService);

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
  private reloadQueued: ReturnType<typeof setTimeout> | null = null;
  private pollTicks = 0;
  private unsubscribeRealtime: (() => void) | null = null;

  constructor() {
    this.load();
    this.timer = setInterval(() => this.poll(), 3000);
    if (this.api.mode() === 'backend') {
      this.realtime.connect();
      const runId = this.api.selectedRunId();
      if (runId) void this.realtime.subscribeToRun(runId);
      this.unsubscribeRealtime = this.realtime.subscribe(() => this.queueReload());
    }
  }

  private poll(): void {
    this.pollTicks++;
    const live = this.api.mode() === 'backend' && this.realtime.connected();
    if (!live || this.pollTicks % 5 === 0) void this.load();
  }

  /** Debounces bursts of SignalR events into a single reload. */
  private queueReload(): void {
    if (this.reloadQueued) return;
    this.reloadQueued = setTimeout(() => {
      this.reloadQueued = null;
      void this.load();
    }, 300);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
    if (this.reloadQueued) clearTimeout(this.reloadQueued);
    this.unsubscribeRealtime?.();
  }

  async load(): Promise<void> {
    const runId = this.api.selectedRunId();
    if (!runId) {
      this.jobs.set([]);
      this.total.set(0);
      void this.realtime.subscribeToRun(null);
      return;
    }
    void this.realtime.subscribeToRun(runId);
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
    try {
      this.detail.set(await this.api.getJob(job.id));
    } catch (e) {
      this.error.set(describeError(e));
    }
  }

  async requeue(job: Job, event: Event): Promise<void> {
    event.stopPropagation();
    try {
      await this.api.requeueJob(job.id);
      await this.load();
    } catch (e) {
      this.error.set(describeError(e));
    }
  }

  readonly shortSha = shortSha;
}
