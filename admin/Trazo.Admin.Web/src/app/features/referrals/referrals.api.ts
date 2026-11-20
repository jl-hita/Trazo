import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';

import type {
  ReferralSummary,
  FunnelStage,
  FunnelGroup,
  FunnelCohort,
  ReferralFunnel,
  ReferralAdminItem,
  PremiumGrantLedgerItem,
  ReferralAdminPage
} from './referrals.models';

const API = '/api/v1/admin';

/** API operations and contracts for referral analytics and reward administration. */
@Injectable({ providedIn: 'root' })
export class ReferralsApi {
  private readonly http = inject(ApiHttpClient);

  private query(params: Record<string, string | boolean | undefined>): HttpParams {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== '') p = p.set(k, String(v));
    return p;
  }

  getReferralProgrammeSettings(): Promise<{ referralsEnabled: boolean; referralRewardDays: number; maxDays: number }> {
    return this.http.get<{ referralsEnabled: boolean; referralRewardDays: number; maxDays: number }>(`${API}/referrals/settings`);
  }

  saveReferralProgrammeSettings(input: { enabled: boolean; rewardDays: number }): Promise<{ referralsEnabled: boolean; referralRewardDays: number; maxDays: number }> {
    return this.http.put<{ referralsEnabled: boolean; referralRewardDays: number; maxDays: number }>(`${API}/referrals/settings`, input);
  }

  getReferralSummary(range: { from?: string; to?: string }): Promise<ReferralSummary> {
    return this.http.get<ReferralSummary>(`${API}/referrals/summary`, { params: this.query(range) });
  }

  getFunnel(params: { from?: string; to?: string; channel?: string }): Promise<ReferralFunnel> {
    return this.http.get<ReferralFunnel>(`${API}/analytics/funnel`, { params: this.query(params) });
  }

  getReferrals(params: { status?: string; incidents?: boolean; from?: string; to?: string; page?: number; pageSize?: number }): Promise<ReferralAdminPage> {
    return this.http.get<ReferralAdminPage>(`${API}/referrals`, { params: this.query({ ...params, page: String(params.page ?? 1), pageSize: String(params.pageSize ?? 25) }) });
  }

  getUserLedger(userId: string): Promise<{ items: PremiumGrantLedgerItem[] }> {
    return this.http.get<{ items: PremiumGrantLedgerItem[] }>(`${API}/referrals/users/${userId}/ledger`);
  }

  voidGrant(id: string, reason: string): Promise<{ outcome: string }> {
    return this.http.post<{ outcome: string }>(`${API}/referrals/grants/${id}/void`, { reason });
  }

  retryGrant(id: string): Promise<{ outcome: string }> {
    return this.http.post<{ outcome: string }>(`${API}/referrals/grants/${id}/retry`, {});
  }

  reconcileReferrals(): Promise<{ conversions: number; pendingResolved: number; pendingWaiting: number }> {
    return this.http.post<{ conversions: number; pendingResolved: number; pendingWaiting: number }>(`${API}/referrals/reconcile`, {});
  }

}
