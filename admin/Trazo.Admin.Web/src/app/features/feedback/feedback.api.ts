import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { ApiHttpClient } from '../../core/api-http.client';
import type { FeedbackPage, FeedbackStatus } from '../../core/admin.models';

const API = '/api/v1/admin/feedback';

@Injectable({ providedIn: 'root' })
export class FeedbackApi {
  private readonly http = inject(ApiHttpClient);

  list(status: string, category: string, page: number, pageSize: number): Promise<FeedbackPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status) params = params.set('status', status);
    if (category) params = params.set('category', category);
    return this.http.get<FeedbackPage>(API, { params });
  }

  setStatus(id: string, status: FeedbackStatus): Promise<void> {
    return this.http.put<void>(`${API}/${id}/status`, { status });
  }

  delete(id: string): Promise<void> {
    return this.http.delete<void>(`${API}/${id}`);
  }
}
