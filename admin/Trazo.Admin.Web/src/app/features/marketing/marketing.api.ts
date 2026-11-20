import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import { HttpParams } from '@angular/common/http';
import { errorMessage } from '../../core/api-error';
import { formatUiCurrency, formatUiNumber, formatUiPercent } from '../../core/ui-format';

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

/** Shared, localized presentation for API failures in all marketing tabs. */
export function marketingError(error: unknown): string {
  return errorMessage(error);
}

/** Calls to the marketing panel (superadmin and commercial roles). Everything it returns is an aggregate. */
@Injectable({ providedIn: 'root' })
export class MarketingApi {
  private readonly http = inject(ApiHttpClient);

  private params(range: Range, extra: Record<string, string> = {}): HttpParams {
    let p = new HttpParams().set('from', `${range.from}T00:00:00Z`).set('to', `${nextDay(range.to)}T00:00:00Z`);
    for (const [k, v] of Object.entries(extra)) if (v) p = p.set(k, v);
    return p;
  }

  options(): Promise<MarketingOptions> { return this.http.get<MarketingOptions>(`${API}/options`); }
  summary(r: Range): Promise<SummaryReport> { return this.http.get<SummaryReport>(`${API}/summary`, { params: this.params(r) }); }
  strategies(r: Range): Promise<StrategiesReport> { return this.http.get<StrategiesReport>(`${API}/strategies`, { params: this.params(r) }); }
  strategiesCsv(r: Range): Promise<Blob> { return this.http.get(`${API}/strategies.csv`, { params: this.params(r), responseType: 'blob' }); }
  funnel(r: Range, segment: string): Promise<MarketingFunnel> { return this.http.get<MarketingFunnel>(`${API}/funnel`, { params: this.params(r, { segment }) }); }
  retention(r: Range, segment: string): Promise<RetentionReport> { return this.http.get<RetentionReport>(`${API}/retention`, { params: this.params(r, { segment }) }); }
  paywall(r: Range): Promise<PaywallReport> { return this.http.get<PaywallReport>(`${API}/paywall`, { params: this.params(r) }); }
  economics(r: Range): Promise<EconomicsReport> { return this.http.get<EconomicsReport>(`${API}/economics`, { params: this.params(r) }); }
  downloads(r: Range): Promise<DownloadsReport> { return this.http.get<DownloadsReport>(`${API}/downloads`, { params: this.params(r) }); }
  saveDownloads(input: { week: string; downloads: number; note: string | null }): Promise<{ week: string; downloads: number }> {
    return this.http.put<{ week: string; downloads: number }>(`${API}/downloads`, input);
  }
  deleteDownloads(week: string): Promise<void> { return this.http.delete<void>(`${API}/downloads/${week}`); }
  waitlist(): Promise<WaitlistReport> { return this.http.get<WaitlistReport>(`${API}/waitlist`); }

  campaigns(): Promise<{ items: CampaignItem[] }> { return this.http.get<{ items: CampaignItem[] }>(`${API}/campaigns`); }
  createCampaign(input: CampaignInput): Promise<CampaignItem> { return this.http.post<CampaignItem>(`${API}/campaigns`, input); }
  updateCampaign(id: string, input: CampaignInput): Promise<CampaignItem> { return this.http.put<CampaignItem>(`${API}/campaigns/${id}`, input); }
  campaign(id: string, r: Range): Promise<CampaignDetail> { return this.http.get<CampaignDetail>(`${API}/campaigns/${id}`, { params: this.params(r) }); }
  addSpend(id: string, input: { day: string; amount: number; note: string | null }): Promise<SpendEntry> {
    return this.http.post<SpendEntry>(`${API}/campaigns/${id}/spend`, input);
  }
  deleteSpend(id: string, spendId: string): Promise<void> { return this.http.delete<void>(`${API}/campaigns/${id}/spend/${spendId}`); }
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
  pct: (v: number | null | undefined, digits = 1): string => formatUiPercent(v, digits),
  eur: (v: number | null | undefined): string => v === null || v === undefined ? '—' : formatUiCurrency(v),
  int: (v: number | null | undefined): string => v === null || v === undefined ? '—' : formatUiNumber(v, 0),
  delta: (current: number, previous: number): string => {
    if (previous === 0) return current === 0 ? '—' : 'nuevo';
    const change = (current - previous) / previous;
    return `${change >= 0 ? '+' : ''}${(change * 100).toFixed(0)}%`;
  }
};
