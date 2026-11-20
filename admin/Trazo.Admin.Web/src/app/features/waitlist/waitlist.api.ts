import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { WaitlistInviteResult, WaitlistPage, WaitlistStatus } from '../../core/admin.models';

const API = '/api/v1/admin/waitlist';

@Injectable({ providedIn: 'root' })
export class WaitlistApi {
  private readonly http = inject(HttpClient);

  list(q: string, page: number, pageSize: number, status: WaitlistStatus = ''): Promise<WaitlistPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (q.trim()) params = params.set('q', q.trim());
    if (status) params = params.set('status', status);
    return firstValueFrom(this.http.get<WaitlistPage>(API, { params }));
  }

  invite(ids: string[]): Promise<WaitlistInviteResult> {
    return firstValueFrom(this.http.post<WaitlistInviteResult>(`${API}/invite`, { ids }));
  }

  delete(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${API}/${id}`));
  }

  /** Use HttpClient so the bearer token is sent with the CSV request. */
  export(): Promise<Blob> {
    return firstValueFrom(this.http.get(`${API}/export.csv`, { responseType: 'blob' }));
  }
}
