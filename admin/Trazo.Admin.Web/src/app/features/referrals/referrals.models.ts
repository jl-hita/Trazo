export interface ReferralSummary {
  from: string; to: string; codesCreated: number; shares: number; linkOpens: number; installsAttributed: number;
  attributed: number; rewarded: number; voided: number; skipped: number; rewardDaysGranted: number; rewardsPending: number;
  rewardDaysPending: number; rewardsDeferred: number; rewardsPromo: number; incidentCount: number;
  linkToRegistered: number | null; registeredToPaid: number | null; paidMonthly: number; paidAnnual: number;
  renewedAtLeastOnce: number; renewedAtLeastThree: number; topCodes: FunnelGroup[]; minGroupSize: number;
}
export interface FunnelStage { name: string; users: number; rateOfPrevious: number | null; rateOfCohort: number | null; }
export interface FunnelGroup { key: string; users: number; paid: number; }
export interface FunnelCohort { week: string; signups: number; paid: number; renewed: number; }
export interface ReferralFunnel {
  from: string; to: string; cohort: number; stages: FunnelStage[]; trialToPaid: number | null; signupToPaid: number | null;
  signupToTrial: number | null; retainedAfterSeveralCycles: number | null; byPlan: FunnelGroup[]; byAppVersion: FunnelGroup[];
  byReference: FunnelGroup[]; cohorts: FunnelCohort[]; minGroupSize: number;
}
export interface ReferralAdminItem {
  id: string; code: string; source: string; status: string; attributedAt: string; convertedAt: string | null; plan: string | null;
  rewardDays: number | null; incident: string | null; appVersion: string | null; referrerId: string; referrerEmail: string | null;
  referredId: string; referredEmail: string | null; rewardGrantId: string | null; rewardStatus: string | null; lastDeferNote: string | null;
}
export interface PremiumGrantLedgerItem {
  id: string; kind: string; sourceKey: string | null; days: number; status: string; grantedAt: string; startsAt: string | null;
  endsAt: string | null; appliedAt: string | null; voidedAt: string | null; voidReason: string | null; voidedBy: string | null;
  originUserId: string | null; originSubscriptionId: string | null; deferredSubscriptionId: string | null;
  googleExpiryBefore: string | null; googleExpiryAfter: string | null; deferAttempts: number; lastDeferError: string | null;
}
export interface ReferralAdminPage { total: number; page: number; pageSize: number; items: ReferralAdminItem[]; }
