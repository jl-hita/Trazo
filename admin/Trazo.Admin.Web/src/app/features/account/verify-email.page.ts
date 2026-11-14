import { Component, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'trazo-verify-email-page',
  standalone: true,
  styles: [`
    :host { display:grid; place-items:center; min-height:100vh; padding:24px; background:#0d1119; color:#e8edf5; }
    section { display:grid; gap:14px; width:min(420px,100%); padding:30px; border:1px solid #293444; border-radius:12px; background:#131b27; }
    img { width:130px; } h1 { margin:0; font-size:22px; } p { margin:0; color:#aab6c7; line-height:1.55; }
    a { display:inline-block; padding:11px 16px; border:1px solid #6d9bd2; border-radius:7px; background:#3b6ea8; color:#fff; text-align:center; text-decoration:none; }
  `],
  template: `
    <section>
      <img src="logo-dark.svg" alt="TRAZO">
      <h1>{{ title() }}</h1>
      <p>{{ message() }}</p>
      @if (done()) { <a href="/login">Continue to sign in</a> }
    </section>
  `
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
