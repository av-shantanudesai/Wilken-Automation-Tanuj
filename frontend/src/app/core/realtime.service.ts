import { Injectable, effect, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
} from '@microsoft/signalr';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

/** SignalR events broadcast by the WilkenAutomation backend. */
const HUB_EVENTS = [
  'JobStarted',
  'JobStatusChanged',
  'JobApplicationStateChanged',
  'JobCompleted',
  'JobFailed',
  'JobRetrying',
  'RunProgressChanged',
  'DashboardSummaryChanged',
  'WorkerStatusChanged',
  'WilkenSessionChanged',
  'LastErrorChanged',
  'LastSuccessChanged',
  'RunsChanged',
] as const;

export type HubEvent = (typeof HUB_EVENTS)[number];

/**
 * Real-time channel to the backend (backend mode only). Job events are delivered
 * only after SubscribeRun for a run the user owns.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  readonly connected = signal(false);
  private auth = inject(AuthService);

  private connection: HubConnection | null = null;
  private listeners = new Set<(event: HubEvent, payload: unknown) => void>();
  private subscribedRunId: string | null = null;

  constructor() {
    // Drop the live connection on logout. Done here (not in AuthService) to
    // avoid a DI cycle: this service already depends on AuthService.
    effect(() => {
      if (!this.auth.isLoggedIn()) void this.disconnect();
    });
  }

  connect(): void {
    if (this.connection) return;

    this.connection = new HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: () => this.auth.token() ?? '',
        withCredentials: true,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retry) =>
          Math.min(30_000, 1000 * 2 ** Math.min(retry.previousRetryCount, 5)),
      })
      .build();

    for (const event of HUB_EVENTS) {
      this.connection.on(event, (payload: unknown) => {
        for (const listener of this.listeners) listener(event, payload);
      });
    }

    this.connection.onreconnected(() => {
      this.connected.set(true);
      void this.resubscribe();
    });
    this.connection.onclose(() => {
      this.connected.set(false);
      this.scheduleReconnect();
    });

    this.startConnection();
  }

  private startConnection(): void {
    this.connection
      ?.start()
      .then(() => {
        this.connected.set(true);
        void this.resubscribe();
      })
      .catch(() => {
        this.connected.set(false);
        this.scheduleReconnect();
      });
  }

  private scheduleReconnect(): void {
    window.setTimeout(() => {
      if (this.connection && this.connection.state === HubConnectionState.Disconnected) {
        this.startConnection();
      }
    }, 5_000);
  }

  async subscribeToRun(runId: string | null): Promise<void> {
    if (this.subscribedRunId === runId) return;
    const previous = this.subscribedRunId;
    this.subscribedRunId = runId;
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;

    if (previous) {
      await this.connection.invoke('UnsubscribeRun', previous).catch(() => undefined);
    }
    if (runId) {
      await this.connection.invoke('SubscribeRun', runId).catch(() => undefined);
    }
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    this.subscribedRunId = null;
    this.connected.set(false);
    if (connection && connection.state !== HubConnectionState.Disconnected) {
      await connection.stop().catch(() => undefined);
    }
  }

  /** Subscribe to all hub events; returns an unsubscribe function. */
  subscribe(listener: (event: HubEvent, payload: unknown) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  private async resubscribe(): Promise<void> {
    const runId = this.subscribedRunId;
    if (!runId || !this.connection || this.connection.state !== HubConnectionState.Connected) return;
    await this.connection.invoke('SubscribeRun', runId).catch(() => undefined);
  }
}
