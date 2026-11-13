import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface SetupStatus {
  setupRequired: boolean;
  needsSuperadmin: boolean;
  googleConfigured: boolean;
  emailConfigured: boolean;
  configurationComplete: boolean;
}

export interface GoogleSettings { clientId: string; }

export interface EmailSettings {
  host: string;
  port: number;
  enableSsl: boolean;
  username: string;
  /** null keeps the stored password (settings edit); '' clears it. */
  password: string | null;
  from: string;
  actionBaseUrl: string;
}

export interface AdminAccount { email: string; displayName: string; password: string; }

export interface TestResult { ok: boolean; message: string; }

export interface SettingsView {
  googleClientId: string;
  smtpHost: string;
  smtpPort: number;
  smtpEnableSsl: boolean;
  smtpUsername: string | null;
  smtpPasswordSet: boolean;
  emailFrom: string;
  emailActionBaseUrl: string;
}

const API = '/api/v1';

@Injectable({ providedIn: 'root' })
export class SetupApi {
  private readonly http = inject(HttpClient);

  status(): Promise<SetupStatus> {
    return firstValueFrom(this.http.get<SetupStatus>(`${API}/setup/status`));
  }

  testGoogle(clientId: string, admin = false): Promise<TestResult> {
    return firstValueFrom(this.http.post<TestResult>(this.base(admin) + '/test/google', { clientId }));
  }

  testSmtp(email: EmailSettings, recipient: string, admin = false): Promise<TestResult> {
    return firstValueFrom(this.http.post<TestResult>(this.base(admin) + '/test/smtp', { email, recipient }));
  }

  complete(google: GoogleSettings, email: EmailSettings, admin: AdminAccount): Promise<{ completed: boolean }> {
    return firstValueFrom(this.http.post<{ completed: boolean }>(`${API}/setup/complete`, { google, email, admin }));
  }

  getSettings(): Promise<SettingsView> {
    return firstValueFrom(this.http.get<SettingsView>(`${API}/admin/settings`));
  }

  saveSettings(google: GoogleSettings, email: EmailSettings): Promise<{ saved: boolean }> {
    return firstValueFrom(this.http.put<{ saved: boolean }>(`${API}/admin/settings`, { google, email }));
  }

  private base(admin: boolean): string {
    return admin ? `${API}/admin/settings` : `${API}/setup`;
  }
}

/** Extracts a readable message from an ASP.NET ValidationProblem / error response. */
export function errorMessage(error: unknown): string {
  const e = error as { status?: number; error?: { errors?: Record<string, string[]>; error?: string } };
  if (e?.status === 429) return 'Too many requests. Wait a minute and try again.';
  if (e?.status === 404) return 'The setup has already been completed.';
  if (e?.status === 401) return 'Your session has expired. Sign in again.';
  if (e?.status === 403) return 'Your account is not allowed to do this.';
  const errors = e?.error?.errors;
  if (errors) return Object.values(errors).flat().join(' ');
  return e?.error?.error ?? 'Unexpected error. Check that the API is reachable.';
}
