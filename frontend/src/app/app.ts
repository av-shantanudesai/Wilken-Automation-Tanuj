import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApiService } from './core/api.service';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private router = inject(Router);

  get showShell(): boolean {
    return this.auth.isLoggedIn() && !this.router.url.startsWith('/login');
  }
}
