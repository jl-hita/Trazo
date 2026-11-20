import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'trazo-reset-password-page',
  standalone: true,
  imports: [FormsModule, UiButtonComponent, UiFormFieldComponent],
  styleUrl: './reset-password.page.scss',
  templateUrl: './reset-password.page.html'
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
