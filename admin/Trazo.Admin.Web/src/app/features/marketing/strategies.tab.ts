import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MarketingApi, fmt, marketingError } from './marketing.api';
import type { Range, SegmentRow, StrategiesReport } from './marketing.models';
import { BarRow, BarsComponent } from './marketing.charts';

type Metric = 'signupToPaid' | 'signupToTrial' | 'signupToActivated' | 'd30' | 'paidRenewed' | 'costPerPaid' | 'returnOnSpend';

const METRICS: { key: Metric; label: string; lowerIsBetter?: boolean; money?: boolean }[] = [
  { key: 'signupToPaid', label: 'Signup → paying customer' },
  { key: 'signupToTrial', label: 'Signup → free trial' },
  { key: 'signupToActivated', label: 'Signup → first workout' },
  { key: 'd30', label: 'Still training 30 days later' },
  { key: 'paidRenewed', label: 'Payers who renewed' },
  { key: 'costPerPaid', label: 'Cost per paying customer', lowerIsBetter: true, money: true },
  { key: 'returnOnSpend', label: 'Estimated return on spend' }
];

/**
 * The heart of the panel: every acquisition strategy side by side, with the whole path from link click to paid renewal, the cost of each
 * step and a confidence range so that a lucky small sample is not mistaken for a winning strategy.
 */
@Component({
  selector: 'mk-strategies',
  standalone: true,
  imports: [FormsModule, NgTemplateOutlet, BarsComponent, UiButtonComponent, UiTableComponent],
  templateUrl: './strategies.tab.html'
})
export class StrategiesTabComponent {
  private readonly api = inject(MarketingApi);
  readonly range = input.required<Range>();
  readonly openCampaign = output<string>();
  protected readonly fmt = fmt;
  protected readonly metrics = METRICS;
  protected readonly data = signal<StrategiesReport | null>(null);
  protected readonly error = signal('');
  protected readonly metric = signal<Metric>('signupToPaid');
  protected readonly onlyReliable = signal(false);

  protected readonly channels = computed(() => this.data()?.channels ?? []);
  protected readonly campaigns = computed(() => this.data()?.campaigns ?? []);

  protected readonly ranking = computed<BarRow[]>(() => {
    const d = this.data();
    if (!d) return [];
    const m = METRICS.find(x => x.key === this.metric())!;
    const rows = [...d.channels.filter(c => c.key === 'organic' || c.key === 'referral'), ...d.campaigns]
      .filter(r => (!this.onlyReliable() || r.reliable) && r.signups > 0);
    const valued = rows.map(r => ({ r, v: this.value(r, m.key) })).filter((x): x is { r: SegmentRow; v: number } => x.v !== null);
    valued.sort((a, b) => (m.lowerIsBetter ? a.v - b.v : b.v - a.v));
    return valued.map(({ r, v }) => ({
      label: r.name, value: Math.max(0, v), dim: !r.reliable,
      text: m.money ? fmt.eur(v) : m.key === 'returnOnSpend' ? fmt.pct(v, 0) : fmt.pct(v),
      low: m.key === 'signupToPaid' ? r.signupToPaidLow : null, high: m.key === 'signupToPaid' ? r.signupToPaidHigh : null,
      hint: `${r.signups} signups${r.reliable ? '' : ' (small sample)'}`
    }));
  });

  constructor() {
    effect(() => { const r = this.range(); void this.load(r); });
  }

  private value(r: SegmentRow, key: Metric): number | null {
    return r[key] as number | null;
  }

  private async load(range: Range): Promise<void> {
    this.error.set('');
    try { this.data.set(await this.api.strategies(range)); } catch (e) { this.error.set(marketingError(e)); }
  }

  protected async download(): Promise<void> {
    try {
      const blob = await this.api.strategiesCsv(this.range());
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url; a.download = `trazo-strategies-${this.range().from}_${this.range().to}.csv`; a.click();
      URL.revokeObjectURL(url);
    } catch (e) { this.error.set(marketingError(e)); }
  }
}
