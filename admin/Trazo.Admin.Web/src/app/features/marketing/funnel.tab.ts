import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MarketingApi, fmt, marketingError } from './marketing.api';
import type { MarketingFunnel, Range, RetentionReport } from './marketing.models';
import { BarRow, BarsComponent } from './marketing.charts';

const STAGE_LABELS: Record<string, string> = {
  signup: 'Created an account', email_verified: 'Verified the e-mail', onboarding_completed: 'Finished onboarding', first_plan: 'Got a training plan',
  first_workout: 'Logged a first workout', trial_started: 'Started a free trial', first_purchase: 'Paid (first real payment)', renewed: 'Renewed at least once',
  canceled: 'Cancelled', refunded: 'Was refunded'
};

/** The path of one strategy (or of everybody) from signup to renewal, and how many keep training week after week. */
@Component({
  selector: 'mk-funnel',
  standalone: true,
  imports: [FormsModule, BarsComponent],
  templateUrl: './funnel.tab.html'
})
export class FunnelTabComponent {
  private readonly api = inject(MarketingApi);
  readonly range = input.required<Range>();
  protected readonly fmt = fmt;
  protected readonly segment = signal('all');
  protected readonly funnel = signal<MarketingFunnel | null>(null);
  protected readonly retention = signal<RetentionReport | null>(null);
  protected readonly error = signal('');
  protected readonly channelOptions = signal<string[]>([]);
  protected readonly campaignOptions = signal<{ slug: string; name: string }[]>([]);
  protected readonly weekNumbers = computed(() => Array.from({ length: (this.retention()?.weeks ?? 8) + 1 }, (_, i) => i));

  protected readonly stageRows = computed<BarRow[]>(() =>
    (this.funnel()?.stages ?? []).map(s => ({ label: this.label(s.name), value: s.ofCohort ?? 0, text: `${s.users} · ${fmt.pct(s.ofCohort, 0)}`, dim: s.name === 'canceled' || s.name === 'refunded' })));

  constructor() {
    void this.loadOptions();
    effect(() => { const r = this.range(); const s = this.segment(); void this.load(r, s); });
  }

  protected label(name: string): string { return STAGE_LABELS[name] ?? name; }

  /** Dark blue (few) to bright green (many). Text stays dark, so every cell is readable. */
  protected color(v: number | null): string {
    if (v === null) return 'transparent';
    const hue = 205 - 85 * v;
    return `hsl(${hue} 55% ${28 + 40 * v}%)`;
  }

  private async loadOptions(): Promise<void> {
    try {
      const [options, campaigns] = await Promise.all([this.api.options(), this.api.campaigns()]);
      this.channelOptions.set(options.channels);
      this.campaignOptions.set(campaigns.items.map(i => ({ slug: i.campaign.slug, name: i.campaign.name })));
    } catch { /* the selector still works with the fixed options */ }
  }

  private async load(range: Range, segment: string): Promise<void> {
    this.error.set('');
    try {
      const [f, r] = await Promise.all([this.api.funnel(range, segment), this.api.retention(range, segment)]);
      this.funnel.set(f);
      this.retention.set(r);
    } catch (e) { this.error.set(marketingError(e)); }
  }
}
