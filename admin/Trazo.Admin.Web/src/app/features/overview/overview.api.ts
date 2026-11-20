import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { Overview, SystemInfo } from '../../core/admin.models';

const API = '/api/v1/admin';

@Injectable({ providedIn: 'root' })
export class OverviewApi {
  private readonly http = inject(HttpClient);

  overview(refresh = false): Promise<Overview> {
    const params = refresh ? new HttpParams().set('refresh', true) : new HttpParams();
    return firstValueFrom(this.http.get<Overview>(`${API}/overview`, { params }));
  }

  systemInfo(): Promise<SystemInfo> {
    return firstValueFrom(this.http.get<SystemInfo>(`${API}/overview/system`));
  }
}
