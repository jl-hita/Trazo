import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';
import type { AdminPlanReview, AdminPlanReviewDetail, Page } from '../../core/admin.models';

const API = '/api/v1/admin/plan-reviews';

@Injectable({ providedIn: 'root' })
export class PlanReviewsApi {
  private readonly http = inject(ApiHttpClient);

  list(status: string, page: number): Promise<Page<AdminPlanReview>> {
    let params = new HttpParams().set('page', page).set('pageSize', 25);
    if (status) params = params.set('status', status);
    return this.http.get<Page<AdminPlanReview>>(API, { params });
  }

  counts(): Promise<{ pending: number; mine: number; mineAwaitingReply: number }> {
    return this.http.get<{ pending: number; mine: number; mineAwaitingReply: number }>(`${API}/pending-count`);
  }

  get(id: string): Promise<AdminPlanReviewDetail> {
    return this.http.get<AdminPlanReviewDetail>(`${API}/${id}`);
  }

  claim(id: string): Promise<void> {
    return this.http.post<void>(`${API}/${id}/claim`, {});
  }

  release(id: string, reason: string): Promise<void> {
    return this.http.post<void>(`${API}/${id}/release`, { reason: reason.trim() || null });
  }

  message(id: string, body: string): Promise<void> {
    return this.http.post<void>(`${API}/${id}/messages`, { body });
  }

  close(id: string, message: string): Promise<void> {
    return this.http.post<void>(`${API}/${id}/review`, { message: message.trim() || null });
  }
}
