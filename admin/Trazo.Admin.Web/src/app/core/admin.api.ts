import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface Page<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface AdminUserSummary {
  id: string; email: string; displayName: string; role: string;
  emailVerified: boolean; twoFactorEnabled: boolean; hasPassword: boolean; hasGoogle: boolean;
  disabled: boolean; lockedOut: boolean; createdAt: string; lastLoginAt: string | null;
}

export interface AdminUserSession { deviceName: string | null; createdAt: string; expiresAt: string; }

export interface AdminUserDetail extends AdminUserSummary {
  failedLoginAttempts: number; disabledAt: string | null; activeSessionCount: number; sessions: AdminUserSession[];
}

export interface UserFilters { q: string; role: string; status: string; sort: string; page: number; pageSize: number; }

export type UserAction = 'disable' | 'enable' | 'revoke-sessions' | 'unlock' | 'verify-email';

export interface AdminExerciseSummary {
  id: string; source: string; nameEs: string; nameEn: string; difficulty: string; equipment: string | null;
  movementPattern: string; minimumExperience: string; primaryMuscles: string[]; secondaryMuscles: string[];
  selectionPriority: number; isActive: boolean; locallyCurated: boolean; imageCount: number;
}

export interface AdminExerciseDetail {
  id: string; source: string; nameEs: string; nameEn: string; descriptionEs: string; instructionsEs: string[]; tipsEs: string[];
  forceType: string; mechanic: string; difficulty: string; equipment: string | null; bodyPart: string; met: number;
  isUnilateral: boolean; isBodyweight: boolean; movementPattern: string; primaryMuscles: string[]; secondaryMuscles: string[];
  minimumExperience: string; suitableGoals: string[]; requiredEquipment: string[]; selectionPriority: number;
  isActive: boolean; locallyCurated: boolean; importedFrom: string | null; imageSlots: number[]; createdAt: string; updatedAt: string;
}

export interface AdminExerciseInput {
  id?: string; nameEs: string; nameEn: string; descriptionEs: string; instructionsEs: string[]; tipsEs: string[];
  forceType: string; mechanic: string; difficulty: string; equipment: string; bodyPart: string; met: number;
  isUnilateral: boolean; isBodyweight: boolean; movementPattern: string; primaryMuscles: string[]; secondaryMuscles: string[];
  minimumExperience: string; suitableGoals: string[]; requiredEquipment: string[]; selectionPriority: number;
}

export interface AdminExerciseOptions {
  movementPatterns: string[]; difficulties: string[]; experiences: string[]; equipment: string[]; forceTypes: string[];
  mechanics: string[]; bodyParts: string[]; sources: string[]; muscles: string[]; goals: string[];
}

export interface ExerciseFilters {
  q: string; movementPattern: string; status: string; source: string; sort: string; page: number; pageSize: number;
}

export type ExerciseAction = 'activate' | 'deactivate' | 'release-curation';

const API = '/api/v1/admin';

/** Calls to the superadmin-only management endpoints. */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  listUsers(filters: UserFilters): Promise<Page<AdminUserSummary>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.role) params = params.set('role', filters.role);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.sort) params = params.set('sort', filters.sort);
    return firstValueFrom(this.http.get<Page<AdminUserSummary>>(`${API}/users`, { params }));
  }

  getUser(id: string): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.get<AdminUserDetail>(`${API}/users/${id}`));
  }

  updateUser(id: string, changes: { displayName?: string; role?: string }): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.patch<AdminUserDetail>(`${API}/users/${id}`, changes));
  }

  userAction(id: string, action: UserAction): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.post<AdminUserDetail>(`${API}/users/${id}/${action}`, {}));
  }

  listExercises(filters: ExerciseFilters): Promise<Page<AdminExerciseSummary>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.movementPattern) params = params.set('movementPattern', filters.movementPattern);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.source) params = params.set('source', filters.source);
    if (filters.sort) params = params.set('sort', filters.sort);
    return firstValueFrom(this.http.get<Page<AdminExerciseSummary>>(`${API}/exercises`, { params }));
  }

  exerciseOptions(): Promise<AdminExerciseOptions> {
    return firstValueFrom(this.http.get<AdminExerciseOptions>(`${API}/exercises/options`));
  }

  getExercise(id: string): Promise<AdminExerciseDetail> {
    return firstValueFrom(this.http.get<AdminExerciseDetail>(`${API}/exercises/${encodeURIComponent(id)}`));
  }

  createExercise(input: AdminExerciseInput): Promise<AdminExerciseDetail> {
    return firstValueFrom(this.http.post<AdminExerciseDetail>(`${API}/exercises`, input));
  }

  updateExercise(id: string, input: AdminExerciseInput): Promise<AdminExerciseDetail> {
    return firstValueFrom(this.http.put<AdminExerciseDetail>(`${API}/exercises/${encodeURIComponent(id)}`, input));
  }

  exerciseAction(id: string, action: ExerciseAction): Promise<AdminExerciseDetail> {
    return firstValueFrom(this.http.post<AdminExerciseDetail>(`${API}/exercises/${encodeURIComponent(id)}/${action}`, {}));
  }
}
