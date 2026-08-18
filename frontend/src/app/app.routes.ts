import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'dashboard',
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.DashboardPage),
  },
  {
    path: 'new-run',
    loadComponent: () => import('./features/new-run/new-run').then((m) => m.NewRunPage),
  },
  {
    path: 'jobs',
    loadComponent: () => import('./features/jobs/jobs').then((m) => m.JobsPage),
  },
  {
    path: 'audit',
    loadComponent: () => import('./features/audit/audit').then((m) => m.AuditPage),
  },
  { path: '**', redirectTo: 'dashboard' },
];
