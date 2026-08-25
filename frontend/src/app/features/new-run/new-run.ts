import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { describeError } from '../../core/errors';
import { ExportDefinitionInfo } from '../../core/models';

@Component({
  selector: 'app-new-run',
  imports: [FormsModule],
  templateUrl: './new-run.html',
  styleUrl: './new-run.scss',
})
export class NewRunPage implements OnInit {
  private api = inject(ApiService);
  private router = inject(Router);

  clientCount = signal(2);
  clientsText = signal('02');
  wilkenExecutablePath = signal('');
  exportRootDirectory = signal('');
  yearFrom = signal(2003);
  yearTo = signal(2004);
  commercialLaw = signal(true);
  taxLaw = signal(true);
  selectedExports = signal<string[]>(['Zugangsliste', 'Anlagenspiegel']);
  definitions = signal<ExportDefinitionInfo[]>([]);
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
  readonly parsedClients = computed(() =>
    this.clientsText()
      .split(/[,;\s]+/)
      .map((c) => c.trim())
      .filter((c) => c.length > 0),
  );
  readonly effectiveClientCount = computed(() =>
    this.parsedClients().length > 0 ? this.parsedClients().length : this.clientCount(),
  );
  readonly expectedJobs = computed(() => {
    const clients = this.effectiveClientCount();
    const years = this.yearCount();
    const laws = [
      ...(this.commercialLaw() ? ['Handelsrecht'] : []),
      ...(this.taxLaw() ? ['Steuerrecht'] : []),
    ];
    let total = 0;
    for (const name of this.selectedExports()) {
      const def = this.definitions().find((d) => d.name === name);
      const requires = def?.requires ?? ['CLIENT', 'YEAR', 'ACCOUNTING_LAW'];
      const c = requires.includes('CLIENT') ? clients : 1;
      const y = requires.includes('YEAR') ? years : 1;
      const p = requires.includes('PERIOD') ? 1 : 1;
      let l = 1;
      if (requires.includes('ACCOUNTING_LAW')) {
        const preferred = name === 'Anlagenspiegel' || name === 'AlleAnlagenNachKontenVerdichtet'
          ? 'Steuerrecht'
          : name === 'Zugangsliste' ? 'Handelsrecht' : null;
        l = preferred ? (laws.includes(preferred) ? 1 : 0) : laws.length;
      }
      total += c * y * p * l;
    }
    return total;
  });

  async ngOnInit(): Promise<void> {
    try {
      this.definitions.set(await this.api.listExportDefinitions());
    } catch {
      this.definitions.set([
        { name: 'Zugangsliste', type: 'SPOOL', module: 'Asset Accounting', displayName: 'Zugangsliste', requires: ['CLIENT', 'YEAR', 'ACCOUNTING_LAW'], format: 'XLSX' },
        { name: 'Anlagenspiegel', type: 'SPOOL', module: 'Asset Accounting', displayName: 'Anlagenspiegel nach Anlagen', requires: ['CLIENT', 'YEAR', 'ACCOUNTING_LAW'], format: 'XLSX' },
        { name: 'AlleAnlagenNachKontenVerdichtet', type: 'SPOOL', module: 'Asset Accounting', displayName: 'Alle Anlagen nach Konten verdichtet', requires: ['CLIENT', 'YEAR', 'ACCOUNTING_LAW'], format: 'XLSX' },
        { name: 'MasterData', type: 'VIEW', module: 'Asset Accounting', displayName: 'Asset master data', requires: ['CLIENT'], format: 'CSV' },
        { name: 'Bookings', type: 'VIEW', module: 'Asset Accounting', displayName: 'Bookings by period', requires: ['CLIENT', 'YEAR', 'PERIOD'], format: 'CSV' },
      ]);
    }
  }

  toggleExport(name: string, on: boolean): void {
    const current = this.selectedExports();
    this.selectedExports.set(on ? [...new Set([...current, name])] : current.filter((n) => n !== name));
  }

  applyPilot(): void {
    this.clientCount.set(2);
    this.clientsText.set('001, 002');
    this.yearFrom.set(2003);
    this.yearTo.set(2004);
    this.commercialLaw.set(true);
    this.taxLaw.set(true);
  }

  applyFull(): void {
    this.clientCount.set(78);
    this.clientsText.set('');
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
      const clients = this.parsedClients();
      const run = await this.api.createRun({
        ...(clients.length > 0 ? { clients } : { clientCount: this.clientCount() }),
        yearFrom: this.yearFrom(),
        yearTo: this.yearTo(),
        departments,
        exportDefinitions: this.selectedExports(),
        maxAttempts: this.maxAttempts(),
        autoStart: this.autoStart(),
        notes: this.notes() || undefined,
        wilkenExecutablePath: this.wilkenExecutablePath().trim() || undefined,
        exportRootDirectory: this.exportRootDirectory().trim() || undefined,
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
