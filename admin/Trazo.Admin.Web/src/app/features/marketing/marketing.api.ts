import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { errorMessage } from '../../core/setup.api';

/** A date range, both ends as yyyy-MM-dd (the end is exclusive on the server: the day after). */
export interface Range { from: string; to: string; }

export interface SegmentRow {
  kind: string; key: string; name: string; channel: string | null; status: string | null;
  clicks: number; installs: number; signups: number; verified: number; onboarded: number; activated: number; trials: number;
  paid: number; renewed: number; canceled: number; refunded: number;
  d7Eligible: number; d7Active: number; d30Eligible: number; d30Active: number;
  spend: number; estimatedRevenue: number;
  clickToInstall: number | null; installToSignup: number | null; signupToActivated: number | null; signupToTrial: number | null;
  signupToPaid: number | null; signupToPaidLow: number | null; signupToPaidHigh: number | null; trialToPaid: number | null;
  paidRenewed: number | null; d7: number | null; d30: number | null;
  costPerInstall: number | null; costPerSignup: number | null; costPerTrial: number | null; costPerPaid: number | null;
  returnOnSpend: number | null; reliable: boolean;
}

export interface Insight { kind: 'good' | 'warn' | 'info'; title: string; detail: string; }

export interface StrategiesReport {
  from: string; to: string; minReliable: number; overall: SegmentRow; channels: SegmentRow[]; campaigns: SegmentRow[]; insights: Insight[];
}

export interface FunnelStage { name: string; users: number; ofPrevious: number | null; ofCohort: number | null; }
export interface MarketingFunnel {
  segment: string; segmentName: string; cohort: number; stages: FunnelStage[];
  signupToPaid: number | null; signupToPaidLow: number | null; signupToPaidHigh: number | null; trialToPaid: number | null; paidRenewed: number | null;
}

export interface RetentionCohort { week: string; size: number; paid: number; renewed: number; active: (number | null)[]; }
export interface RetentionReport { segment: string; segmentName: string; weeks: number; cohort: number; average: (number | null)[]; cohorts: RetentionCohort[]; }

export interface Kpi { key: string; label: string; current: number; previous: number; }
export interface WeekPoint { week: string; signups: number; trials: number; firstPurchases: number; renewals: number; cancellations: number; refunds: number; paywallViews: number; }
export interface MixRow { key: string; name: string; signups: number; paid: number; }
export interface SummaryReport {
  kpis: Kpi[]; signupToTrial: number | null; signupToPaid: number | null; trialToPaid: number | null; cohort: number; paywallViews: number;
  subscribers: { paying: number; trialing: number; cancelingAtPeriodEnd: number; billing: number };
  mix: MixRow[]; weeks: WeekPoint[]; waitlistConfirmed: number; activeCampaigns: number; downloads: number | null; downloadToSignup: number | null;
}

export interface PaywallReport {
  total: number; firstPurchases: number; viewsPerPurchase: number | null;
  sources: { source: string; views: number; share: number }[];
  weeks: { week: string; total: number; bySource: Record<string, number> }[];
}

export interface WaitlistReport {
  total: number; confirmed: number; invited: number; confirmRate: number | null; inviteRate: number | null;
  byLanguage: Record<string, number>; weeks: { week: string; joined: number }[];
}

export interface Campaign {
  id: string; slug: string; name: string; channel: string; status: string; startsOn: string | null; endsOn: string | null; notes: string | null;
  createdAt: string; clicks: number; installs: number; signups: number; spend: number;
}
export interface CampaignItem { campaign: Campaign; link: string; }
export interface CampaignInput { slug?: string; name: string; channel: string; status: string; startsOn: string | null; endsOn: string | null; notes: string | null; }
export interface SpendEntry { id: string; day: string; amount: number; note: string | null; createdBy: string | null; }
export interface CampaignDetail {
  campaign: Campaign; row: SegmentRow; spend: SpendEntry[];
  days: { day: string; clicks: number; installs: number; signups: number; spend: number }[];
}

export interface ReferralReport {
  codesCreated: number; shares: number; linkOpens: number; installsAttributed: number; attributed: number; rewarded: number; voided: number;
  rewardDaysGranted: number; linkToRegistered: number | null; registeredToPaid: number | null; paidMonthly: number; paidAnnual: number;
  renewedAtLeastOnce: number; renewedAtLeastThree: number; topCodes: { key: string; users: number; paid: number }[];
}

export interface DownloadWeek { week: string; downloads: number | null; signups: number; downloadToSignup: number | null; note: string | null; }
export interface DownloadsReport {
  weeks: DownloadWeek[]; downloads: number; signupsInThoseWeeks: number; downloadToSignup: number | null; unregisteredInstalls: number;
  weeksWithData: number; weeksMissing: number;
}

export interface EconomicsRow {
  kind: string; key: string; name: string; signups: number; payers: number; spend: number; netRevenue: number; paidMonths: number;
  cac: number | null; netPerMonth: number | null; monthlyChurn: number | null; lifetimeMonths: number | null; ltv: number | null;
  ltvToCac: number | null; paybackMonths: number | null; reliable: boolean;
}
export interface EconomicsReport {
  minPayers: number; lifetimeCapMonths: number; overall: EconomicsRow; rows: EconomicsRow[];
  survival: { months: number; retained: number | null; eligible: number }[]; insights: Insight[];
}

