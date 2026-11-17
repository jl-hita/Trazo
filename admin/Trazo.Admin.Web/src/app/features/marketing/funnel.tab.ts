import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MarketingApi, MarketingFunnel, Range, RetentionReport, fmt, marketingError } from './marketing.api';
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
  template: `
    @if (error()) { <p class="mk-err" role="alert">{{ error() }}</p> }
    <div class="mk-toolbar">
      <label>Strategy
        <select [ngModel]="segment()" (ngModelChange)="segment.set($event)" name="segment">
          <option value="all">Everybody</option>
          <option value="organic">Organic (no campaign)</option>
          <option value="referral">Referral programme</option>
          @for (c of channelOptions(); track c) { <option [value]="'channel:' + c">Channel: {{ c }}</option> }
          @for (c of campaignOptions(); track c.slug) { <option [value]="'campaign:' + c.slug">Campaign: {{ c.name }}</option> }
        </select></label>
    </div>

    @if (funnel(); as f) {
      <h2>Funnel: {{ f.segmentName }} · {{ f.cohort }} accounts created in the period</h2>
      @if (f.cohort === 0) { <p class="mk-empty">No accounts were created by this strategy in the period.</p> }
      @else {
        <div class="mk-panel"><mk-bars [rows]="stageRows()" [max]="1" /></div>
        <p class="mk-note" style="margin-top:10px">Signup → paying customer {{ fmt.pct(f.signupToPaid) }} (95% range {{ fmt.pct(f.signupToPaidLow) }}–{{ fmt.pct(f.signupToPaidHigh) }}) ·
          trial → paying {{ fmt.pct(f.trialToPaid) }} · payers who renewed {{ fmt.pct(f.paidRenewed) }}. The biggest drop between two consecutive steps is where to look first.</p>
        <div class="mk-scroll"><table class="mk-table">
          <thead><tr><th>Step</th><th>People</th><th>of the previous step</th><th>of all signups</th></tr></thead>
          <tbody>@for (s of f.stages; track s.name) {
            <tr><td>{{ label(s.name) }}</td><td>{{ s.users }}</td><td>{{ fmt.pct(s.ofPrevious) }}</td><td>{{ fmt.pct(s.ofCohort) }}</td></tr>
          }</tbody></table></div>
      }
    }

    @if (retention(); as r) {
      <h2>Retention: people who trained each week after signing up</h2>
      <p class="mk-note">Each row is the group that signed up in that week. Week 0 is the week of signup, week 1 the next one, and so on. A cell is the share of that group who logged a workout in that week; only people whose week is already over are counted. Empty cells are weeks that have not ended yet.</p>
      @if (r.cohorts.length) {
        <div class="mk-scroll"><table class="mk-table mk-heat">
          <thead><tr><th>Signup week</th><th>People</th><th>Paid</th><th>Renewed</th>@for (w of weekNumbers(); track w) { <th>Wk {{ w }}</th> }</tr></thead>
          <tbody>
            <tr><td><b>All</b></td><td>{{ r.cohort }}</td><td></td><td></td>
              @for (v of r.average; track $index) { <td class="cell" [style.background]="color(v)">{{ fmt.pct(v, 0) }}</td> }</tr>
            @for (c of r.cohorts; track c.week) {
              <tr><td>{{ c.week }}</td><td>{{ c.size }}</td><td>{{ c.paid }}</td><td>{{ c.renewed }}</td>
                @for (v of c.active; track $index) { <td class="cell" [style.background]="color(v)">{{ fmt.pct(v, 0) }}</td> }</tr>
            }
          </tbody></table></div>
      } @else { <p class="mk-empty">No accounts in this period.</p> }
    }
  `
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
