import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import type { CatalogReadiness, PlanPreviewRequest, PlanPreviewResponse } from '../../core/admin.models';

const API = '/api/v1/admin/planning';

@Injectable({ providedIn: 'root' })
export class PlanningApi {
  private readonly http = inject(ApiHttpClient);

  preview(request: PlanPreviewRequest): Promise<PlanPreviewResponse> {
    return this.http.post<PlanPreviewResponse>(`${API}/preview`, request);
  }

  catalogReadiness(): Promise<CatalogReadiness> {
    return this.http.get<CatalogReadiness>(`${API}/catalog-readiness`);
  }
}
