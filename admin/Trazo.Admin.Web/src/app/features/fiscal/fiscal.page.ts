import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiTableComponent } from '../../shared/ui/ui-table.component';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import { FiscalApi } from './fiscal.api';
import type { FiscalAuditEntry, FiscalEntry, FiscalMonthView, FiscalProfile, FiscalWarning, FiscalYearView } from './fiscal.models';
import { errorMessage } from '../../core/api-error';
import { UI_COPY } from '../../core/ui-copy';
import { formatUiDate } from '../../core/ui-format';

const EMPTY_ENTRY = () => ({
  id: null as string | null, kind: 'expense', date: '', concept: '', counterparty: '', counterpartyTaxId: '', counterpartyCountry: 'ES',
  invoiceNumber: '', category: 'hosting', base: null as number | null, vatTreatment: 'domestic', vatRatePct: 21 as number | null,
  withholdingPct: 0 as number | null, deductiblePct: 100 as number | null, notes: ''
});

/**
 * Tax management for the self-employed owner (superadmin only): income and expense books, VAT and income-tax figures per
 * quarter, and "close month", which freezes the month and produces the package for the accountant. Figures are orientative.
 */
@Component({
  selector: 'trazo-fiscal-page',
  standalone: true,
  imports: [FormsModule, UiButtonComponent, UiTableComponent, UiFormFieldComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './fiscal.page.scss',
  templateUrl: './fiscal.page.html'
})
export class FiscalPageComponent {
  private readonly api = inject(FiscalApi);
  private readonly money = new Intl.NumberFormat('es-ES', { style: 'currency', currency: 'EUR' });
  protected readonly copy = UI_COPY.fiscal;
  protected readonly years = (() => { const y = new Date().getFullYear(); return [y - 2, y - 1, y, y + 1]; })();
  protected readonly yearNumber = signal(new Date().getFullYear());
  protected readonly year = signal<FiscalYearView | null>(null);
  protected readonly month = signal<FiscalMonthView | null>(null);
  protected readonly selected = signal('');
  protected readonly audit = signal<FiscalAuditEntry[]>([]);
  protected readonly message = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected profile: FiscalProfile = { legalName: '', taxId: '', address: '', gestorEmail: '', defaultVatPct: 21, irpfAdvancePct: 20, filesModelo130: true, playMonthlyPrice: 6.99, playAnnualPrice: 49.99, consumerVatPct: 21, googleFeePct: 15 };
  protected form = EMPTY_ENTRY();
  private options = { incomeCategories: ['google_play', 'other_income'], expenseCategories: ['other'], vatTreatments: [] as string[] };
  protected readonly categories = computed(() => {
    this.formKind();
    return this.formKind() === 'income' ? this.options.incomeCategories : this.options.expenseCategories;
  });
  private readonly formKind = signal('expense');

  constructor() { void this.init(); }

  protected eur(value: number | null | undefined): string { return this.money.format(value ?? 0); }

  protected date(value: string): string { return formatUiDate(value); }

  protected auditAction(action: string): string { return this.copy.auditActions[action as keyof typeof this.copy.auditActions] ?? action; }

  protected kindLabel(kind: string): string { return kind === 'income' ? this.copy.incomeKind : this.copy.expense; }

  protected vatLabel(t: string): string {
    return this.copy.vatLabels[t as keyof typeof this.copy.vatLabels] ?? t;
  }

  private fail(e: unknown): void {
    this.error.set(errorMessage(e));
    this.message.set('');
  }

  private ok(text: string): void { this.message.set(text); this.error.set(''); }

  private async init(): Promise<void> {
    try {
      const [profile, options] = await Promise.all([this.api.getFiscalProfile(), this.api.getFiscalOptions()]);
      this.profile = { ...profile, gestorEmail: profile.gestorEmail ?? '' };
      this.options = options;
      this.formKind.set(this.form.kind);
      await this.loadYear();
    } catch (e) { this.fail(e); }
  }

  protected async changeYear(year: number): Promise<void> { this.yearNumber.set(year); this.month.set(null); this.selected.set(''); await this.loadYear(); }

  private async loadYear(): Promise<void> {
    try {
      this.year.set(await this.api.getFiscalYear(this.yearNumber()));
      this.audit.set((await this.api.getFiscalAudit()).items);
    } catch (e) { this.fail(e); }
  }

  protected async openMonth(period: string): Promise<void> {
    try { this.selected.set(period); this.month.set(await this.api.getFiscalMonth(period)); this.resetForm(period); } catch (e) { this.fail(e); }
  }

  private async refresh(): Promise<void> {
    const period = this.selected();
    await this.loadYear();
    if (period) this.month.set(await this.api.getFiscalMonth(period));
  }

  protected async saveProfile(): Promise<void> {
    try {
      this.profile = { ...(await this.api.saveFiscalProfile({ ...this.profile, gestorEmail: this.profile.gestorEmail || null })) };
      this.profile.gestorEmail ??= '';
      this.ok(this.copy.messages.profileSaved);
      await this.refresh();
    } catch (e) { this.fail(e); }
  }

  protected kindChanged(): void {
    this.formKind.set(this.form.kind);
    this.form.category = this.categories()[0];
    if (this.form.kind === 'income') { this.form.vatTreatment = 'eu_reverse_charge'; this.form.category = 'google_play'; }
  }

  protected resetForm(period?: string): void {
    this.form = EMPTY_ENTRY();
    this.form.vatRatePct = this.profile.defaultVatPct ?? 21;
    const p = period ?? this.selected();
    if (p) this.form.date = `${p}-01`;
    this.formKind.set(this.form.kind);
    this.form.category = this.categories()[0];
  }

  protected edit(e: FiscalEntry): void {
    this.form = {
      id: e.id, kind: e.kind, date: e.date, concept: e.concept, counterparty: e.counterparty ?? '', counterpartyTaxId: e.counterpartyTaxId ?? '',
      counterpartyCountry: e.counterpartyCountry ?? '', invoiceNumber: e.invoiceNumber ?? '', category: e.category, base: e.base,
      vatTreatment: e.vatTreatment, vatRatePct: e.vatRatePct, withholdingPct: e.withholdingPct, deductiblePct: e.deductiblePct, notes: e.notes ?? ''
    };
    this.formKind.set(this.form.kind);
  }

  protected async saveEntry(): Promise<void> {
    try {
      const { id, ...body } = this.form;
      await this.api.saveFiscalEntry(id, body);
      this.ok(id ? this.copy.messages.entryUpdated : this.copy.messages.entryAdded);
      this.resetForm();
      await this.refresh();
    } catch (e) { this.fail(e); }
  }

  protected async remove(e: FiscalEntry): Promise<void> {
    if (!window.confirm(this.copy.prompts.deleteEntry.replace('{concept}', e.concept))) return;
    try { await this.api.deleteFiscalEntry(e.id); this.ok(this.copy.messages.entryDeleted); await this.refresh(); } catch (err) { this.fail(err); }
  }

  protected async bookEstimate(period: string): Promise<void> {
    try { await this.api.bookGoogleEstimate(period); this.ok(this.copy.messages.estimateBooked); await this.refresh(); } catch (e) { this.fail(e); }
  }

  protected async close(m: FiscalMonthView): Promise<void> {
    if (m.warnings.length > 0) {
      const list = m.warnings.map((w: FiscalWarning) => `• ${w.message}`).join('\n');
      if (!window.confirm(`${this.copy.prompts.closeWarnings}\n\n${list}\n\n${this.copy.prompts.closeAnyway}`)) return;
    } else if (!window.confirm(this.copy.prompts.closeMonth.replace('{period}', m.period))) return;
    this.busy.set(true);
    try {
      await this.api.closeFiscalMonth(m.period, true);
      await this.refresh();
      this.ok(`${m.period} ${this.copy.messages.monthClosed}`);
      await this.downloadMonth(m.period);
    } catch (e) { this.fail(e); } finally { this.busy.set(false); }
  }

  protected async reopen(period: string): Promise<void> {
    const reason = window.prompt(this.copy.prompts.reopenMonth);
    if (!reason) return;
    try { await this.api.reopenFiscalMonth(period, reason); this.ok(`${period} ${this.copy.messages.monthReopened}`); await this.refresh(); } catch (e) { this.fail(e); }
  }

  protected async downloadMonth(period: string): Promise<void> {
    try { this.save(await this.api.downloadFiscalMonth(period), `trazo_gestor_${period}.zip`); } catch (e) { this.fail(e); }
  }

  protected async downloadQuarter(quarter: number): Promise<void> {
    try { this.save(await this.api.downloadFiscalQuarter(this.yearNumber(), quarter), `trazo_gestor_${this.yearNumber()}T${quarter}.zip`); } catch (e) { this.fail(e); }
  }

  private save(blob: Blob, name: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = name; a.click();
    URL.revokeObjectURL(url);
  }
}
