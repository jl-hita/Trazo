import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MarketingApi, Range, SummaryReport, fmt, marketingError } from './marketing.api';
import { BarRow, BarsComponent, ColumnsComponent, Series } from './marketing.charts';

/** The state of acquisition at a glance: flows against the previous period, conversion rates, current subscribers and where signups come from. */
@Component({
  selector: 'mk-overview',
  standalone: true,
  imports: [BarsComponent, ColumnsComponent],
  template: `
    @if (error()) { <p class="mk-err" role="alert">{{ error() }}</p> }
    @if (data(); as s) {
      <h2>Flows in the period (compared with the previous period of the same length)</h2>
      <div class="mk-cards">
        @for (k of s.kpis; track k.key) {
          <div class="mk-card"><b>{{ fmt.int(k.current) }}</b><span>{{ k.label }}</span>
            <span class="delta" [class.mk-up]="good(k.key, k.current, k.previous) === 1" [class.mk-down]="good(k.key, k.current, k.previous) === -1"
                  [class.mk-flat]="good(k.key, k.current, k.previous) === 0">{{ fmt.delta(k.current, k.previous) }} vs {{ fmt.int(k.previous) }} before</span></div>
        }
        <div class="mk-card"><b>{{ fmt.int(s.paywallViews) }}</b><span>Premium offer opened</span></div>
        <div class="mk-card"><b>{{ fmt.int(s.waitlistConfirmed) }}</b><span>Confirmed on the waitlist</span></div>
      </div>

      <div class="mk-grid2">
        <div class="mk-panel">
          <h2>Accounts created in the period: how far they got</h2>
          <p class="mk-note">{{ fmt.int(s.cohort) }} accounts. Conversion is measured on these accounts, so recent signups may still convert.</p>
          <mk-bars [rows]="rates()" [max]="1" />
        </div>
        <div class="mk-panel">
          <h2>Subscribers right now</h2>
          <div class="mk-cards" style="margin:0">
            <div class="mk-card"><b>{{ s.subscribers.paying }}</b><span>Paying</span></div>
            <div class="mk-card"><b>{{ s.subscribers.trialing }}</b><span>In a free trial</span></div>
            <div class="mk-card"><b>{{ s.subscribers.cancelingAtPeriodEnd }}</b><span>Will not renew</span></div>
            <div class="mk-card"><b>{{ s.subscribers.billing }}</b><span>Payment problem</span></div>
          </div>
          <p class="mk-muted">Test purchases and promotional time (welcome period, referral rewards) are not counted.</p>
        </div>
      </div>

      <h2>Where the signups of the period come from</h2>
      @if (s.mix.length) {
        <div class="mk-grid2">
          <div class="mk-panel"><mk-bars [rows]="mix()" /></div>
          <div class="mk-panel"><p class="mk-note">The share of each source and how many of its signups became paying customers. Open the Strategies tab to compare quality, retention and cost per customer.
            {{ s.activeCampaigns }} campaign link{{ s.activeCampaigns === 1 ? '' : 's' }} active.</p></div>
        </div>
      } @else { <p class="mk-empty">No signups in this period.</p> }

      @if (s.weeks.length) {
        <h2>Week by week</h2>
        <div class="mk-panel"><mk-columns [labels]="weekLabels()" [series]="weekSeries()" label="Signups, trials and new paying customers per week" /></div>
        <div class="mk-scroll"><table class="mk-table">
          <thead><tr><th>Week starting</th><th>Signups</th><th>Trials</th><th>New payers</th><th>Renewals</th><th>Cancellations</th><th>Refunds</th><th>Offer opened</th></tr></thead>
          <tbody>@for (w of s.weeks; track w.week) {
            <tr><td>{{ w.week }}</td><td>{{ w.signups }}</td><td>{{ w.trials }}</td><td>{{ w.firstPurchases }}</td><td>{{ w.renewals }}</td><td>{{ w.cancellations }}</td><td>{{ w.refunds }}</td><td>{{ w.paywallViews }}</td></tr>
          }</tbody></table></div>
      }
    } @else if (!error()) { <p class="mk-empty">Loading…</p> }
  `
})
export class OverviewTabComponent {
  private readonly api = inject(MarketingApi);
  readonly range = input.required<Range>();
  protected readonly fmt = fmt;
  protected readonly data = signal<SummaryReport | null>(null);
  protected readonly error = signal('');

  protected readonly rates = computed<BarRow[]>(() => {
    const s = this.data();
    if (!s) return [];
    return [
      { label: 'Started a free trial', value: s.signupToTrial ?? 0, text: fmt.pct(s.signupToTrial) },
      { label: 'Paid (signup → payer)', value: s.signupToPaid ?? 0, text: fmt.pct(s.signupToPaid) },
      { label: 'Trial → payer', value: s.trialToPaid ?? 0, text: fmt.pct(s.trialToPaid) }
    ];
  });
  protected readonly mix = computed<BarRow[]>(() => {
    const s = this.data();
    if (!s) return [];
    const total = s.mix.reduce((n, m) => n + m.signups, 0) || 1;
    return s.mix.map(m => ({ label: m.name, value: m.signups / total, text: `${m.signups} · ${m.paid} paid`, hint: `${m.signups} signups, ${m.paid} became paying customers` }));
  });
  protected readonly weekLabels = computed(() => this.data()?.weeks.map(w => w.week) ?? []);
  protected readonly weekSeries = computed<Series[]>(() => {
    const w = this.data()?.weeks ?? [];
    return [
      { name: 'Signups', color: '#5b8fcb', values: w.map(x => x.signups) },
      { name: 'Trials', color: '#d6a83e', values: w.map(x => x.trials) },
      { name: 'New payers', color: '#4fae78', values: w.map(x => x.firstPurchases) }
    ];
  });

  constructor() {
    effect(() => { const r = this.range(); void this.load(r); });
  }

  /** 1 improving, -1 worsening, 0 neutral (cancellations and refunds are better when they go down). */
  protected good(key: string, current: number, previous: number): number {
    if (current === previous) return 0;
    const bad = key === 'canceled' || key === 'refunds';
    return (current > previous) !== bad ? 1 : -1;
  }

  private async load(range: Range): Promise<void> {
    this.error.set('');
    try { this.data.set(await this.api.summary(range)); } catch (e) { this.error.set(marketingError(e)); }
  }
}
