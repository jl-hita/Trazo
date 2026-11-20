import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { CatalogReadiness, PlanPreviewRequest, PlanPreviewResponse } from '../../core/admin.models';

const API = '/api/v1/admin/planning';

@Injectable({ providedIn: 'root' })
export class PlanningApi {
  private readonly http = inject(HttpClient);

  preview(request: PlanPreviewRequest): Promise<PlanPreviewResponse> {
    return firstValueFrom(this.http.post<PlanPreviewResponse>(`${API}/preview`, request));
  }

  catalogReadiness(): Promise<CatalogReadiness> {
    return firstValueFrom(this.http.get<CatalogReadiness>(`${API}/catalog-readiness`));
  }
}
