import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'trazo-reset-password-page',
  standalone: true,
  imports: [FormsModule],
  styles: [`
    :host { display:grid; place-items:center; min-height:100vh; padding:24px; background:#0d1119; color:#e8edf5; }
    form,.panel { display:grid; gap:16px; width:min(420px,100%); padding:30px; border:1px solid #293444; border-radius:12px; background:#131b27; }
    .logo { width:130px; height:auto; margin-bottom:4px; }
    h1 { margin:0; font-size:22px; }
    p { margin:0; color:#aab6c7; line-height:1.55; }
    label { display:grid; gap:7px; color:#c5cfdd; font-size:13px; }
    input { min-width:0; padding:11px 12px; border:1px solid #34465b; border-radius:7px; background:#0d1119; color:#e8edf5; }
    button,a { display:inline-block; padding:11px 16px; border:1px solid #6d9bd2; border-radius:7px; background:#3b6ea8; color:#fff; text-align:center; text-decoration:none; cursor:pointer; }
    button:disabled { opacity:.55; cursor:wait; }
    .err { color:#f0a3a3; font-size:13px; }
    .success { color:#a8dfb5; }
  `],
  template: `
    @if (complete()) {
      <section class="panel">
        <img class="logo" src="logo-dark.svg" alt="TRAZO">
        <h1>Password saved</h1>
        <p class="success">Your account is now activated. You can sign in with your email and new password.</p>
        <a href="/login">Go to sign in</a>
      </section>
    } @else if (!token) {
      <section class="panel">
        <h1>Invalid invitation link</h1>
        <p>This link is missing its security token. Ask the administrator to send a new invitation.</p>
        <a href="/">Back to TRAZO</a>
      </section>
    } @else {
      <form (ngSubmit)="submit()">
        <img class="logo" src="logo-dark.svg" alt="TRAZO">
        <h1>Set your password</h1>
        <p>Choose a secure password to activate your TRAZO account.</p>
        <label>New password
          <input type="password" name="password" autocomplete="new-password" required minlength="12" maxlength="128"
            [ngModel]="password()" (ngModelChange)="password.set($event)">
        </label>
        <label>Confirm password
          <input type="password" name="confirmPassword" autocomplete="new-password" required
            [ngModel]="confirmation()" (ngModelChange)="confirmation.set($event)">
        </label>
        <p>At least 12 characters, including uppercase, lowercase and a number.</p>
        @if (error()) { <p class="err" role="alert">{{ error() }}</p> }
        <button type="submit" [disabled]="busy() || !valid">Activate account</button>
      </form>
    }
  `
})
export class ResetPasswordPageComponent {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  protected readonly token = this.route.snapshot.queryParamMap.get('token') ?? '';

  constructor() {
    // The token stays in memory only: out of the address bar, the browser history and anything that reads the URL.
    if (this.token) inject(Location).replaceState('/account/reset-password');
  }
  protected readonly password = signal('');
  protected readonly confirmation = signal('');
  protected readonly busy = signal(false);
  protected readonly complete = signal(false);
  protected readonly error = signal('');

  protected get valid(): boolean {
    const p = this.password();
    return p.length >= 12 && p.length <= 128 && /[A-Z]/.test(p) && /[a-z]/.test(p) && /[0-9]/.test(p)
      && p === this.confirmation();
  }

  protected async submit(): Promise<void> {
    if (!this.token || !this.valid) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/password/reset', {
        token: this.token, newPassword: this.password()
      }));
      this.complete.set(true);
    } catch (e) {
      const status = (e as { status?: number }).status;
      this.error.set(status === 400
        ? 'This link is invalid, expired or has already been used. Ask the administrator for a new invitation.'
        : 'We could not save your password. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
