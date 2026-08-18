import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { describeError } from '../../core/errors';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class LoginPage {
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  readonly registering = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  email = '';
  password = '';
  displayName = '';

  constructor() {
    if (this.auth.isLoggedIn()) {
      void this.router.navigateByUrl(this.returnUrl());
    }
  }

  async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      if (this.registering()) {
        await this.auth.register(this.email, this.password, this.displayName);
      } else {
        await this.auth.login(this.email, this.password);
      }
      await this.router.navigateByUrl(this.returnUrl());
    } catch (e) {
      this.error.set(describeError(e));
    } finally {
      this.busy.set(false);
    }
  }

  private returnUrl(): string {
    return this.route.snapshot.queryParamMap.get('returnUrl') || '/dashboard';
  }
}
