import { Component, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'trazo-verify-email-page',
  standalone: true,
  styleUrl: './verify-email.page.scss',
  templateUrl: './verify-email.page.html'
})
export class VerifyEmailPageComponent {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  protected readonly title = signal('Verifying your email');
  protected readonly message = signal('Please wait while we verify your email address.');
  protected readonly done = signal(false);

  constructor() {
    const token = this.route.snapshot.queryParamMap.get('token');
    if (!token) {
      this.title.set('Invalid verification link');
      this.message.set('The link is missing its security token. Request a new verification email.');
      return;
    }
    // The token stays in memory only: out of the address bar and the browser history.
    inject(Location).replaceState('/account/verify-email');
    void this.verify(token);
  }

  private async verify(token: string): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/email/verify', { token }));
      this.title.set('Email verified');
      this.message.set('Your email address has been verified. You can now sign in.');
      this.done.set(true);
    } catch {
      this.title.set('Link invalid or expired');
      this.message.set('This verification link is invalid, expired or has already been used. Request a new one.');
    }
  }
}
