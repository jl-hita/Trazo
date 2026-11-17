import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MarketingApi, PaywallReport, Range, ReferralReport, WaitlistReport, fmt, marketingError } from './marketing.api';
import { BarRow, BarsComponent, ColumnsComponent, Series } from './marketing.charts';

const SOURCE_LABELS: Record<string, string> = {
  account: 'Account screen', plan: 'A locked plan', session: 'During a session', onboarding: 'Onboarding', other: 'Elsewhere'
};

/** The levers that are not campaigns: the Premium offer (where people see it), the invitation programme and the landing waitlist. */
@Component({
  selector: 'mk-levers',
  standalone: true,
  imports: [BarsComponent, ColumnsComponent],
  template: `
    @if (error()) { <p class="mk-err" role="alert">{{ error() }}</p> }

    @if (paywall(); as p) {
      <h2>Premium offer: where people see it</h2>
      <p class="mk-note">How many times the Premium offer was opened, by the place in the app that opened it. It is an anonymous counter (nobody can be followed). The more a place opens it without payments, the more that place is worth improving.</p>
      <div class="mk-cards">
        <div class="mk-card"><b>{{ fmt.int(p.total) }}</b><span>Times opened</span></div>
        <div class="mk-card"><b>{{ fmt.int(p.firstPurchases) }}</b><span>New paying customers</span></div>
        <div class="mk-card"><b>{{ p.viewsPerPurchase ?? '—' }}</b><span>Offer openings per new payer</span></div>
      </div>
      @if (p.total) {
        <div class="mk-grid2">
          <div class="mk-panel"><mk-bars [rows]="sourceRows()" [max]="1" /></div>
          <div class="mk-panel"><mk-columns [labels]="paywallLabels()" [series]="paywallSeries()" label="Premium offer openings per week" /></div>
        </div>
      } @else { <p class="mk-empty">The offer was not opened in this period.</p> }
    }

    @if (referrals(); as r) {
      <h2>Invitation programme</h2>
      <div class="mk-cards">
        <div class="mk-card"><b>{{ r.linkOpens }}</b><span>Invitation links opened</span></div>
        <div class="mk-card"><b>{{ r.shares }}</b><span>Times shared</span></div>
        <div class="mk-card"><b>{{ r.attributed }}</b><span>Accounts created by invitation ({{ fmt.pct(r.linkToRegistered, 0) }} of links)</span></div>
        <div class="mk-card"><b>{{ r.rewarded }}</b><span>Became payers ({{ fmt.pct(r.registeredToPaid, 0) }})</span></div>
        <div class="mk-card"><b>{{ r.paidMonthly }} / {{ r.paidAnnual }}</b><span>Monthly / annual</span></div>
        <div class="mk-card"><b>{{ r.renewedAtLeastOnce }} / {{ r.renewedAtLeastThree }}</b><span>Renewed 1+ / 3+ times</span></div>
        <div class="mk-card"><b>{{ r.rewardDaysGranted }}</b><span>Reward days given (your cost)</span></div>
      </div>
      @if (r.topCodes.length) {
        <p class="mk-note">Invitations are personal codes. Codes with very few invitations are grouped as "other" so nobody can be identified.</p>
        <div class="mk-panel"><mk-bars [rows]="codeRows()" /></div>
      }
    }

    @if (waitlist(); as w) {
      <h2>Landing waitlist</h2>
      <p class="mk-note">People who asked from the landing page to be told when the app opens. Counts only: e-mail addresses are not shown to this role.</p>
      <div class="mk-cards">
        <div class="mk-card"><b>{{ w.total }}</b><span>Signed up</span></div>
        <div class="mk-card"><b>{{ w.confirmed }}</b><span>Confirmed their e-mail ({{ fmt.pct(w.confirmRate, 0) }})</span></div>
        <div class="mk-card"><b>{{ w.invited }}</b><span>Invited to the closed test ({{ fmt.pct(w.inviteRate, 0) }})</span></div>
        @for (l of languages(); track l[0]) { <div class="mk-card"><b>{{ l[1] }}</b><span>Language: {{ l[0] }}</span></div> }
      </div>
      @if (w.weeks.length) { <div class="mk-panel"><mk-columns [labels]="waitLabels()" [series]="waitSeries()" label="Waitlist signups per week" /></div> }
    }
  `
})
export class LeversTabComponent {
  private readonly api = inject(MarketingApi);
  readonly range = input.required<Range>();
  protected readonly fmt = fmt;
  protected readonly paywall = signal<PaywallReport | null>(null);
  protected readonly referrals = signal<ReferralReport | null>(null);
  protected readonly waitlist = signal<WaitlistReport | null>(null);
  protected readonly error = signal('');

  protected readonly sourceRows = computed<BarRow[]>(() =>
    (this.paywall()?.sources ?? []).filter(s => s.views > 0).map(s => ({ label: SOURCE_LABELS[s.source] ?? s.source, value: s.share, text: `${s.views} · ${fmt.pct(s.share, 0)}` })));
  protected readonly paywallLabels = computed(() => this.paywall()?.weeks.map(w => w.week) ?? []);
  protected readonly paywallSeries = computed<Series[]>(() => [{ name: 'Openings', color: '#5b8fcb', values: this.paywall()?.weeks.map(w => w.total) ?? [] }]);
  protected readonly codeRows = computed<BarRow[]>(() =>
    (this.referrals()?.topCodes ?? []).map(c => ({ label: c.key === 'other' ? 'Other (small groups)' : c.key, value: c.users, text: `${c.users} · ${c.paid} paid` })));
  protected readonly languages = computed(() => Object.entries(this.waitlist()?.byLanguage ?? {}));
  protected readonly waitLabels = computed(() => this.waitlist()?.weeks.map(w => w.week) ?? []);
  protected readonly waitSeries = computed<Series[]>(() => [{ name: 'Signups', color: '#4fae78', values: this.waitlist()?.weeks.map(w => w.joined) ?? [] }]);

  constructor() {
    effect(() => { const r = this.range(); void this.load(r); });
  }

  private async load(range: Range): Promise<void> {
    this.error.set('');
    try {
      const [p, r, w] = await Promise.all([this.api.paywall(range), this.api.referrals(range), this.api.waitlist()]);
      this.paywall.set(p); this.referrals.set(r); this.waitlist.set(w);
    } catch (e) { this.error.set(marketingError(e)); }
  }
}
