import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { describeError } from '../../core/errors';

@Component({
  selector: 'app-new-run',
  imports: [FormsModule],
  templateUrl: './new-run.html',
  styleUrl: './new-run.scss',
})
export class NewRunPage {
  private api = inject(ApiService);
  private router = inject(Router);

  clientCount = signal(2);
  yearFrom = signal(2003);
  yearTo = signal(2004);
  commercialLaw = signal(true);
  taxLaw = signal(true);
  maxAttempts = signal(3);
  autoStart = signal(true);
  notes = signal('');

  minJobSeconds = signal(1);
  maxJobSeconds = signal(3);
  failureRate = signal(0.08);
  crashRate = signal(0.03);
  emptyRate = signal(0.1);

  busy = signal(false);
  error = signal<string | null>(null);

  readonly departmentCount = computed(() => (this.commercialLaw() ? 1 : 0) + (this.taxLaw() ? 1 : 0));
  readonly yearCount = computed(() => Math.max(0, this.yearTo() - this.yearFrom() + 1));
  readonly expectedJobs = computed(() => this.clientCount() * this.yearCount() * this.departmentCount());

  applyPilot(): void {
    this.clientCount.set(2);
    this.yearFrom.set(2003);
    this.yearTo.set(2004);
    this.commercialLaw.set(true);
    this.taxLaw.set(true);
  }

  applyFull(): void {
    this.clientCount.set(78);
    this.yearFrom.set(2003);
    this.yearTo.set(2025);
    this.commercialLaw.set(true);
    this.taxLaw.set(true);
  }

  async create(): Promise<void> {
    if (this.expectedJobs() === 0) {
      this.error.set('Configuration yields zero jobs - select at least one client, year and department.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const departments = [
        ...(this.commercialLaw() ? ['Handelsrecht'] : []),
        ...(this.taxLaw() ? ['Steuerrecht'] : []),
      ];
      const run = await this.api.createRun({
        clientCount: this.clientCount(),
        yearFrom: this.yearFrom(),
        yearTo: this.yearTo(),
        departments,
        maxAttempts: this.maxAttempts(),
        autoStart: this.autoStart(),
        notes: this.notes() || undefined,
        simulation: {
          minJobSeconds: this.minJobSeconds(),
          maxJobSeconds: this.maxJobSeconds(),
          failureRate: this.failureRate(),
          crashRate: this.crashRate(),
          emptyRate: this.emptyRate(),
        },
      });
      this.api.selectRun(run.id);
      await this.router.navigate(['/dashboard']);
    } catch (e) {
      this.error.set(describeError(e));
    } finally {
      this.busy.set(false);
    }
  }
}
