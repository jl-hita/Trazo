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

export type AlertSeverity = 'critical' | 'warning' | 'info';

export interface OverviewAlert { id: string; severity: AlertSeverity; title: string; detail: string; link: string; }

export interface Overview {
  generatedAt: string; cacheSeconds: number;
  health: {
    databaseOk: boolean; databaseLatencyMs: number; version: string; commit: string | null; environment: string;
    migrationsApplied: number; migrationsTotal: number; pendingMigrations: string[];
  };
  logs: {
    errors24h: number; warnings24h: number; errorsPrevious24h: number; errorsLink: string; warningsLink: string;
    topErrorSources: { category: string; count: number; lastAt: string; link: string }[];
  };
  configuration: {
    smtpConfigured: boolean; lastSmtpSend: { succeeded: boolean; at: string; error: string | null } | null;
    googleConfigured: boolean; playBillingConfigured: boolean; playRtdnConfigured: boolean; subscriptionsEnforced: boolean;
    missing: { id: string; label: string; link: string }[];
  };
  summary: {
    usersTotal: number; usersNew7d: number; plansCreated7d: number; workoutsLogged7d: number; subscriptionsTotal: number;
    subscriptionsByStatus: { name: string; count: number }[]; subscriptionsEnforced: boolean;
    exercisesActive: number; exercisesWithoutImages: number; exercisesUnusable: number; exercisesUsable: number;
  };
  alerts: OverviewAlert[];
  insights: OverviewInsights;
}

export interface OverviewInsights {
  activity: {
    days: { date: string; signups: number; plans: number; workouts: number }[];
    activeUsers7d: number; activeUsers30d: number; unverifiedOver3Days: number;
  };
  funnel: { registered: number; emailVerified: number; withPlan: number; withWorkout: number; recurring: number };
  subscriptions: { trialsEndingSoon: number; cancelingSoon: number; paymentIssues: number; testPurchases: number };
  engine: {
    workouts30d: number; completed30d: number; partial30d: number; notCompleted30d: number; painReportedPercent: number | null;
    adaptationsTotal30d: number; adaptationsByKind: { name: string; count: number }[];
    notCompletedReasons: { name: string; count: number }[];
  };
  conversion: {
    cohorts: { weekStart: string; signups: number; subscribed: number; paid: number }[];
    paywall: { days: number; views: number; bySource: { name: string; count: number }[] };
  };
}

export interface SystemInfo { version: string; commit: string | null; environment: string; }

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

export interface PushView {
  projectId: string; applicationId: string; apiKey: string; senderId: string | null;
  serviceAccountSet: boolean; serviceAccountEmail: string | null; configured: boolean; deviceCount: number;
}

/** A null service-account key keeps the stored one; an empty string clears it. */
export interface PushInput {
  projectId: string; applicationId: string; apiKey: string; senderId: string; serviceAccountJson: string | null;
}

export interface PushSendResult { ok: boolean; message: string; }

export interface PromotionsSettings {
  signupTrialEnabled: boolean; signupTrialDays: number; referralsEnabled: boolean; referralRewardDays: number;
}
export interface PromotionsView extends PromotionsSettings { maxDays: number; }

export interface PlayBillingView {
  packageName: string; serviceAccountSet: boolean; serviceAccountEmail: string | null; rtdnSecretSet: boolean;
  rtdnAudience: string | null; rtdnServiceAccountEmail: string | null; configured: boolean; rtdnConfigured: boolean;
}

/** Null secrets keep the stored value; an empty string clears it. */
export interface PlayBillingInput {
  packageName: string; serviceAccountJson: string | null; rtdnSecret: string | null;
  rtdnAudience: string; rtdnServiceAccountEmail: string;
}

export interface OperationsView {
  environmentCorsOrigins: string[]; additionalCorsOrigins: string[];
  setupPerMinute: number; setupTestPerMinute: number; subscriptionPerMinute: number;
  logLevel: string; logRetentionDays: number;
  dietoExpressEndpoint: string; dietoExpressApiKeySet: boolean; dietoExpressApiKeySource: 'database' | 'environment' | 'none';
}

/** A null API key keeps the stored one; an empty string clears it (the environment value, if any, applies again). */
export interface OperationsInput {
  additionalCorsOrigins: string[]; setupPerMinute: number; setupTestPerMinute: number; subscriptionPerMinute: number;
  logLevel: string; logRetentionDays: number; dietoExpressEndpoint: string; dietoExpressApiKey: string | null;
}

