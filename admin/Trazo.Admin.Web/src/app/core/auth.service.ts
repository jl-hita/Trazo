import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface UserSummary { id: string; email: string; displayName: string; role: string; twoFactorEnabled: boolean; }
interface TokenResponse { accessToken: string; refreshToken: string; user: UserSummary; }
export type LoginResult = { status: 'ok' } | { status: 'two-factor'; challengeToken: string };

const KEY = 'trazo.admin.session';

/**
 * Admin session. Tokens live in sessionStorage (cleared when the tab closes). Only users with the
 * `superadmin` role may use the admin console.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private session: TokenResponse | null = this.load();
  readonly user = signal<UserSummary | null>(this.session?.user ?? null);

  get accessToken(): string | null { return this.session?.accessToken ?? null; }
  get isSuperadmin(): boolean { return this.user()?.role === 'superadmin'; }

  async login(email: string, password: string): Promise<LoginResult> {
    const r = await firstValueFrom(this.http.post<TokenResponse & { twoFactorRequired?: boolean; challengeToken?: string }>(
      '/api/v1/auth/login', { email, password, deviceName: 'Admin web' }));
    if (r.twoFactorRequired && r.challengeToken) return { status: 'two-factor', challengeToken: r.challengeToken };
    this.store(r);
    return { status: 'ok' };
  }

  async verifyTwoFactor(challengeToken: string, code: string): Promise<void> {
    this.store(await firstValueFrom(this.http.post<TokenResponse>(
      '/api/v1/auth/2fa/verify', { challengeToken, code, deviceName: 'Admin web' })));
  }

  async logout(): Promise<void> {
    const hadSession = this.session !== null;
    try {
      if (hadSession) await firstValueFrom(this.http.post('/api/v1/auth/logout', { refreshToken: this.session?.refreshToken }));
    } catch { /* token already invalid */ }
    this.clear();
  }

  clear(): void {
    this.session = null;
    this.user.set(null);
    try { sessionStorage.removeItem(KEY); } catch { /* storage unavailable */ }
  }

  private store(r: TokenResponse): void {
    this.session = r;
    this.user.set(r.user);
    try { sessionStorage.setItem(KEY, JSON.stringify(r)); } catch { /* storage unavailable */ }
  }

  private load(): TokenResponse | null {
    try { return JSON.parse(sessionStorage.getItem(KEY) ?? 'null'); } catch { return null; }
  }
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).accessToken;
  const authed = token && req.url.startsWith('/api/') && !req.url.startsWith('/api/v1/auth/login')
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
  return next(authed);
};
