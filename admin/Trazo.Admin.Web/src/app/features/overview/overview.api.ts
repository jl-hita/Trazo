import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';
import type { Overview, SystemInfo } from '../../core/admin.models';

const API = '/api/v1/admin';

@Injectable({ providedIn: 'root' })
export class OverviewApi {
  private readonly http = inject(ApiHttpClient);

  overview(refresh = false): Promise<Overview> {
    const params = refresh ? new HttpParams().set('refresh', true) : new HttpParams();
    return this.http.get<Overview>(`${API}/overview`, { params });
  }

  systemInfo(): Promise<SystemInfo> {
    return this.http.get<SystemInfo>(`${API}/overview/system`);
  }
}
