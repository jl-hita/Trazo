import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MarketingApi, Range, SegmentRow, StrategiesReport, fmt, marketingError } from './marketing.api';
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
  imports: [FormsModule, NgTemplateOutlet, BarsComponent],
  template: `
    @if (error()) { <p class="mk-err" role="alert">{{ error() }}</p> }
    @if (data(); as d) {
      @if (d.insights.length) {
        <h2>What the data says</h2>
        <div class="mk-insights">
          @for (i of d.insights; track i.title) { <div class="mk-insight" [class]="'mk-insight ' + i.kind"><b>{{ i.title }}</b><span>{{ i.detail }}</span></div> }
        </div>
      }

      <h2>Ranking</h2>
      <div class="mk-toolbar">
        <label>Rank strategies by
          <select [ngModel]="metric()" (ngModelChange)="metric.set($event)" name="metric">
            @for (m of metrics; track m.key) { <option [value]="m.key">{{ m.label }}</option> }
          </select></label>
        <label><span><input type="checkbox" [ngModel]="onlyReliable()" (ngModelChange)="onlyReliable.set($event)" name="reliable"> Only strategies with {{ d.minReliable }}+ signups</span></label>
        <button type="button" class="mk-btn" (click)="download()">Download CSV</button>
      </div>
      @if (ranking().length) {
        <div class="mk-panel"><mk-bars [rows]="ranking()" /></div>
        @if (metric() === 'signupToPaid') { <p class="mk-muted">The thin white line is the 95% confidence range: the wider it is, the less the figure can be trusted.</p> }
      } @else { <p class="mk-empty">Nothing to rank yet for this metric in the period.</p> }

      <ng-template #tbl let-rows><div class="mk-scroll"><table class="mk-table">
    <thead><tr><th>Strategy</th><th>Clicks</th><th>Installs</th><th>Signups</th><th>Trial</th><th>Paid</th><th>Signup → paid</th><th>Trial → paid</th>
      <th>Renewed</th><th>Still training (30 d)</th><th>Spend</th><th>€ / signup</th><th>€ / payer</th><th>Est. revenue</th><th>Return</th></tr></thead>
    <tbody>
      @for (r of rows; track r.key) {
        <tr [class.clickable]="r.kind === 'campaign'" (click)="r.kind === 'campaign' && openCampaign.emit(r.key)">
          <td><span class="name">{{ r.name }}@if (!r.reliable && r.signups > 0) { <span class="mk-flag" title="Fewer than the minimum signups: do not decide on this yet">small sample</span> }</span>
            @if (r.status) { <span class="sub">{{ r.channel }} · {{ r.status }}</span> }</td>
          <td>{{ r.kind === 'campaign' || r.clicks ? fmt.int(r.clicks) : '—' }}</td>
          <td>{{ r.kind === 'campaign' || r.installs ? fmt.int(r.installs) : '—' }}</td>
          <td>{{ r.signups }}</td>
          <td>{{ fmt.pct(r.signupToTrial) }}<span class="mk-ci">{{ r.trials }}</span></td>
          <td>{{ r.paid }}</td>
          <td>{{ fmt.pct(r.signupToPaid) }}@if (r.signups > 0) { <span class="mk-ci">{{ fmt.pct(r.signupToPaidLow, 0) }}–{{ fmt.pct(r.signupToPaidHigh, 0) }}</span> }</td>
          <td>{{ fmt.pct(r.trialToPaid) }}</td>
          <td>{{ fmt.pct(r.paidRenewed) }}</td>
          <td>{{ fmt.pct(r.d30) }}<span class="mk-ci">of {{ r.d30Eligible }}</span></td>
          <td>{{ r.spend ? fmt.eur(r.spend) : '—' }}</td>
          <td>{{ fmt.eur(r.costPerSignup) }}</td>
          <td>{{ fmt.eur(r.costPerPaid) }}</td>
          <td>{{ r.estimatedRevenue ? fmt.eur(r.estimatedRevenue) : '—' }}</td>
          <td [class.mk-up]="(r.returnOnSpend ?? 0) > 0" [class.mk-down]="r.returnOnSpend !== null && r.returnOnSpend < 0">{{ fmt.pct(r.returnOnSpend, 0) }}</td>
        </tr>
      }
    </tbody></table></div></ng-template>

      <h2>Channels</h2>
      <p class="mk-note">A channel type groups all its campaigns. Organic is everybody who arrived without a campaign link or an invitation code; referral is the invitation programme.</p>
      <ng-container *ngTemplateOutlet="tbl; context: { $implicit: channels() }" />

      <h2>Campaigns</h2>
      @if (d.campaigns.length) {
        <ng-container *ngTemplateOutlet="tbl; context: { $implicit: campaigns() }" />
        <p class="mk-muted">Click a campaign to manage its link and spend in the Campaigns tab.</p>
      } @else { <p class="mk-empty">No campaigns yet. Create one in the Campaigns tab to get a tracking link.</p> }

      <details class="mk-help mk-panel" style="margin-top:18px">
        <summary>How to read these numbers</summary>
        <ul>
          <li><b>Attribution.</b> A person counts in one strategy only: an invitation code wins over a campaign, a campaign wins over organic. The campaign is known when the account is created, from the link the person tapped before installing from Google Play.</li>
          <li><b>Clicks and installs.</b> Clicks are link openings (link-preview robots excluded). Installs are first launches where the app read the campaign. People who install from elsewhere, or on another device, show up as organic.</li>
          <li><b>Conversion</b> is measured on the accounts created in the period. A payer is someone whose first real payment was confirmed by Google Play; free trials, test purchases and promotional time never count.</li>
          <li><b>Confidence.</b> With few signups, a rate swings a lot. Below {{ d.minReliable }} signups a strategy is flagged "small sample"; use the range shown under each rate before deciding anything.</li>
          <li><b>Cost.</b> Spend is what you entered per campaign day in the period. Estimated revenue is the net income of those customers so far (renewals included, refunds deducted), computed from the Google Play prices of the tax settings, VAT and Google's fee already taken out. It is orientative: the real figures are in Google Play Console.</li>
          <li><b>Retention.</b> "Still training" means the person logged a workout in the week that starts 30 days after signing up. Only people old enough to have had that week are counted.</li>
        </ul>
      </details>
    } @else if (!error()) { <p class="mk-empty">Loading…</p> }
  `
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
