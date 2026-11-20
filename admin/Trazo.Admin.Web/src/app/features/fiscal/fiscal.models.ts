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
