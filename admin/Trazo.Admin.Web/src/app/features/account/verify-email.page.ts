import { Component, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ACCOUNT_COPY } from './account.copy';

@Component({
  selector: 'trazo-verify-email-page',
  standalone: true,
  styleUrl: './verify-email.page.scss',
  templateUrl: './verify-email.page.html'
})
export class VerifyEmailPageComponent {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  protected readonly copy = ACCOUNT_COPY.verifyEmail;
  protected readonly title = signal(this.copy.verifyingTitle);
  protected readonly message = signal(this.copy.waitingMessage);
  protected readonly done = signal(false);

  constructor() {
    const token = this.route.snapshot.queryParamMap.get('token');
    if (!token) {
      this.title.set(this.copy.invalidTitle);
      this.message.set(this.copy.missingToken);
      return;
    }
    // The token stays in memory only: out of the address bar and the browser history.
    inject(Location).replaceState('/account/verify-email');
    void this.verify(token);
  }

  private async verify(token: string): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/email/verify', { token }));
      this.title.set(this.copy.verifiedTitle);
      this.message.set(this.copy.verifiedMessage);
      this.done.set(true);
    } catch {
      this.title.set(this.copy.expiredTitle);
      this.message.set(this.copy.expiredMessage);
    }
  }
}
