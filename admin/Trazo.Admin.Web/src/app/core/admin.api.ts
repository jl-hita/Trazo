import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface Page<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface AdminUserSummary {
  id: string; email: string; displayName: string; role: string;
  emailVerified: boolean; twoFactorEnabled: boolean; hasPassword: boolean; hasGoogle: boolean;
  disabled: boolean; lockedOut: boolean; createdAt: string; lastLoginAt: string | null;
}

export interface AdminUserSession { deviceName: string | null; createdAt: string; expiresAt: string; }

export interface AdminUserDetail extends AdminUserSummary {
  failedLoginAttempts: number; disabledAt: string | null; activeSessionCount: number; sessions: AdminUserSession[];
}

export interface UserFilters { q: string; role: string; status: string; sort: string; page: number; pageSize: number; }

export type UserAction = 'disable' | 'enable' | 'revoke-sessions' | 'unlock' | 'verify-email';

const API = '/api/v1/admin';

/** Calls to the superadmin-only management endpoints. */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  listUsers(filters: UserFilters): Promise<Page<AdminUserSummary>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.role) params = params.set('role', filters.role);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.sort) params = params.set('sort', filters.sort);
    return firstValueFrom(this.http.get<Page<AdminUserSummary>>(`${API}/users`, { params }));
  }

  getUser(id: string): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.get<AdminUserDetail>(`${API}/users/${id}`));
  }

  updateUser(id: string, changes: { displayName?: string; role?: string }): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.patch<AdminUserDetail>(`${API}/users/${id}`, changes));
  }

  userAction(id: string, action: UserAction): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.post<AdminUserDetail>(`${API}/users/${id}/${action}`, {}));
  }
}
