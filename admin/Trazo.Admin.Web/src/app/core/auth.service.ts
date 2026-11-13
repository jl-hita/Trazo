import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, firstValueFrom, from, switchMap, throwError } from 'rxjs';

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
  get hasSession(): boolean { return this.session !== null; }
  private refreshing: Promise<string> | null = null;

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

  /** Exchanges the refresh token for a new pair. Concurrent callers share one request, because refresh tokens rotate. */
  refresh(): Promise<string> {
    const current = this.session;
    if (!current) return Promise.reject(new Error('No session.'));
    this.refreshing ??= firstValueFrom(this.http.post<TokenResponse>(
      '/api/v1/auth/refresh', { refreshToken: current.refreshToken, deviceName: 'Admin web' }))
      .then(r => { this.store(r); return r.accessToken; })
      .finally(() => { this.refreshing = null; });
    return this.refreshing;
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
  const auth = inject(AuthService);
  const router = inject(Router);
  const isApi = req.url.startsWith('/api/');
  const isAuthCall = ['/api/v1/auth/login', '/api/v1/auth/refresh', '/api/v1/auth/2fa/verify'].some(u => req.url.startsWith(u));
  const withToken = (r: HttpRequest<unknown>, token: string | null) =>
    token && isApi && !isAuthCall ? r.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : r;

  return next(withToken(req, auth.accessToken)).pipe(
    catchError((error: unknown) => {
      // Access tokens last 15 minutes: on a 401 renew the session once and retry the request.
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !isApi || isAuthCall || !auth.hasSession) {
        return throwError(() => error);
      }
      return from(auth.refresh().catch(() => null)).pipe(
        switchMap(token => {
          if (token) return next(withToken(req, token));
          auth.clear();
          void router.navigateByUrl('/login');
          return throwError(() => error);
        }));
    }));
};
