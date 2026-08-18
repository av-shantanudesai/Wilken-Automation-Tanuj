import { Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
} from '@microsoft/signalr';
import { AuthService } from './auth.service';

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

const HUB_URL = 'http://localhost:5210/hubs/job-monitoring';

/**
 * Real-time channel to the backend (backend mode only). The REST API remains
 * the source of truth; events are used as refresh triggers so the dashboard
 * updates immediately instead of waiting for the next poll.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  readonly connected = signal(false);
  private auth = inject(AuthService);

  private connection: HubConnection | null = null;
  private listeners = new Set<(event: HubEvent, payload: unknown) => void>();

  connect(): void {
    if (this.connection) return;

    this.connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        accessTokenFactory: () => this.auth.token() ?? '',
      })
      .withAutomaticReconnect()
      .build();

    for (const event of HUB_EVENTS) {
      this.connection.on(event, (payload: unknown) => {
        for (const listener of this.listeners) listener(event, payload);
      });
    }

    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onclose(() => this.connected.set(false));

    this.connection
      .start()
      .then(() => this.connected.set(true))
      .catch(() => this.connected.set(false));
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
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
}
