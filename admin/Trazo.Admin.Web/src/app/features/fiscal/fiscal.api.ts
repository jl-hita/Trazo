import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

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
  private readonly http = inject(HttpClient);

  // ---- Fiscal management (superadmin) ----
  getFiscalProfile(): Promise<FiscalProfile> { return firstValueFrom(this.http.get<FiscalProfile>(`${API}/fiscal/profile`)); }
  saveFiscalProfile(profile: FiscalProfile): Promise<FiscalProfile> { return firstValueFrom(this.http.put<FiscalProfile>(`${API}/fiscal/profile`, profile)); }
  getFiscalOptions(): Promise<{ incomeCategories: string[]; expenseCategories: string[]; vatTreatments: string[] }> {
    return firstValueFrom(this.http.get<{ incomeCategories: string[]; expenseCategories: string[]; vatTreatments: string[] }>(`${API}/fiscal/options`));
  }
  getFiscalYear(year: number): Promise<FiscalYearView> { return firstValueFrom(this.http.get<FiscalYearView>(`${API}/fiscal/years/${year}`)); }
  getFiscalMonth(period: string): Promise<FiscalMonthView> { return firstValueFrom(this.http.get<FiscalMonthView>(`${API}/fiscal/months/${period}`)); }
  getFiscalQuarter(year: number, quarter: number): Promise<FiscalQuarterSummary> { return firstValueFrom(this.http.get<FiscalQuarterSummary>(`${API}/fiscal/quarters/${year}/${quarter}`)); }
  saveFiscalEntry(id: string | null, entry: FiscalEntryInput): Promise<FiscalEntry> {
    return firstValueFrom(id ? this.http.put<FiscalEntry>(`${API}/fiscal/entries/${id}`, entry) : this.http.post<FiscalEntry>(`${API}/fiscal/entries`, entry));
  }
  deleteFiscalEntry(id: string): Promise<void> { return firstValueFrom(this.http.delete<void>(`${API}/fiscal/entries/${id}`)); }
  bookGoogleEstimate(period: string): Promise<FiscalEntry> { return firstValueFrom(this.http.post<FiscalEntry>(`${API}/fiscal/months/${period}/book-google-estimate`, {})); }
  closeFiscalMonth(period: string, acknowledgeWarnings: boolean): Promise<FiscalMonthView> {
    return firstValueFrom(this.http.post<FiscalMonthView>(`${API}/fiscal/months/${period}/close`, { acknowledgeWarnings }));
  }
  reopenFiscalMonth(period: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${API}/fiscal/months/${period}/reopen`, { reason }));
  }
  getFiscalAudit(): Promise<{ items: FiscalAuditEntry[] }> { return firstValueFrom(this.http.get<{ items: FiscalAuditEntry[] }>(`${API}/fiscal/audit`)); }
  /** ZIPs are fetched through HttpClient so the bearer token is sent. */
  downloadFiscalMonth(period: string): Promise<Blob> { return firstValueFrom(this.http.get(`${API}/fiscal/months/${period}/package`, { responseType: 'blob' })); }
  downloadFiscalQuarter(year: number, quarter: number): Promise<Blob> {
    return firstValueFrom(this.http.get(`${API}/fiscal/quarters/${year}/${quarter}/package`, { responseType: 'blob' }));
  }
}
