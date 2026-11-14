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

export interface TransferResult {
  sent: boolean; exercises: number; images: number; skipped: { exerciseId: string; reason: string }[];
  remote: { exercisesInserted?: number; exercisesUpdated?: number; exercisesSkipped?: number; imagesStored?: number } | null;
}

export type ExerciseAction = 'activate' | 'deactivate' | 'release-curation';

export interface PlanPreviewRequest {
  goal: string; experience: string; startDate: string; durationWeeks: number; trainingDays: string[];
  maxSessionMinutes: number; availableEquipment: string[]; excludedExerciseIds: string[]; split: string;
  macrocycleWeeks?: number; includeDeloadPhases?: boolean; deloadEveryTrainingWeeks?: number;
}

export interface PlanExercise {
  exerciseId: string; exerciseNameSnapshot: string; order: number; sets: number; minRepetitions: number;
  maxRepetitions: number; targetRepetitionsInReserve: number | null; restSeconds: number; progressionRule: string;
  selectionRationale: string; primaryMuscleGroups: string[]; secondaryMuscleGroups: string[];
  warmupSets: number | null; warmupNote: string | null;
}

export interface PlanSession { weekNumber: number; day: string; title: string; estimatedDurationMinutes: number; exercises: PlanExercise[]; }

export interface MuscleVolume {
  muscleGroup: string; primarySets: number; secondarySets: number; primaryFrequencySessions: number;
  secondaryFrequencySessions: number; estimatedEffectiveSets: number;
}

export interface PlanProgram {
  engineVersion: string; goal: string; startDate: string; durationWeeks: number; sessions: PlanSession[];
  qualityReport: { methodology: string; warnings: string[]; weeklyMuscleVolumes: { weekNumber: number; muscles: MuscleVolume[] }[] } | null;
}

export interface MacrocycleDraft {
  name: string; periodizationModel: string; startDate: string; endDate: string; notes: string;
  phases: { name: string; sequenceNumber: number; startDate: string; endDate: string; objective: string; phaseType: string }[];
}

export interface SkippedExercise { exerciseId: string; reason: string; }

export interface PlanPreviewResponse { program: PlanProgram; macrocycle: MacrocycleDraft | null; usableExercises: number; skipped: SkippedExercise[]; }

export interface ReadinessGroup { name: string; count: number; }

export interface CatalogReadiness {
  usableExercises: number; skipped: SkippedExercise[]; byPattern: ReadinessGroup[]; byExperience: ReadinessGroup[]; byGoal: ReadinessGroup[];
}

export type LogLevelGroup = 'info' | 'warning' | 'error';

export interface LogEntry {
  id: number; timestamp: string; level: LogLevelGroup; category: string; message: string; exception: string | null; traceId: string | null;
}

export interface LogPage { items: LogEntry[]; nextCursor: string | null; counts: { info: number; warning: number; error: number }; }

export interface LogFilters {
  levels: LogLevelGroup[]; from: string | null; to: string | null; q: string; cursor: string | null; pageSize: number;
}

export type SubscriptionStatusName =
  'pending' | 'trial' | 'active' | 'grace_period' | 'on_hold' | 'canceled' | 'expired' | 'revoked';

export interface AdminSubscriptionSummary {
  id: string; userId: string; email: string; displayName: string; productId: string; status: SubscriptionStatusName;
  hasAccess: boolean; startedAt: string; expiresAt: string; autoRenewing: boolean; source: string; updatedAt: string;
}

export interface AdminSubscriptionDetail {
  subscription: AdminSubscriptionSummary; storeState: string | null; latestOrderId: string | null;
  acknowledgedAt: string | null; isTestPurchase: boolean; createdAt: string; purchaseTokenFingerprint: string;
}

export interface SubscriptionFilters {
  q: string; status: string; source: string; expiresFrom: string; expiresTo: string; sort: string; page: number; pageSize: number;
}

export interface PlayBillingView {
  packageName: string; serviceAccountSet: boolean; serviceAccountEmail: string | null; rtdnSecretSet: boolean;
  rtdnAudience: string | null; rtdnServiceAccountEmail: string | null; configured: boolean; rtdnConfigured: boolean;
}

/** Null secrets keep the stored value; an empty string clears it. */
export interface PlayBillingInput {
  packageName: string; serviceAccountJson: string | null; rtdnSecret: string | null;
  rtdnAudience: string; rtdnServiceAccountEmail: string;
}

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

  listLogs(filters: LogFilters): Promise<LogPage> {
    let params = new HttpParams().set('pageSize', filters.pageSize).set('levels', filters.levels.join(','));
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.cursor) params = params.set('cursor', filters.cursor);
    return firstValueFrom(this.http.get<LogPage>(`${API}/logs`, { params }));
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

  previewPlan(request: PlanPreviewRequest): Promise<PlanPreviewResponse> {
    return firstValueFrom(this.http.post<PlanPreviewResponse>(`${API}/planning/preview`, request));
  }

  catalogReadiness(): Promise<CatalogReadiness> {
    return firstValueFrom(this.http.get<CatalogReadiness>(`${API}/planning/catalog-readiness`));
  }

  uploadExerciseImage(id: string, slot: number, file: File): Promise<AdminExerciseDetail> {
    return firstValueFrom(this.http.put<AdminExerciseDetail>(
      `${API}/exercises/${encodeURIComponent(id)}/images/${slot}`, file, { headers: { 'Content-Type': file.type } }));
  }

  deleteExerciseImage(id: string, slot: number): Promise<AdminExerciseDetail> {
    return firstValueFrom(this.http.delete<AdminExerciseDetail>(`${API}/exercises/${encodeURIComponent(id)}/images/${slot}`));
  }

  listSubscriptions(filters: SubscriptionFilters): Promise<Page<AdminSubscriptionSummary>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.status) params = params.set('status', filters.status);
    if (filters.source) params = params.set('source', filters.source);
    if (filters.expiresFrom) params = params.set('expiresFrom', new Date(filters.expiresFrom + 'T00:00:00').toISOString());
    if (filters.expiresTo) params = params.set('expiresTo', new Date(filters.expiresTo + 'T23:59:59').toISOString());
    if (filters.sort) params = params.set('sort', filters.sort);
    return firstValueFrom(this.http.get<Page<AdminSubscriptionSummary>>(`${API}/subscriptions`, { params }));
  }

  getSubscription(id: string): Promise<AdminSubscriptionDetail> {
    return firstValueFrom(this.http.get<AdminSubscriptionDetail>(`${API}/subscriptions/${id}`));
  }

  refreshSubscription(id: string): Promise<AdminSubscriptionDetail> {
    return firstValueFrom(this.http.post<AdminSubscriptionDetail>(`${API}/subscriptions/${id}/refresh`, {}));
  }

  getPlayBilling(): Promise<PlayBillingView> {
    return firstValueFrom(this.http.get<PlayBillingView>(`${API}/settings/play-billing`));
  }

  savePlayBilling(input: PlayBillingInput): Promise<{ saved: boolean }> {
    return firstValueFrom(this.http.put<{ saved: boolean }>(`${API}/settings/play-billing`, input));
  }

  testPlayBilling(): Promise<{ ok: boolean; message: string }> {
    return firstValueFrom(this.http.post<{ ok: boolean; message: string }>(`${API}/settings/play-billing/test`, {}));
  }

  sendLibraryToDietoExpress(): Promise<TransferResult> {
    return firstValueFrom(this.http.post<TransferResult>(`${API}/exercise-library-transfer/dietoexpress`, {}));
  }
}
