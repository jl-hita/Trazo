import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiCardComponent } from '../../shared/ui/ui-card.component';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import { UiStateComponent } from '../../shared/ui/ui-state.component';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { UI_COPY } from '../../core/ui-copy';

@Component({
  selector: 'trazo-login-page',
  standalone: true,
  imports: [FormsModule, UiButtonComponent, UiCardComponent, UiFormFieldComponent, UiStateComponent],
  styleUrl: './login.page.scss',
  templateUrl: './login.page.html'
})
export class LoginPageComponent {
  protected readonly copy = UI_COPY.login;
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
      if (!this.auth.canUseConsole) {
        this.auth.clear();
        this.error.set('This console is only available to administrators.');
        return;
      }
      await this.router.navigateByUrl(this.auth.home);
    } catch (e) {
      const status = (e as { status?: number }).status;
      this.error.set(status === 429 ? 'Too many attempts. Wait a minute.'
        : status === 409 ? 'Verify your email before signing in.' : 'Invalid credentials.');
    } finally {
      this.busy.set(false);
    }
  }
}