export interface MarketingOptions { channels: string[]; statuses: string[]; minReliable: number; retentionWeeks: number; }

const API = '/api/v1/admin/marketing';

/** The message the server gave for a rejected request, or a generic one. */
export function marketingError(error: unknown): string {
  const e = error as { error?: { message?: string } };
  return e?.error?.message ?? errorMessage(error);
}

/** Calls to the marketing panel (superadmin and commercial roles). Everything it returns is an aggregate. */
@Injectable({ providedIn: 'root' })
export class MarketingApi {
  private readonly http = inject(HttpClient);

  private params(range: Range, extra: Record<string, string> = {}): HttpParams {
    let p = new HttpParams().set('from', `${range.from}T00:00:00Z`).set('to', `${nextDay(range.to)}T00:00:00Z`);
    for (const [k, v] of Object.entries(extra)) if (v) p = p.set(k, v);
    return p;
  }

  options(): Promise<MarketingOptions> { return firstValueFrom(this.http.get<MarketingOptions>(`${API}/options`)); }
  summary(r: Range): Promise<SummaryReport> { return firstValueFrom(this.http.get<SummaryReport>(`${API}/summary`, { params: this.params(r) })); }
  strategies(r: Range): Promise<StrategiesReport> { return firstValueFrom(this.http.get<StrategiesReport>(`${API}/strategies`, { params: this.params(r) })); }
  strategiesCsv(r: Range): Promise<Blob> { return firstValueFrom(this.http.get(`${API}/strategies.csv`, { params: this.params(r), responseType: 'blob' })); }
  funnel(r: Range, segment: string): Promise<MarketingFunnel> { return firstValueFrom(this.http.get<MarketingFunnel>(`${API}/funnel`, { params: this.params(r, { segment }) })); }
  retention(r: Range, segment: string): Promise<RetentionReport> { return firstValueFrom(this.http.get<RetentionReport>(`${API}/retention`, { params: this.params(r, { segment }) })); }
  paywall(r: Range): Promise<PaywallReport> { return firstValueFrom(this.http.get<PaywallReport>(`${API}/paywall`, { params: this.params(r) })); }
  economics(r: Range): Promise<EconomicsReport> { return firstValueFrom(this.http.get<EconomicsReport>(`${API}/economics`, { params: this.params(r) })); }
  downloads(r: Range): Promise<DownloadsReport> { return firstValueFrom(this.http.get<DownloadsReport>(`${API}/downloads`, { params: this.params(r) })); }
  saveDownloads(input: { week: string; downloads: number; note: string | null }): Promise<{ week: string; downloads: number }> {
    return firstValueFrom(this.http.put<{ week: string; downloads: number }>(`${API}/downloads`, input));
  }
  deleteDownloads(week: string): Promise<void> { return firstValueFrom(this.http.delete<void>(`${API}/downloads/${week}`)); }
  waitlist(): Promise<WaitlistReport> { return firstValueFrom(this.http.get<WaitlistReport>(`${API}/waitlist`)); }
  referrals(r: Range): Promise<ReferralReport> { return firstValueFrom(this.http.get<ReferralReport>(`${API}/referrals`, { params: this.params(r) })); }

  campaigns(): Promise<{ items: CampaignItem[] }> { return firstValueFrom(this.http.get<{ items: CampaignItem[] }>(`${API}/campaigns`)); }
  createCampaign(input: CampaignInput): Promise<CampaignItem> { return firstValueFrom(this.http.post<CampaignItem>(`${API}/campaigns`, input)); }
  updateCampaign(id: string, input: CampaignInput): Promise<CampaignItem> { return firstValueFrom(this.http.put<CampaignItem>(`${API}/campaigns/${id}`, input)); }
  campaign(id: string, r: Range): Promise<CampaignDetail> { return firstValueFrom(this.http.get<CampaignDetail>(`${API}/campaigns/${id}`, { params: this.params(r) })); }
  addSpend(id: string, input: { day: string; amount: number; note: string | null }): Promise<SpendEntry> {
    return firstValueFrom(this.http.post<SpendEntry>(`${API}/campaigns/${id}/spend`, input));
  }
  deleteSpend(id: string, spendId: string): Promise<void> { return firstValueFrom(this.http.delete<void>(`${API}/campaigns/${id}/spend/${spendId}`)); }
}

export function nextDay(day: string): string {
  const d = new Date(`${day}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}

export function isoDay(date: Date): string { return date.toISOString().slice(0, 10); }
export function daysAgo(n: number): string { const d = new Date(); d.setUTCDate(d.getUTCDate() - n); return isoDay(d); }

/** Formatting shared by the panel tabs. */
export const fmt = {
  pct: (v: number | null | undefined, digits = 1): string => (v === null || v === undefined ? '—' : `${(v * 100).toFixed(digits).replace(/\.0+$/, '')}%`),
  eur: (v: number | null | undefined): string => (v === null || v === undefined ? '—' : `${v.toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} €`),
  int: (v: number | null | undefined): string => (v === null || v === undefined ? '—' : v.toLocaleString('en-GB')),
  delta: (current: number, previous: number): string => {
    if (previous === 0) return current === 0 ? '—' : 'new';
    const change = (current - previous) / previous;
    return `${change >= 0 ? '+' : ''}${(change * 100).toFixed(0)}%`;
  }
};
