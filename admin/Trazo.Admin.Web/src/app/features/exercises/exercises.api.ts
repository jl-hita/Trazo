import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';
import type {
  AdminExerciseDetail, AdminExerciseInput, AdminExerciseOptions, AdminExerciseSummary, AdminSeedReport,
  ExerciseAction, ExerciseFilters, Page, TransferResult
} from '../../core/admin.models';

const API = '/api/v1/admin';

@Injectable({ providedIn: 'root' })
export class ExercisesApi {
  private readonly http = inject(ApiHttpClient);

  list(filters: ExerciseFilters): Promise<Page<AdminExerciseSummary>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.movementPattern) params = params.set('movementPattern', filters.movementPattern);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.source) params = params.set('source', filters.source);
    if (filters.sort) params = params.set('sort', filters.sort);
    return this.http.get<Page<AdminExerciseSummary>>(`${API}/exercises`, { params });
  }

  seedReport(): Promise<AdminSeedReport> {
    return this.http.get<AdminSeedReport>(`${API}/exercises/seed-report`);
  }

  options(): Promise<AdminExerciseOptions> {
    return this.http.get<AdminExerciseOptions>(`${API}/exercises/options`);
  }

  get(id: string): Promise<AdminExerciseDetail> {
    return this.http.get<AdminExerciseDetail>(`${API}/exercises/${encodeURIComponent(id)}`);
  }

  create(input: AdminExerciseInput): Promise<AdminExerciseDetail> {
    return this.http.post<AdminExerciseDetail>(`${API}/exercises`, input);
  }

  update(id: string, input: AdminExerciseInput): Promise<AdminExerciseDetail> {
    return this.http.put<AdminExerciseDetail>(`${API}/exercises/${encodeURIComponent(id)}`, input);
  }

  action(id: string, action: ExerciseAction): Promise<AdminExerciseDetail> {
    return this.http.post<AdminExerciseDetail>(`${API}/exercises/${encodeURIComponent(id)}/${action}`, {});
  }

  uploadImage(id: string, slot: number, file: File): Promise<AdminExerciseDetail> {
    return this.http.put<AdminExerciseDetail>(
      `${API}/exercises/${encodeURIComponent(id)}/images/${slot}`, file, { headers: { 'Content-Type': file.type } });
  }

  deleteImage(id: string, slot: number): Promise<AdminExerciseDetail> {
    return this.http.delete<AdminExerciseDetail>(`${API}/exercises/${encodeURIComponent(id)}/images/${slot}`);
  }

  transferToDietoExpress(): Promise<TransferResult> {
    return this.http.post<TransferResult>(`${API}/exercise-library-transfer/dietoexpress`, {});
  }
}
