import { UI_COPY } from '../../core/ui-copy';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MarketingApi, fmt, marketingError } from './marketing.api';
import type { Range, SummaryReport } from './marketing.models';
import { BarRow, BarsComponent, ColumnsComponent, Series } from './marketing.charts';
import { UiTableComponent } from '../../shared/ui/ui-table.component';

/** The state of acquisition at a glance: flows against the previous period, conversion rates, current subscribers and where signups come from. */
@Component({
  selector: 'mk-overview',
  standalone: true,
  imports: [BarsComponent, ColumnsComponent, UiTableComponent],
  templateUrl: './overview.tab.html'
})
export class OverviewTabComponent {
  protected readonly labels = UI_COPY.marketing;

  private readonly api = inject(MarketingApi);
  readonly range = input.required<Range>();
  protected readonly fmt = fmt;
  protected readonly data = signal<SummaryReport | null>(null);
  protected readonly error = signal('');

  protected readonly rates = computed<BarRow[]>(() => {
    const s = this.data();
    if (!s) return [];
    return [
      { label: this.labels.startedFreeTrial, value: s.signupToTrial ?? 0, text: fmt.pct(s.signupToTrial) },
      { label: this.labels.signupToPayer, value: s.signupToPaid ?? 0, text: fmt.pct(s.signupToPaid) },
      { label: this.labels.trialToPayer, value: s.trialToPaid ?? 0, text: fmt.pct(s.trialToPaid) }
    ];
  });
  protected readonly mix = computed<BarRow[]>(() => {
    const s = this.data();
    if (!s) return [];
    const total = s.mix.reduce((n, m) => n + m.signups, 0) || 1;
    return s.mix.map(m => ({ label: this.labels.channelNames[m.name as keyof typeof this.labels.channelNames] ?? m.name, value: m.signups / total, text: `${m.signups} · ${m.paid} ${this.labels.paid}`, hint: `${m.signups} ${this.labels.signups}, ${m.paid} ${this.labels.payingCustomersShort}` }));
  });
  protected readonly weekLabels = computed(() => this.data()?.weeks.map(w => w.week) ?? []);
  protected readonly weekSeries = computed<Series[]>(() => {
    const w = this.data()?.weeks ?? [];
    return [
      { name: this.labels.signups, color: '#5b8fcb', values: w.map(x => x.signups) },
      { name: this.labels.trials, color: '#d6a83e', values: w.map(x => x.trials) },
      { name: this.labels.newPayers, color: '#4fae78', values: w.map(x => x.firstPurchases) }
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
