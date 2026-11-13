import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'trazo-login-page',
  standalone: true,
  imports: [FormsModule],
  styles: [`
    :host { display: grid; place-items: center; min-height: 100vh; padding: 20px; background: #0d1119; color: #e8edf5; }
    form { display: grid; gap: 14px; width: min(380px, 100%); padding: 28px; border: 1px solid #293444; border-radius: 10px; background: #131b27; }
    .logo { justify-self: start; height: 38px; width: auto; }
    h1 { margin: 0; font-size: 18px; font-weight: 500; color: #aab6c7; }
    label { display: grid; gap: 6px; color: #aab6c7; font-size: 12px; }
    input { padding: 10px 12px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; }
    button { padding: 10px 16px; border: 1px solid #6d9bd2; border-radius: 6px; background: #3b6ea8; color: #fff; cursor: pointer; }
    button:disabled { opacity: .5; }
    .err { margin: 0; color: #f0a3a3; font-size: 12px; }
  `],
  template: `
    <form (ngSubmit)="submit()">
      <img class="logo" src="logo-dark.svg" alt="TRAZO" height="38">
      <h1>Administration</h1>
      @if (challenge()) {
        <label>Authentication code <input name="code" inputmode="numeric" autocomplete="one-time-code" required [ngModel]="code()" (ngModelChange)="code.set($event)"></label>
      } @else {
        <label>Email <input type="email" name="email" autocomplete="username" required [ngModel]="email()" (ngModelChange)="email.set($event)"></label>
        <label>Password <input type="password" name="password" autocomplete="current-password" required [ngModel]="password()" (ngModelChange)="password.set($event)"></label>
      }
      @if (error()) { <p class="err" role="alert">{{ error() }}</p> }
      <button type="submit" [disabled]="busy()">Sign in</button>
    </form>
  `
})
export class LoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly code = signal('');
  protected readonly challenge = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const token = this.challenge();
      if (token) {
        await this.auth.verifyTwoFactor(token, this.code().trim());
      } else {
        const r = await this.auth.login(this.email().trim(), this.password());
        if (r.status === 'two-factor') { this.challenge.set(r.challengeToken); return; }
      }
      if (!this.auth.isSuperadmin) {
        this.auth.clear();
        this.error.set('This console is only available to superadmins.');
        return;
      }
      await this.router.navigateByUrl('/');
    } catch (e) {
      const status = (e as { status?: number }).status;
      this.error.set(status === 429 ? 'Too many attempts. Wait a minute.'
        : status === 409 ? 'Verify your email before signing in.' : 'Invalid credentials.');
    } finally {
      this.busy.set(false);
    }
  }
}
