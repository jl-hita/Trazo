export interface Page<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface AdminUserSummary {
  id: string; email: string; displayName: string; role: string;
  emailVerified: boolean; twoFactorEnabled: boolean; hasPassword: boolean; hasGoogle: boolean;
  disabled: boolean; lockedOut: boolean; createdAt: string; lastLoginAt: string | null;
}

export interface AdminUserSession { deviceName: string | null; createdAt: string; expiresAt: string; }

export interface AdminPremiumGrant {
  id: string; kind: string; status: string; days: number; permanent: boolean;
  startsAt: string | null; endsAt: string | null; reason: string | null; grantedBy: string | null; grantedAt: string;
  revokedAt: string | null; revocationReason: string | null; revokedBy: string | null; isCurrent: boolean;
}

export interface AdminUserDetail extends AdminUserSummary {
  failedLoginAttempts: number; disabledAt: string | null; activeSessionCount: number; sessions: AdminUserSession[];
}

export interface UserFilters { q: string; role: string; status: string; sort: string; page: number; pageSize: number; }

export type UserAction = 'disable' | 'enable' | 'revoke-sessions' | 'unlock' | 'verify-email';

export interface AdminExerciseSummary {
  id: string; source: string; nameEs: string; nameEn: string; difficulty: string; equipment: string | null;
  movementPattern: string; minimumExperience: string; primaryMuscles: string[]; secondaryMuscles: string[];
  selectionPriority: number; isActive: boolean; locallyCurated: boolean; imageCount: number; fromSeed: boolean;
}

export interface AdminExerciseDetail {
  id: string; source: string; nameEs: string; nameEn: string; descriptionEs: string; instructionsEs: string[]; tipsEs: string[];
  forceType: string; mechanic: string; difficulty: string; equipment: string | null; bodyPart: string; met: number;
  isUnilateral: boolean; isBodyweight: boolean; movementPattern: string; primaryMuscles: string[]; secondaryMuscles: string[];
  minimumExperience: string; suitableGoals: string[]; requiredEquipment: string[]; selectionPriority: number;
  isActive: boolean; locallyCurated: boolean; importedFrom: string | null; imageSlots: number[]; createdAt: string; updatedAt: string;
  fromSeed: boolean; effectiveSecondaryMuscles: string[]; emphasis: string[]; limitationAreas: string[]; metadataReviewed: boolean; allowedTechniques: string[];
}

export interface AdminExerciseInput {
  id?: string; nameEs: string; nameEn: string; descriptionEs: string; instructionsEs: string[]; tipsEs: string[];
  forceType: string; mechanic: string; difficulty: string; equipment: string; bodyPart: string; met: number;
  isUnilateral: boolean; isBodyweight: boolean; movementPattern: string; primaryMuscles: string[]; secondaryMuscles: string[];
  minimumExperience: string; suitableGoals: string[]; requiredEquipment: string[]; selectionPriority: number;
  effectiveSecondaryMuscles: string[]; emphasis: string[]; limitationAreas: string[]; metadataReviewed: boolean; allowedTechniques: string[];
}

export interface AdminExerciseOptions {
  movementPatterns: string[]; difficulties: string[]; experiences: string[]; equipment: string[]; forceTypes: string[];
  mechanics: string[]; bodyParts: string[]; sources: string[]; muscles: string[]; goals: string[]; emphases: string[]; limitationAreas: string[]; techniques: string[];
}

/** Where the database differs from the exercise seed shipped with the API. */
export interface AdminSeedReport {
  seedExercises: number; databaseExercises: number; missingInDatabase: string[];
  differences: { exerciseId: string; engineFields: string[]; contentFields: string[]; translations: string[] }[];
  outsideSeed: { exerciseId: string; source: string; isActive: boolean }[];
  invalid: { exerciseId: string; source: string; isActive: boolean; errors: Record<string, string[]> }[];
  languages: { language: string; complete: number; incomplete: number }[];
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

/** Android version codes; 0 turns the notice off. */
export interface AppVersionSettings { minimumAndroidVersionCode: number; recommendedAndroidVersionCode: number; }

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

export type BackupHealth = 'healthy' | 'warning' | 'error' | 'unknown';

export interface BackupCopy { name: string; sizeBytes: number; createdAt: string; }

export interface BackupLastRun { ok: boolean; startedAt: string | null; finishedAt: string | null; copy: string | null; error: string | null; }

export interface BackupRestoreStatus {
  state: 'running' | 'ok' | 'failed'; mode: 'test' | 'production' | null; copy: string | null;
  startedAt: string | null; finishedAt: string | null; message: string | null;
}

export interface BackupStatus {
  configured: boolean; health: BackupHealth; message: string | null; checkedAt: string | null; maxAgeHours: number;
  remote: string | null; lastRun: BackupLastRun | null; copies: BackupCopy[];
  /** A backup is being made right now. */
  running: boolean;
  /** How the last verification or restore requested from the panel went. */
  restore: BackupRestoreStatus | null;
}

export type BackupRestoreMode = 'test' | 'production';
export interface BackupActionResult { ok: boolean; message: string; }

export interface BackupAlertCandidate { id: string; displayName: string; email: string; role: string; devices: number; }

/** Who gets the push notification when the backups go wrong; `candidates` are the active staff accounts that can be chosen. */
export interface BackupAlertView {
  recipientUserId: string | null; recipientName: string | null; recipientDevices: number; candidates: BackupAlertCandidate[];
}

export interface BackupAlertTestResult { ok: boolean; message: string; }
