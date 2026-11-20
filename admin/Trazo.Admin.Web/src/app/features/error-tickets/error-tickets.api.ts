import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { ErrorTicketDetail, ErrorTicketPage, ErrorTicketStatus } from '../../core/admin.models';

const API = '/api/v1/admin/error-tickets';

@Injectable({ providedIn: 'root' })
export class ErrorTicketsApi {
  private readonly http = inject(HttpClient);

  list(q: string, status: string, source: string, page: number, pageSize: number): Promise<ErrorTicketPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (q.trim()) params = params.set('q', q.trim());
    if (status) params = params.set('status', status);
    if (source) params = params.set('source', source);
    return firstValueFrom(this.http.get<ErrorTicketPage>(API, { params }));
  }

  get(id: string): Promise<ErrorTicketDetail> {
    return firstValueFrom(this.http.get<ErrorTicketDetail>(`${API}/${id}`));
  }

  setStatus(id: string, status: ErrorTicketStatus): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${API}/${id}/status`, { status }));
  }

  /** Makes the API throw on purpose; resolves with the HTTP status it answered (500 when the chain works). */
  async triggerTestException(): Promise<number> {
    try {
      await firstValueFrom(this.http.post<void>(`${API}/test-exception`, {}));
      return 200;
    } catch (e) {
      return (e as { status?: number }).status ?? 0;
    }
  }

  delete(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${API}/${id}`));
  }
}