export interface AdminPlanReview {
  id: string; status: 'pending' | 'in_progress' | 'reviewed'; userId: string; userName: string; userEmail: string;
  goal: string; experience: string; split: string; totalWeeks: number;
  assignedToUserId: string | null; assignedToName: string | null; assignedToMe: boolean;
  preview: string | null; lastAuthor: string | null; messageCount: number; awaitingReply: boolean;
  createdAt: string; updatedAt: string; reviewedAt: string | null;
}

export interface AdminPlanReviewMessage { id: string; author: 'user' | 'staff' | 'system'; authorName: string | null; body: string; createdAt: string; }

export interface AdminPlanReviewDetail {
  review: AdminPlanReview; startDate: string; canTake: boolean; canReply: boolean; canRelease: boolean; canClose: boolean;
  messages: AdminPlanReviewMessage[];
}

export interface WaitlistEntry {
  id: string; email: string; language: 'es' | 'en'; consentAt: string; createdAt: string;
  confirmedAt: string | null; invitedAt: string | null;
}
export type WaitlistStatus = '' | 'pending' | 'confirmed' | 'invited';
export interface WaitlistPage extends Page<WaitlistEntry> {
  overall: { total: number; es: number; en: number; pending: number; confirmed: number; invited: number };
}
export interface WaitlistInviteResult { requested: number; invited: number; failed: number; skipped: number; }

