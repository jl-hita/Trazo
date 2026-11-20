import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';
import type { LogFilters, LogPage } from '../../core/admin.models';

const API = '/api/v1/admin/logs';

@Injectable({ providedIn: 'root' })
export class LogsApi {
  private readonly http = inject(ApiHttpClient);

  list(filters: LogFilters): Promise<LogPage> {
    let params = new HttpParams().set('pageSize', filters.pageSize).set('levels', filters.levels.join(','));
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.cursor) params = params.set('cursor', filters.cursor);
    return this.http.get<LogPage>(API, { params });
  }
}
