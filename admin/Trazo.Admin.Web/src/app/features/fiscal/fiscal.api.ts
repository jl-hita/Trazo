import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface FiscalProfile {
  legalName: string; taxId: string; address: string; gestorEmail: string | null; defaultVatPct: number; irpfAdvancePct: number;
  filesModelo130: boolean; playMonthlyPrice: number; playAnnualPrice: number; consumerVatPct: number; googleFeePct: number;
}
export interface FiscalEntry {
  id: string; kind: string; date: string; period: string; concept: string; counterparty: string | null;
  counterpartyTaxId: string | null; counterpartyCountry: string | null; invoiceNumber: string | null; category: string;
  base: number; vatTreatment: string; vatRatePct: number; vatAmount: number; withholdingPct: number; withholdingAmount: number;
  deductiblePct: number; total: number; source: string; notes: string | null; createdBy: string | null; createdAt: string; updatedAt: string;
}
export interface FiscalEntryInput {
  kind: string; date: string; concept: string; counterparty: string | null; counterpartyTaxId: string | null;
  counterpartyCountry: string | null; invoiceNumber: string | null; category: string; base: number | null;
  vatTreatment: string; vatRatePct: number | null; withholdingPct: number | null; deductiblePct: number | null; notes: string;
}
export interface FiscalTotals {
  incomeCount: number; expenseCount: number; incomeBase: number; incomeDomesticBase: number; incomeEuB2bBase: number;
  incomeNoVatBase: number; vatOnIncome: number; selfAssessedVat: number; vatOutput: number; expenseBase: number;
  deductibleCost: number; vatInput: number; vatBalance: number; withholdingsSuffered: number; netResult: number;
}
export interface FiscalMonthRow {
  period: string; closed: boolean; closedAt: string | null; incomeCount: number; expenseCount: number; incomeBase: number;
  deductibleCost: number; vatBalance: number; finished: boolean;
}
export interface FiscalQuarterRow {
  quarter: number; label: string; dueDate: string; allClosed: boolean; vatBalance: number; irpfToPay: number; overdue: boolean;
}
export interface FiscalYearView {
  year: number; profileComplete: boolean; currentPeriod: string; months: FiscalMonthRow[]; quarters: FiscalQuarterRow[]; totals: FiscalTotals;
}
export interface FiscalWarning { code: string; message: string; count: number; }
export interface FiscalEstimate {
  available: boolean; charges: number; monthlyCharges: number; annualCharges: number; refunds: number;
  gross: number; netOfVat: number; googleFee: number; income: number;
}
export interface FiscalClosure { period: string; closedAt: string; closedBy: string; entryCount: number; snapshotJson: string; }
export interface FiscalMonthView {
  period: string; closed: boolean; closure: FiscalClosure | null; entries: FiscalEntry[]; totals: FiscalTotals;
  estimate: FiscalEstimate; hasGoogleIncome: boolean; warnings: FiscalWarning[]; canClose: boolean; cannotCloseReason: string | null;
}
export interface FiscalAuditEntry { id: string; period: string; action: string; actor: string; reason: string | null; at: string; }
export interface FiscalQuarterSummary {
  year: number; quarter: number; label: string; dueDate: string; totals: FiscalTotals;
  iva: { domesticIncomeBase: number; vatOnIncome: number; selfAssessedBase: number; selfAssessedVat: number; vatOutput: number; deductibleBase: number; vatInput: number; balance: number; notSubjectEuB2bBase: number };
  irpf: { applies: boolean; ytdIncome: number; ytdDeductibleCost: number; netYtd: number; pct: number; gross: number; withholdingsYtd: number; previousPayments: number; result: number; toPay: number };
  intra349: { key: string; counterparty: string | null; taxId: string | null; country: string | null; base: number }[];
}

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
