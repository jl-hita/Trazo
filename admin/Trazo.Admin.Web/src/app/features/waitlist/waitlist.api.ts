import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';
import type { WaitlistInviteResult, WaitlistPage, WaitlistStatus } from '../../core/admin.models';

const API = '/api/v1/admin/waitlist';

@Injectable({ providedIn: 'root' })
export class WaitlistApi {
  private readonly http = inject(ApiHttpClient);

  list(q: string, page: number, pageSize: number, status: WaitlistStatus = ''): Promise<WaitlistPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (q.trim()) params = params.set('q', q.trim());
    if (status) params = params.set('status', status);
    return this.http.get<WaitlistPage>(API, { params });
  }

  invite(ids: string[]): Promise<WaitlistInviteResult> {
    return this.http.post<WaitlistInviteResult>(`${API}/invite`, { ids });
  }

  delete(id: string): Promise<void> {
    return this.http.delete<void>(`${API}/${id}`);
  }

  /** Use HttpClient so the bearer token is sent with the CSV request. */
  export(): Promise<Blob> {
    return this.http.get(`${API}/export.csv`, { responseType: 'blob' });
  }
}
