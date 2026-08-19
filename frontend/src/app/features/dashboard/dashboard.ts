import { Component, OnDestroy, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { RealtimeService } from '../../core/realtime.service';
import { LogEntry, RunStatusInfo, RunSummary } from '../../core/models';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, DecimalPipe, RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardPage implements OnDestroy {
  readonly api = inject(ApiService);
  readonly realtime = inject(RealtimeService);

  readonly runs = signal<RunSummary[]>([]);
  readonly status = signal<RunStatusInfo | null>(null);
  readonly logs = signal<LogEntry[]>([]);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  private timer: ReturnType<typeof setInterval>;
  private refreshQueued: ReturnType<typeof setTimeout> | null = null;
  private unsubscribeRealtime: (() => void) | null = null;

  constructor() {
    this.refresh();
    // Polling fallback; SignalR events trigger immediate refreshes in backend mode.
    this.timer = setInterval(() => this.refresh(), 1500);

    if (this.api.mode() === 'backend') {
      this.realtime.connect();
      this.unsubscribeRealtime = this.realtime.subscribe(() => this.queueRefresh());
    }
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
    if (this.refreshQueued) clearTimeout(this.refreshQueued);
    this.unsubscribeRealtime?.();
  }

  /** Debounces bursts of SignalR events into a single refresh. */
  private queueRefresh(): void {
    if (this.refreshQueued) return;
    this.refreshQueued = setTimeout(() => {
      this.refreshQueued = null;
      this.refresh();
    }, 250);
  }

  async refresh(): Promise<void> {
    try {
      const runs = await this.api.listRuns();
      this.runs.set(runs);

      let selected = this.api.selectedRunId();
      if (!selected || !runs.some((r) => r.id === selected)) {
        selected = runs[0]?.id ?? null;
        this.api.selectRun(selected);
      }

      if (selected) {
        const [status, logs] = await Promise.all([
          this.api.getStatus(selected),
          this.api.getLogs(selected, undefined, 12),
        ]);
        this.status.set(status);
        this.logs.set(logs);
      } else {
        this.status.set(null);
        this.logs.set([]);
      }
      this.error.set(null);
    } catch (e) {
      this.error.set(this.describe(e));
    }
  }

  select(run: RunSummary): void {
    this.api.selectRun(run.id);
    this.refresh();
  }

  async start(): Promise<void> { await this.control((id) => this.api.startRun(id)); }
  async pause(): Promise<void> { await this.control((id) => this.api.pauseRun(id)); }
  async retryFailed(): Promise<void> { await this.control((id) => this.api.retryFailed(id)); }

  private async control(action: (id: string) => Promise<unknown>): Promise<void> {
    const id = this.api.selectedRunId();
    if (!id) return;
    this.busy.set(true);
    try {
      await action(id);
      await this.refresh();
    } catch (e) {
      this.error.set(this.describe(e));
    } finally {
      this.busy.set(false);
    }
  }

  formatMs(ms: number | null | undefined): string {
    if (ms == null) return '–';
    if (ms < 1000) return `${Math.round(ms)} ms`;
    const totalSeconds = Math.round(ms / 1000);
    const h = Math.floor(totalSeconds / 3600);
    const m = Math.floor((totalSeconds % 3600) / 60);
    const s = totalSeconds % 60;
    if (h > 0) return `${h}h ${m}m`;
    if (m > 0) return `${m}m ${s}s`;
    return `${s}s`;
  }

  private describe(e: unknown): string {
    if (e && typeof e === 'object' && 'message' in e) {
      const msg = String((e as { message: unknown }).message);
      return msg.includes('Http failure')
        ? `Backend not reachable at ${environment.apiBaseUrl} - start the API or switch to Mock mode.`
        : msg;
    }
    return String(e);
  }
}
