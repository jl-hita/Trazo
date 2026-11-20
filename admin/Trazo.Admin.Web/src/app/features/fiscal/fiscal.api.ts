import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';

import type {
  FiscalProfile,
  FiscalEntry,
  FiscalEntryInput,
  FiscalTotals,
  FiscalMonthRow,
  FiscalQuarterRow,
  FiscalYearView,
  FiscalWarning,
  FiscalEstimate,
  FiscalClosure,
  FiscalMonthView,
  FiscalAuditEntry,
  FiscalQuarterSummary
} from './fiscal.models';

const API = '/api/v1/admin';

/** API operations and contracts for the superadmin fiscal workspace. */
@Injectable({ providedIn: 'root' })
export class FiscalApi {
  private readonly http = inject(ApiHttpClient);

  // ---- Fiscal management (superadmin) ----
  getFiscalProfile(): Promise<FiscalProfile> { return this.http.get<FiscalProfile>(`${API}/fiscal/profile`); }
  saveFiscalProfile(profile: FiscalProfile): Promise<FiscalProfile> { return this.http.put<FiscalProfile>(`${API}/fiscal/profile`, profile); }
  getFiscalOptions(): Promise<{ incomeCategories: string[]; expenseCategories: string[]; vatTreatments: string[] }> {
    return this.http.get<{ incomeCategories: string[]; expenseCategories: string[]; vatTreatments: string[] }>(`${API}/fiscal/options`);
  }
  getFiscalYear(year: number): Promise<FiscalYearView> { return this.http.get<FiscalYearView>(`${API}/fiscal/years/${year}`); }
  getFiscalMonth(period: string): Promise<FiscalMonthView> { return this.http.get<FiscalMonthView>(`${API}/fiscal/months/${period}`); }
  getFiscalQuarter(year: number, quarter: number): Promise<FiscalQuarterSummary> { return this.http.get<FiscalQuarterSummary>(`${API}/fiscal/quarters/${year}/${quarter}`); }
  saveFiscalEntry(id: string | null, entry: FiscalEntryInput): Promise<FiscalEntry> {
    return id ? this.http.put<FiscalEntry>(`${API}/fiscal/entries/${id}`, entry) : this.http.post<FiscalEntry>(`${API}/fiscal/entries`, entry);
  }
  deleteFiscalEntry(id: string): Promise<void> { return this.http.delete<void>(`${API}/fiscal/entries/${id}`); }
  bookGoogleEstimate(period: string): Promise<FiscalEntry> { return this.http.post<FiscalEntry>(`${API}/fiscal/months/${period}/book-google-estimate`, {}); }
  closeFiscalMonth(period: string, acknowledgeWarnings: boolean): Promise<FiscalMonthView> {
    return this.http.post<FiscalMonthView>(`${API}/fiscal/months/${period}/close`, { acknowledgeWarnings });
  }
  reopenFiscalMonth(period: string, reason: string): Promise<void> {
    return this.http.post<void>(`${API}/fiscal/months/${period}/reopen`, { reason });
  }
  getFiscalAudit(): Promise<{ items: FiscalAuditEntry[] }> { return this.http.get<{ items: FiscalAuditEntry[] }>(`${API}/fiscal/audit`); }
  /** ZIPs are fetched through HttpClient so the bearer token is sent. */
  downloadFiscalMonth(period: string): Promise<Blob> { return this.http.get(`${API}/fiscal/months/${period}/package`, { responseType: 'blob' }); }
  downloadFiscalQuarter(year: number, quarter: number): Promise<Blob> {
    return this.http.get(`${API}/fiscal/quarters/${year}/${quarter}/package`, { responseType: 'blob' });
  }
}
