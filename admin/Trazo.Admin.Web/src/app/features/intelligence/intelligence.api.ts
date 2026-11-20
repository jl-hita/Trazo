import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { IntelligenceSummary } from './intelligence.models';

const API = '/api/v1/admin/intelligence';

@Injectable({ providedIn: 'root' })
export class IntelligenceApi {
  private readonly http = inject(HttpClient);

  summary(from: string, to: string): Promise<IntelligenceSummary> {
    return firstValueFrom(this.http.get<IntelligenceSummary>(`${API}/summary`, { params: this.params(from, to) }));
  }

  export(from: string, to: string): Promise<Blob> {
    return firstValueFrom(this.http.get(`${API}/export`, { params: this.params(from, to), responseType: 'blob' }));
  }

  private params(from: string, to: string): HttpParams {
    const start = new Date(`${from}T00:00:00.000Z`);
    const end = new Date(`${to}T00:00:00.000Z`);
    end.setUTCDate(end.getUTCDate() + 1);
    return new HttpParams().set('from', start.toISOString()).set('to', end.toISOString());
  }
}
