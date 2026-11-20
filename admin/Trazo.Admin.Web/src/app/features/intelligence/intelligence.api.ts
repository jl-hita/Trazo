import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';
import { IntelligenceSummary } from './intelligence.models';

const API = '/api/v1/admin/intelligence';

@Injectable({ providedIn: 'root' })
export class IntelligenceApi {
  private readonly http = inject(ApiHttpClient);

  summary(from: string, to: string): Promise<IntelligenceSummary> {
    return this.http.get<IntelligenceSummary>(`${API}/summary`, { params: this.params(from, to) });
  }

  export(from: string, to: string): Promise<Blob> {
    return this.http.get(`${API}/export`, { params: this.params(from, to), responseType: 'blob' });
  }

  private params(from: string, to: string): HttpParams {
    const start = new Date(`${from}T00:00:00.000Z`);
    const end = new Date(`${to}T00:00:00.000Z`);
    end.setUTCDate(end.getUTCDate() + 1);
    return new HttpParams().set('from', start.toISOString()).set('to', end.toISOString());
  }
}