export type ErrorTicketStatus = 'new' | 'seen' | 'resolved';
export interface ErrorTicketSummary {
  id: string; source: 'backend' | 'android'; appVersion: string | null; errorType: string;
  occurrences: number; firstSeenAt: string; lastSeenAt: string; status: ErrorTicketStatus;
}
export interface ErrorTicketDetail extends ErrorTicketSummary { stackTrace: string; originalStackTrace: string | null; }
export interface ErrorTicketPage extends Page<ErrorTicketSummary> { overall: { total: number; new: number; seen: number; resolved: number }; }

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

  overview(refresh = false): Promise<Overview> {
    const params = refresh ? new HttpParams().set('refresh', true) : new HttpParams();
    return firstValueFrom(this.http.get<Overview>(`${API}/overview`, { params }));
  }

  systemInfo(): Promise<SystemInfo> {
    return firstValueFrom(this.http.get<SystemInfo>(`${API}/overview/system`));
  }

  listLogs(filters: LogFilters): Promise<LogPage> {
    let params = new HttpParams().set('pageSize', filters.pageSize).set('levels', filters.levels.join(','));
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.q.trim()) params = params.set('q', filters.q.trim());
    if (filters.cursor) params = params.set('cursor', filters.cursor);
    return firstValueFrom(this.http.get<LogPage>(`${API}/logs`, { params }));
  }

  createUser(input: { displayName: string; email: string; role: string }): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.post<AdminUserDetail>(`${API}/users`, input));
  }

  getUser(id: string): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.get<AdminUserDetail>(`${API}/users/${id}`));
  }

  updateUser(id: string, changes: { displayName?: string; role?: string }): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.patch<AdminUserDetail>(`${API}/users/${id}`, changes));
  }

  deleteUser(id: string, confirmEmail: string): Promise<{ deleted: boolean; playSubscriptionNotCancelled: boolean }> {
    return firstValueFrom(this.http.delete<{ deleted: boolean; playSubscriptionNotCancelled: boolean }>(`${API}/users/${id}`, { body: { confirmEmail } }));
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

  listPlanReviews(status: string, page: number): Promise<Page<AdminPlanReview>> {
    let params = new HttpParams().set('page', page).set('pageSize', 25);
    if (status) params = params.set('status', status);
    return firstValueFrom(this.http.get<Page<AdminPlanReview>>(`${API}/plan-reviews`, { params }));
  }

  planReviewsCounts(): Promise<{ pending: number; mine: number; mineAwaitingReply: number }> {
    return firstValueFrom(this.http.get<{ pending: number; mine: number; mineAwaitingReply: number }>(`${API}/plan-reviews/pending-count`));
  }

  planReview(id: string): Promise<AdminPlanReviewDetail> {
    return firstValueFrom(this.http.get<AdminPlanReviewDetail>(`${API}/plan-reviews/${id}`));
  }

  claimPlanReview(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${API}/plan-reviews/${id}/claim`, {}));
  }

  releasePlanReview(id: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${API}/plan-reviews/${id}/release`, { reason: reason.trim() || null }));
  }

  messagePlanReview(id: string, body: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${API}/plan-reviews/${id}/messages`, { body }));
  }

  closePlanReview(id: string, message: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${API}/plan-reviews/${id}/review`, { message: message.trim() || null }));
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

  private query(params: Record<string, string | boolean | undefined>): HttpParams {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== '') p = p.set(k, String(v));
    return p;
  }

  getReferralSummary(range: { from?: string; to?: string }): Promise<any> {
    return firstValueFrom(this.http.get<any>(`${API}/referrals/summary`, { params: this.query(range) }));
  }

  getFunnel(params: { from?: string; to?: string; channel?: string }): Promise<any> {
    return firstValueFrom(this.http.get<any>(`${API}/analytics/funnel`, { params: this.query(params) }));
  }

  getReferrals(params: { status?: string; incidents?: boolean }): Promise<{ items: any[] }> {
    return firstValueFrom(this.http.get<{ items: any[] }>(`${API}/referrals`, { params: this.query({ ...params, pageSize: '100' }) }));
  }

  getUserLedger(userId: string): Promise<{ items: any[] }> {
    return firstValueFrom(this.http.get<{ items: any[] }>(`${API}/referrals/users/${userId}/ledger`));
  }

  voidGrant(id: string, reason: string): Promise<{ outcome: string }> {
    return firstValueFrom(this.http.post<{ outcome: string }>(`${API}/referrals/grants/${id}/void`, { reason }));
  }

  retryGrant(id: string): Promise<{ outcome: string }> {
    return firstValueFrom(this.http.post<{ outcome: string }>(`${API}/referrals/grants/${id}/retry`, {}));
  }

  reconcileReferrals(): Promise<{ conversions: number; pendingResolved: number; pendingWaiting: number }> {
    return firstValueFrom(this.http.post<{ conversions: number; pendingResolved: number; pendingWaiting: number }>(`${API}/referrals/reconcile`, {}));
  }

  getPromotions(): Promise<PromotionsView> {
    return firstValueFrom(this.http.get<PromotionsView>(`${API}/settings/promotions`));
  }

  savePromotions(settings: PromotionsSettings): Promise<PromotionsView> {
    return firstValueFrom(this.http.put<PromotionsView>(`${API}/settings/promotions`, settings));
  }

  getAccessSettings(): Promise<{ subscriptionsEnforced: boolean }> {
    return firstValueFrom(this.http.get<{ subscriptionsEnforced: boolean }>(`${API}/settings/access`));
  }

  saveAccessSettings(subscriptionsEnforced: boolean): Promise<{ subscriptionsEnforced: boolean }> {
    return firstValueFrom(this.http.put<{ subscriptionsEnforced: boolean }>(`${API}/settings/access`, { subscriptionsEnforced }));
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

  getPush(): Promise<PushView> {
    return firstValueFrom(this.http.get<PushView>(`${API}/settings/push`));
  }

  savePush(input: PushInput): Promise<{ saved: boolean }> {
    return firstValueFrom(this.http.put<{ saved: boolean }>(`${API}/settings/push`, input));
  }

  checkPush(): Promise<PushSendResult> {
    return firstValueFrom(this.http.post<PushSendResult>(`${API}/settings/push/check`, {}));
  }

  testPush(): Promise<PushSendResult> {
    return firstValueFrom(this.http.post<PushSendResult>(`${API}/settings/push/test`, {}));
  }

  broadcastPush(title: string, body: string): Promise<PushSendResult> {
    return firstValueFrom(this.http.post<PushSendResult>(`${API}/settings/push/broadcast`, { title, body }));
  }

  getOperations(): Promise<OperationsView> {
    return firstValueFrom(this.http.get<OperationsView>(`${API}/settings/operations`));
  }

  saveOperations(input: OperationsInput): Promise<OperationsView> {
    return firstValueFrom(this.http.put<OperationsView>(`${API}/settings/operations`, input));
  }

  testDietoExpress(endpoint: string): Promise<{ ok: boolean; message: string }> {
    return firstValueFrom(this.http.post<{ ok: boolean; message: string }>(`${API}/settings/operations/test/dietoexpress`, { endpoint }));
  }

  sendLibraryToDietoExpress(): Promise<TransferResult> {
    return firstValueFrom(this.http.post<TransferResult>(`${API}/exercise-library-transfer/dietoexpress`, {}));
  }

  listWaitlist(q: string, page: number, pageSize: number, status: WaitlistStatus = ''): Promise<WaitlistPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (q.trim()) params = params.set('q', q.trim());
    if (status) params = params.set('status', status);
    return firstValueFrom(this.http.get<WaitlistPage>(`${API}/waitlist`, { params }));
  }

  /** Mails the closed-test invitation to up to 50 confirmed addresses (call it once they are in the Play Console tester list). */
  inviteWaitlist(ids: string[]): Promise<WaitlistInviteResult> {
    return firstValueFrom(this.http.post<WaitlistInviteResult>(`${API}/waitlist/invite`, { ids }));
  }

  deleteWaitlistEntry(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${API}/waitlist/${id}`));
  }

  listErrorTickets(q: string, status: string, source: string, page: number, pageSize: number): Promise<ErrorTicketPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (q.trim()) params = params.set('q', q.trim());
    if (status) params = params.set('status', status);
    if (source) params = params.set('source', source);
    return firstValueFrom(this.http.get<ErrorTicketPage>(`${API}/error-tickets`, { params }));
  }

  getErrorTicket(id: string): Promise<ErrorTicketDetail> {
    return firstValueFrom(this.http.get<ErrorTicketDetail>(`${API}/error-tickets/${id}`));
  }

  setErrorTicketStatus(id: string, status: ErrorTicketStatus): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${API}/error-tickets/${id}/status`, { status }));
  }

  /** Makes the API throw on purpose; resolves with the HTTP status it answered (500 when the chain works). */
  async triggerTestException(): Promise<number> {
    try {
      await firstValueFrom(this.http.post<void>(`${API}/error-tickets/test-exception`, {}));
      return 200;
    } catch (e) {
      return (e as { status?: number }).status ?? 0;
    }
  }

  deleteErrorTicket(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${API}/error-tickets/${id}`));
  }

  /** The CSV is fetched through HttpClient so the bearer token is sent (a plain link would not carry it). */
  exportWaitlist(): Promise<Blob> {
    return firstValueFrom(this.http.get(`${API}/waitlist/export.csv`, { responseType: 'blob' }));
  }

  // ---- Fiscal management (superadmin) ----
  getFiscalProfile(): Promise<any> { return firstValueFrom(this.http.get<any>(`${API}/fiscal/profile`)); }
  saveFiscalProfile(profile: unknown): Promise<any> { return firstValueFrom(this.http.put<any>(`${API}/fiscal/profile`, profile)); }
  getFiscalOptions(): Promise<{ incomeCategories: string[]; expenseCategories: string[]; vatTreatments: string[] }> {
    return firstValueFrom(this.http.get<{ incomeCategories: string[]; expenseCategories: string[]; vatTreatments: string[] }>(`${API}/fiscal/options`));
  }
  getFiscalYear(year: number): Promise<any> { return firstValueFrom(this.http.get<any>(`${API}/fiscal/years/${year}`)); }
  getFiscalMonth(period: string): Promise<any> { return firstValueFrom(this.http.get<any>(`${API}/fiscal/months/${period}`)); }
  getFiscalQuarter(year: number, quarter: number): Promise<any> { return firstValueFrom(this.http.get<any>(`${API}/fiscal/quarters/${year}/${quarter}`)); }
  saveFiscalEntry(id: string | null, entry: unknown): Promise<any> {
    return firstValueFrom(id ? this.http.put<any>(`${API}/fiscal/entries/${id}`, entry) : this.http.post<any>(`${API}/fiscal/entries`, entry));
  }
  deleteFiscalEntry(id: string): Promise<void> { return firstValueFrom(this.http.delete<void>(`${API}/fiscal/entries/${id}`)); }
  bookGoogleEstimate(period: string): Promise<any> { return firstValueFrom(this.http.post<any>(`${API}/fiscal/months/${period}/book-google-estimate`, {})); }
  closeFiscalMonth(period: string, acknowledgeWarnings: boolean): Promise<any> {
    return firstValueFrom(this.http.post<any>(`${API}/fiscal/months/${period}/close`, { acknowledgeWarnings }));
  }
  reopenFiscalMonth(period: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${API}/fiscal/months/${period}/reopen`, { reason }));
  }
  getFiscalAudit(): Promise<{ items: any[] }> { return firstValueFrom(this.http.get<{ items: any[] }>(`${API}/fiscal/audit`)); }
  /** ZIPs are fetched through HttpClient so the bearer token is sent. */
  downloadFiscalMonth(period: string): Promise<Blob> { return firstValueFrom(this.http.get(`${API}/fiscal/months/${period}/package`, { responseType: 'blob' })); }
  downloadFiscalQuarter(year: number, quarter: number): Promise<Blob> {
    return firstValueFrom(this.http.get(`${API}/fiscal/quarters/${year}/${quarter}/package`, { responseType: 'blob' }));
  }
}
