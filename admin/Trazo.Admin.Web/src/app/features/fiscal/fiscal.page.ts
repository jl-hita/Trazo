import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

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
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .fs h2 { margin: 24px 0 10px; font-size: 14px; color: #a9c2df; letter-spacing: .08em; text-transform: uppercase; }
    .fs .toolbar { display: flex; flex-wrap: wrap; gap: 10px; align-items: end; margin-bottom: 14px; }
    .fs label { display: grid; gap: 4px; color: #8291a6; font-size: 11px; }
    .fs input, .fs select, .fs textarea { padding: 8px 10px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; }
    .fs button, .fs .btn { padding: 8px 14px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .fs button.primary { background: #2c5fa8; border-color: #4b82cf; font-weight: 600; padding: 10px 18px; font-size: 13px; }
    .fs button.danger { border-color: #7a3b3b; color: #f0a3a3; }
    .fs button:disabled { opacity: .45; cursor: not-allowed; }
    .fs .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 12px; margin-bottom: 8px; }
    .fs .card { padding: 14px; border: 1px solid #293444; border-radius: 8px; background: #131b27; }
    .fs .card b { display: block; font-size: 20px; color: #f2f5fa; }
    .fs .card span { color: #8291a6; font-size: 11px; }
    .fs table { width: 100%; border-collapse: collapse; border: 1px solid #293444; background: #131b27; font-size: 13px; }
    .fs th { padding: 9px 12px; text-align: left; color: #8291a6; font-size: 10px; letter-spacing: .12em; text-transform: uppercase; border-bottom: 1px solid #293444; }
    .fs td { padding: 9px 12px; border-bottom: 1px solid #1d2735; color: #d4deea; }
    .fs td.num, .fs th.num { text-align: right; font-variant-numeric: tabular-nums; }
    .fs .muted { color: #8291a6; font-size: 11px; }
    .fs .ok { color: #7fd3a0; } .fs .warn { color: #e6d39b; } .fs .err { color: #f0a3a3; }
    .fs .pill { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 11px; border: 1px solid #34465b; }
    .fs .pill.closed { border-color: #3d7a55; color: #7fd3a0; } .fs .pill.overdue { border-color: #7a3b3b; color: #f0a3a3; }
    .fs .box { padding: 14px 16px; border: 1px solid #293444; border-radius: 8px; background: #131b27; margin-bottom: 12px; }
    .fs .box.alert { border-color: #7a6a3b; background: #1f1c12; }
    .fs .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 12px; margin-bottom: 12px; }
    .fs .row-actions { display: flex; gap: 6px; }
    .fs .notice { color: #8291a6; font-size: 12px; max-width: 900px; line-height: 1.5; }
    .fs ul.warnings { margin: 6px 0 0; padding-left: 18px; color: #e6d39b; font-size: 13px; }
    .fs details > summary { cursor: pointer; color: #a9c2df; font-size: 13px; margin-bottom: 10px; }
  `],
  template: `
    <div class="page-heading">
      <div><p class="eyebrow">FINANCE</p><h1>Tax management</h1>
        <p class="subtitle">Books of income and expenses, VAT and income-tax figures per quarter, and a one-click month close that prepares everything the accountant needs.</p></div>
    </div>
    <div class="fs">
      @if (message()) { <p class="ok" role="status">{{ message() }}</p> }
      @if (error()) { <p class="err" role="alert">{{ error() }}</p> }

      @if (year(); as y) {
        @if (!y.profileComplete) {
          <div class="box alert">Fill in your <b>legal name and tax id (NIF)</b> in the fiscal profile below. Months cannot be closed without them.</div>
        }

        <details [open]="!y.profileComplete">
          <summary>Fiscal profile and rules</summary>
          <div class="box">
            <div class="grid">
              <label>Legal name<input [(ngModel)]="profile.legalName" name="legalName"></label>
              <label>Tax id (NIF)<input [(ngModel)]="profile.taxId" name="taxId"></label>
              <label>Address<input [(ngModel)]="profile.address" name="address"></label>
              <label>Accountant's email (optional)<input [(ngModel)]="profile.gestorEmail" name="gestorEmail"></label>
              <label>Default VAT %<input type="number" step="0.01" [(ngModel)]="profile.defaultVatPct" name="defaultVatPct"></label>
              <label>Income-tax instalment % (modelo 130)<input type="number" step="0.01" [(ngModel)]="profile.irpfAdvancePct" name="irpfAdvancePct"></label>
              <label><span><input type="checkbox" [(ngModel)]="profile.filesModelo130" name="filesModelo130"> I file modelo 130</span></label>
            </div>
            <p class="muted">Used to estimate Google Play income from verified purchases (an estimate: what counts is Google's monthly earnings report).</p>
            <div class="grid">
              <label>Monthly price (€, with VAT)<input type="number" step="0.01" [(ngModel)]="profile.playMonthlyPrice" name="playMonthlyPrice"></label>
              <label>Annual price (€, with VAT)<input type="number" step="0.01" [(ngModel)]="profile.playAnnualPrice" name="playAnnualPrice"></label>
              <label>VAT included in the consumer price %<input type="number" step="0.01" [(ngModel)]="profile.consumerVatPct" name="consumerVatPct"></label>
              <label>Google service fee %<input type="number" step="0.01" [(ngModel)]="profile.googleFeePct" name="googleFeePct"></label>
            </div>
            <button type="button" class="primary" (click)="saveProfile()">Save profile</button>
          </div>
        </details>

        <div class="toolbar">
          <label>Year <select [ngModel]="yearNumber()" (ngModelChange)="changeYear($event)" name="year">
            @for (v of years; track v) { <option [ngValue]="v">{{ v }}</option> }
          </select></label>
          <span class="muted">Current month: {{ y.currentPeriod }}</span>
        </div>
        <div class="cards">
          <div class="card"><b>{{ eur(y.totals.incomeBase) }}</b><span>Income (base)</span></div>
          <div class="card"><b>{{ eur(y.totals.deductibleCost) }}</b><span>Deductible costs</span></div>
          <div class="card"><b>{{ eur(y.totals.netResult) }}</b><span>Net result</span></div>
          <div class="card"><b>{{ eur(y.totals.vatBalance) }}</b><span>VAT balance (output − input)</span></div>
        </div>

        <h2>Quarters — returns 303, 130 and 349</h2>
        <table>
          <thead><tr><th>Quarter</th><th>Due</th><th class="num">VAT balance (303)</th><th class="num">Income tax to pay (130)</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (q of y.quarters; track q.quarter) {
              <tr>
                <td>{{ q.label }}</td><td>{{ q.dueDate }}</td>
                <td class="num">{{ eur(q.vatBalance) }}</td><td class="num">{{ eur(q.irpfToPay) }}</td>
                <td>@if (q.allClosed) { <span class="pill closed">All months closed</span> }
                    @else if (q.overdue) { <span class="pill overdue">Overdue</span> }
                    @else { <span class="pill">Open</span> }</td>
                <td class="row-actions">
                  <button type="button" [disabled]="!q.allClosed" (click)="downloadQuarter(q.quarter)">Download quarter package</button>
                </td>
              </tr>
            }
          </tbody>
        </table>

        <h2>Months</h2>
        <table>
          <thead><tr><th>Month</th><th class="num">Income</th><th class="num">Costs</th><th class="num">VAT balance</th><th class="num">Entries</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (m of y.months; track m.period) {
              <tr [style.background]="selected() === m.period ? '#18243a' : ''">
                <td>{{ m.period }}</td>
                <td class="num">{{ eur(m.incomeBase) }}</td><td class="num">{{ eur(m.deductibleCost) }}</td><td class="num">{{ eur(m.vatBalance) }}</td>
                <td class="num">{{ m.incomeCount + m.expenseCount }}</td>
                <td>@if (m.closed) { <span class="pill closed">Closed</span> } @else if (m.finished) { <span class="pill warn">To close</span> } @else { <span class="pill">In progress</span> }</td>
                <td><button type="button" (click)="openMonth(m.period)">Open</button></td>
              </tr>
            }
          </tbody>
        </table>
      }

      @if (month(); as m) {
        <h2>{{ m.period }} — {{ m.closed ? 'closed' : 'open' }}</h2>
        @if (m.closed && m.closure) {
          <p class="muted">Closed on {{ m.closure.closedAt.slice(0, 10) }} by {{ m.closure.closedBy }} ({{ m.closure.entryCount }} entries). Reopen it to make changes.</p>
        }
        <div class="cards">
          <div class="card"><b>{{ eur(m.totals.incomeBase) }}</b><span>Income (base)</span></div>
          <div class="card"><b>{{ eur(m.totals.deductibleCost) }}</b><span>Deductible costs</span></div>
          <div class="card"><b>{{ eur(m.totals.vatOutput) }}</b><span>VAT charged / self-assessed</span></div>
          <div class="card"><b>{{ eur(m.totals.vatInput) }}</b><span>VAT deductible</span></div>
          <div class="card"><b>{{ eur(m.totals.vatBalance) }}</b><span>VAT balance</span></div>
          <div class="card"><b>{{ eur(m.totals.netResult) }}</b><span>Net result</span></div>
        </div>

        @if (m.estimate.available) {
          <div class="box">
            <b>Google Play estimate</b> — {{ m.estimate.charges }} charge(s) ({{ m.estimate.monthlyCharges }} monthly, {{ m.estimate.annualCharges }} annual),
            {{ m.estimate.refunds }} refund(s). Gross {{ eur(m.estimate.gross) }} → without VAT {{ eur(m.estimate.netOfVat) }} → minus Google fee
            {{ eur(m.estimate.googleFee) }} = <b>{{ eur(m.estimate.income) }}</b>.
            <p class="muted">An estimate from verified purchases: replace it with the amount in Google Play's earnings report when you have it.</p>
            <button type="button" [disabled]="m.closed || m.hasGoogleIncome" (click)="bookEstimate(m.period)">
              {{ m.hasGoogleIncome ? 'Google Play income already booked' : 'Book this income' }}</button>
          </div>
        }

        @if (m.warnings.length && !m.closed) {
          <div class="box alert"><b>Review before closing</b>
            <ul class="warnings">@for (w of m.warnings; track w.code) { <li>{{ w.message }}@if (w.count) { ({{ w.count }}) }</li> }</ul>
          </div>
        }

        <table>
          <thead><tr><th>Date</th><th>Kind</th><th>Concept</th><th>Counterparty</th><th class="num">Base</th><th>VAT</th><th class="num">Total</th><th></th></tr></thead>
          <tbody>
            @for (e of m.entries; track e.id) {
              <tr>
                <td>{{ e.date }}</td><td>{{ e.kind }}<div class="muted">{{ e.category }}</div></td>
                <td>{{ e.concept }}@if (e.invoiceNumber) { <div class="muted">{{ e.invoiceNumber }}</div> }@if (e.source === 'google_estimate') { <div class="warn muted">estimate</div> }</td>
                <td>{{ e.counterparty }}<div class="muted">{{ e.counterpartyTaxId }}</div></td>
                <td class="num">{{ eur(e.base) }}</td>
                <td>{{ vatLabel(e.vatTreatment) }}@if (e.vatAmount) { <div class="muted">{{ e.vatRatePct }}% = {{ eur(e.vatAmount) }}</div> }</td>
                <td class="num">{{ eur(e.total) }}</td>
                <td class="row-actions">
                  <button type="button" [disabled]="m.closed" (click)="edit(e)">Edit</button>
                  <button type="button" class="danger" [disabled]="m.closed" (click)="remove(e)">Delete</button>
                </td>
              </tr>
            } @empty { <tr><td colspan="8" class="muted">No entries yet.</td></tr> }
          </tbody>
        </table>

        @if (!m.closed) {
          <h2>{{ form.id ? 'Edit entry' : 'Add entry' }}</h2>
          <div class="box">
            <div class="grid">
              <label>Kind<select [(ngModel)]="form.kind" (ngModelChange)="kindChanged()" name="kind"><option value="expense">Expense</option><option value="income">Income</option></select></label>
              <label>Date<input type="date" [(ngModel)]="form.date" name="date"></label>
              <label>Concept<input [(ngModel)]="form.concept" name="concept" maxlength="200"></label>
              <label>Category<select [(ngModel)]="form.category" name="category">
                @for (c of categories(); track c) { <option [value]="c">{{ c }}</option> }</select></label>
              <label>Base amount (€, negative for a credit note)<input type="number" step="0.01" [(ngModel)]="form.base" name="base"></label>
              <label>VAT treatment<select [(ngModel)]="form.vatTreatment" name="vatTreatment">
                <option value="domestic">Spanish VAT</option>
                <option value="eu_reverse_charge">EU business (reverse charge)</option>
                <option value="no_vat">No VAT / exempt</option></select></label>
              @if (form.vatTreatment === 'domestic' || (form.kind === 'expense' && form.vatTreatment === 'eu_reverse_charge')) {
                <label>VAT %<input type="number" step="0.01" [(ngModel)]="form.vatRatePct" name="vatRatePct"></label>
              }
              @if (form.kind === 'income') {
                <label>IRPF withholding %<input type="number" step="0.01" [(ngModel)]="form.withholdingPct" name="withholdingPct"></label>
              } @else {
                <label>Deductible %<input type="number" step="1" [(ngModel)]="form.deductiblePct" name="deductiblePct"></label>
              }
              <label>{{ form.kind === 'income' ? 'Customer' : 'Supplier' }}<input [(ngModel)]="form.counterparty" name="counterparty"></label>
              <label>Tax id / EU VAT number<input [(ngModel)]="form.counterpartyTaxId" name="counterpartyTaxId"></label>
              <label>Country (2 letters)<input [(ngModel)]="form.counterpartyCountry" name="counterpartyCountry" maxlength="2"></label>
              <label>Invoice number<input [(ngModel)]="form.invoiceNumber" name="invoiceNumber"></label>
              <label>Notes<input [(ngModel)]="form.notes" name="notes" maxlength="500"></label>
            </div>
            <button type="button" class="primary" (click)="saveEntry()">{{ form.id ? 'Save changes' : 'Add entry' }}</button>
            @if (form.id) { <button type="button" (click)="resetForm()">Cancel</button> }
            <p class="muted">Tip: services billed by an EU company (hosting, Google Cloud…) carry no Spanish VAT — use "EU business (reverse charge)": TRAZO self-assesses the VAT and deducts it. Social-security fee and bank charges: "No VAT".</p>
          </div>
        }

        <div class="box">
          @if (!m.closed) {
            <button type="button" class="primary" [disabled]="!m.canClose || busy()" (click)="close(m)">Close month and prepare the accountant's package</button>
            @if (m.cannotCloseReason === 'month_not_finished') { <span class="muted"> A month can only be closed once it has finished.</span> }
            @if (m.cannotCloseReason === 'profile_incomplete') { <span class="warn"> Complete the fiscal profile first.</span> }
          } @else {
            <button type="button" class="primary" (click)="downloadMonth(m.period)">Download the accountant's package (.zip)</button>
            <button type="button" class="danger" (click)="reopen(m.period)">Reopen…</button>
          }
        </div>
      }

      @if (audit().length) {
        <h2>Closing history</h2>
        <table>
          <thead><tr><th>When</th><th>Month</th><th>Action</th><th>By</th><th>Reason</th></tr></thead>
          <tbody>@for (a of audit(); track a.id) { <tr><td>{{ a.at.slice(0, 16).replace('T', ' ') }}</td><td>{{ a.period }}</td><td>{{ a.action }}</td><td>{{ a.actor }}</td><td>{{ a.reason }}</td></tr> }</tbody>
        </table>
      }

      <p class="notice">Orientative figures to prepare the work of your accountant. They do not file anything and do not replace professional advice:
        have your gestor review the VAT treatments, the 303/130/349 figures and the Google Play income before filing. Google Play income is booked against
        Google Ireland Limited as a service to an EU business (no Spanish VAT, reverse charge, modelo 349).</p>
    </div>
  `
})
export class FiscalPageComponent {
  private readonly api = inject(AdminApi);
  private readonly money = new Intl.NumberFormat('es-ES', { style: 'currency', currency: 'EUR' });
  protected readonly years = (() => { const y = new Date().getFullYear(); return [y - 2, y - 1, y, y + 1]; })();
  protected readonly yearNumber = signal(new Date().getFullYear());
  protected readonly year = signal<any>(null);
  protected readonly month = signal<any>(null);
  protected readonly selected = signal('');
  protected readonly audit = signal<any[]>([]);
  protected readonly message = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected profile: any = { legalName: '', taxId: '', address: '', gestorEmail: '', defaultVatPct: 21, irpfAdvancePct: 20, filesModelo130: true, playMonthlyPrice: 6.99, playAnnualPrice: 49.99, consumerVatPct: 21, googleFeePct: 15 };
  protected form = EMPTY_ENTRY();
  private options = { incomeCategories: ['google_play', 'other_income'], expenseCategories: ['other'], vatTreatments: [] as string[] };
  protected readonly categories = computed(() => {
    this.formKind();
    return this.formKind() === 'income' ? this.options.incomeCategories : this.options.expenseCategories;
  });
  private readonly formKind = signal('expense');

  constructor() { void this.init(); }

  protected eur(value: number | null | undefined): string { return this.money.format(value ?? 0); }

  protected vatLabel(t: string): string {
    return t === 'domestic' ? 'Spanish VAT' : t === 'eu_reverse_charge' ? 'EU reverse charge' : 'No VAT';
  }

  private fail(e: unknown): void {
    const body = (e as { error?: { message?: string; errors?: unknown } })?.error;
    this.error.set(body?.errors ? errorMessage(e) : (body?.message ?? errorMessage(e)));
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
      this.ok('Fiscal profile saved.');
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

  protected edit(e: any): void {
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
      this.ok(id ? 'Entry updated.' : 'Entry added.');
      this.resetForm();
      await this.refresh();
    } catch (e) { this.fail(e); }
  }

  protected async remove(e: any): Promise<void> {
    if (!window.confirm(`Delete "${e.concept}"?`)) return;
    try { await this.api.deleteFiscalEntry(e.id); this.ok('Entry deleted.'); await this.refresh(); } catch (err) { this.fail(err); }
  }

  protected async bookEstimate(period: string): Promise<void> {
    try { await this.api.bookGoogleEstimate(period); this.ok('Google Play income booked from the estimate. Edit it when you have the real figure.'); await this.refresh(); } catch (e) { this.fail(e); }
  }

  protected async close(m: any): Promise<void> {
    if (m.warnings.length > 0) {
      const list = m.warnings.map((w: any) => `• ${w.message}`).join('\n');
      if (!window.confirm(`This month has warnings:\n\n${list}\n\nClose it anyway?`)) return;
    } else if (!window.confirm(`Close ${m.period}? Its entries will be frozen until you reopen it.`)) return;
    this.busy.set(true);
    try {
      await this.api.closeFiscalMonth(m.period, true);
      await this.refresh();
      this.ok(`${m.period} closed. Downloading the package for your accountant.`);
      await this.downloadMonth(m.period);
    } catch (e) { this.fail(e); } finally { this.busy.set(false); }
  }

  protected async reopen(period: string): Promise<void> {
    const reason = window.prompt('Why are you reopening this month? (10–300 characters, it is recorded)');
    if (!reason) return;
    try { await this.api.reopenFiscalMonth(period, reason); this.ok(`${period} reopened.`); await this.refresh(); } catch (e) { this.fail(e); }
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
