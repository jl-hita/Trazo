import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { errorMessage } from '../../core/setup.api';

import type {
  Range,
  SegmentRow,
  Insight,
  StrategiesReport,
  FunnelStage,
  MarketingFunnel,
  RetentionCohort,
  RetentionReport,
  Kpi,
  WeekPoint,
  MixRow,
  SummaryReport,
  PaywallReport,
  WaitlistReport,
  Campaign,
  CampaignItem,
  CampaignInput,
  SpendEntry,
  CampaignDetail,
  DownloadWeek,
  DownloadsReport,
  EconomicsRow,
  EconomicsReport,
  MarketingOptions
} from './marketing.models';

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
