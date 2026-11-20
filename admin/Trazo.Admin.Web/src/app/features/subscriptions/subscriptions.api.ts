import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';
import type { AdminSubscriptionDetail, AdminSubscriptionSummary, Page, SubscriptionFilters } from '../../core/admin.models';

const API = '/api/v1/admin/subscriptions';

@Injectable({ providedIn: 'root' })
export class SubscriptionsApi {
  private readonly http = inject(ApiHttpClient);

  list(filters: SubscriptionFilters): Promise<Page<AdminSubscriptionSummary>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.status) params = params.set('status', filters.status);
    if (filters.source) params = params.set('source', filters.source);
    if (filters.expiresFrom) params = params.set('expiresFrom', new Date(filters.expiresFrom + 'T00:00:00').toISOString());
    if (filters.expiresTo) params = params.set('expiresTo', new Date(filters.expiresTo + 'T23:59:59').toISOString());
    if (filters.sort) params = params.set('sort', filters.sort);
    return this.http.get<Page<AdminSubscriptionSummary>>(API, { params });
  }

  get(id: string): Promise<AdminSubscriptionDetail> {
    return this.http.get<AdminSubscriptionDetail>(`${API}/${id}`);
  }

  refresh(id: string): Promise<AdminSubscriptionDetail> {
    return this.http.post<AdminSubscriptionDetail>(`${API}/${id}/refresh`, {});
  }
}
