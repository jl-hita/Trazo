import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';
import type {
  AdminPremiumGrant, AdminUserDetail, AdminUserSummary, Page, UserAction, UserFilters
} from '../../core/admin.models';

const API = '/api/v1/admin';

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(ApiHttpClient);

  listUsers(filters: UserFilters): Promise<Page<AdminUserSummary>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.role) params = params.set('role', filters.role);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.sort) params = params.set('sort', filters.sort);
    return this.http.get<Page<AdminUserSummary>>(`${API}/users`, { params });
  }

  createUser(input: { displayName: string; email: string; role: string }): Promise<AdminUserDetail> {
    return this.http.post<AdminUserDetail>(`${API}/users`, input);
  }

  getUser(id: string): Promise<AdminUserDetail> {
    return this.http.get<AdminUserDetail>(`${API}/users/${id}`);
  }

  listPremiumGrants(userId: string): Promise<AdminPremiumGrant[]> {
    return this.http.get<AdminPremiumGrant[]>(`${API}/users/${userId}/premium-grants`);
  }

  grantPremium(userId: string, input: { days?: number; permanent: boolean; reason: string }): Promise<AdminPremiumGrant> {
    return this.http.post<AdminPremiumGrant>(`${API}/users/${userId}/premium-grants`, input);
  }

  revokePremium(userId: string, grantId: string, reason: string): Promise<{ revoked: boolean; grantId: string }> {
    return this.http.post<{ revoked: boolean; grantId: string }>(
      `${API}/users/${userId}/premium-grants/${grantId}/revoke`, { reason });
  }

  updateUser(id: string, changes: { displayName?: string; role?: string }): Promise<AdminUserDetail> {
    return this.http.patch<AdminUserDetail>(`${API}/users/${id}`, changes);
  }

  deleteUser(id: string, confirmEmail: string): Promise<{ deleted: boolean; playSubscriptionNotCancelled: boolean }> {
    return this.http.delete<{ deleted: boolean; playSubscriptionNotCancelled: boolean }>(
      `${API}/users/${id}`, { body: { confirmEmail } });
  }

  sendPasswordInvitation(id: string): Promise<void> {
    return this.http.post<void>(`${API}/users/${id}/password-invitation`, {});
  }

  userAction(id: string, action: UserAction): Promise<AdminUserDetail> {
    return this.http.post<AdminUserDetail>(`${API}/users/${id}/${action}`, {});
  }
}
