import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

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

const API = '/api/v1/admin';

/** API operations and contracts for referral analytics and reward administration. */
@Injectable({ providedIn: 'root' })
export class ReferralsApi {
  private readonly http = inject(HttpClient);

  private query(params: Record<string, string | boolean | undefined>): HttpParams {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== '') p = p.set(k, String(v));
    return p;
  }

  getReferralProgrammeSettings(): Promise<{ referralsEnabled: boolean; referralRewardDays: number; maxDays: number }> {
    return firstValueFrom(this.http.get<{ referralsEnabled: boolean; referralRewardDays: number; maxDays: number }>(`${API}/referrals/settings`));
  }

  saveReferralProgrammeSettings(input: { enabled: boolean; rewardDays: number }): Promise<{ referralsEnabled: boolean; referralRewardDays: number; maxDays: number }> {
    return firstValueFrom(this.http.put<{ referralsEnabled: boolean; referralRewardDays: number; maxDays: number }>(`${API}/referrals/settings`, input));
  }

  getReferralSummary(range: { from?: string; to?: string }): Promise<ReferralSummary> {
    return firstValueFrom(this.http.get<ReferralSummary>(`${API}/referrals/summary`, { params: this.query(range) }));
  }

  getFunnel(params: { from?: string; to?: string; channel?: string }): Promise<ReferralFunnel> {
    return firstValueFrom(this.http.get<ReferralFunnel>(`${API}/analytics/funnel`, { params: this.query(params) }));
  }

  getReferrals(params: { status?: string; incidents?: boolean; from?: string; to?: string; page?: number; pageSize?: number }): Promise<ReferralAdminPage> {
    return firstValueFrom(this.http.get<ReferralAdminPage>(`${API}/referrals`, { params: this.query({ ...params, page: String(params.page ?? 1), pageSize: String(params.pageSize ?? 25) }) }));
  }

  getUserLedger(userId: string): Promise<{ items: PremiumGrantLedgerItem[] }> {
    return firstValueFrom(this.http.get<{ items: PremiumGrantLedgerItem[] }>(`${API}/referrals/users/${userId}/ledger`));
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

}
