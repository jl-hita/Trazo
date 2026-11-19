import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DownloadsReport, MarketingApi, PaywallReport, Range, WaitlistReport, fmt, marketingError } from './marketing.api';
import { BarRow, BarsComponent, ColumnsComponent, Series } from './marketing.charts';

const SOURCE_LABELS: Record<string, string> = {
  account: 'Account screen', plan: 'A locked plan', session: 'During a session', onboarding: 'Onboarding', other: 'Elsewhere'
};

/** The levers that are not campaigns: the Premium offer (where people see it), the invitation programme and the landing waitlist. */
@Component({
  selector: 'mk-levers',
  standalone: true,
  imports: [FormsModule, BarsComponent, ColumnsComponent],
  template: `
    @if (error()) { <p class="mk-err" role="alert">{{ error() }}</p> }

    @if (message()) { <p class="mk-ok" role="status">{{ message() }}</p> }
    @if (downloads(); as dl) {
      <h2>Store downloads (Google Play Console) against accounts created</h2>
      <p class="mk-note">Google Play does not tell the backend how many people install the app, so type the weekly figure from Play Console (Statistics → Installs → new users).
        Then the panel shows how many downloads end in an account, and how many installs never register: the part no campaign link can see.</p>
      <form class="mk-form" (ngSubmit)="saveDownloads()" #df="ngForm">
        <label class="mk-field">Any day of the week <input type="date" name="week" [(ngModel)]="dlWeek" required></label>
        <label class="mk-field">New downloads that week <input type="number" name="dl" [(ngModel)]="dlCount" required min="0" step="1"></label>
        <label class="mk-field">Note <input name="note" [(ngModel)]="dlNote" maxlength="200" placeholder="Launch week"></label>
        <div><button class="mk-btn on" type="submit" [disabled]="df.invalid">Save week</button></div>
      </form>
      <div class="mk-cards" style="margin-top:14px">
        <div class="mk-card"><b>{{ fmt.int(dl.downloads) }}</b><span>Downloads in {{ dl.weeksWithData }} weeks entered</span></div>
        <div class="mk-card"><b>{{ fmt.pct(dl.downloadToSignup) }}</b><span>Became an account ({{ dl.signupsInThoseWeeks }})</span></div>
        <div class="mk-card"><b>{{ fmt.int(dl.unregisteredInstalls) }}</b><span>Installs without an account</span></div>
        <div class="mk-card"><b [class.mk-down]="dl.weeksMissing > 0">{{ dl.weeksMissing }}</b><span>Weeks of the period without a figure</span></div>
      </div>
      @if (dl.weeks.length) {
        <div class="mk-scroll"><table class="mk-table">
          <thead><tr><th>Week starting</th><th>Downloads</th><th>Accounts created</th><th>Download → account</th><th>Note</th><th></th></tr></thead>
          <tbody>@for (w of dl.weeks; track w.week) {
            <tr><td>{{ w.week }}</td><td>{{ w.downloads === null ? 'missing' : w.downloads }}</td><td>{{ w.signups }}</td><td>{{ fmt.pct(w.downloadToSignup) }}</td><td>{{ w.note ?? '' }}</td>
              <td>@if (w.downloads !== null) { <button class="mk-btn danger" type="button" (click)="removeDownloads(w.week)">Remove</button> }</td></tr>
          }</tbody></table></div>
      }
    }

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
  protected readonly waitlist = signal<WaitlistReport | null>(null);
  protected readonly downloads = signal<DownloadsReport | null>(null);
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected dlWeek = new Date().toISOString().slice(0, 10);
  protected dlCount: number | null = null;
  protected dlNote = '';

  protected readonly sourceRows = computed<BarRow[]>(() =>
    (this.paywall()?.sources ?? []).filter(s => s.views > 0).map(s => ({ label: SOURCE_LABELS[s.source] ?? s.source, value: s.share, text: `${s.views} · ${fmt.pct(s.share, 0)}` })));
  protected readonly paywallLabels = computed(() => this.paywall()?.weeks.map(w => w.week) ?? []);
  protected readonly paywallSeries = computed<Series[]>(() => [{ name: 'Openings', color: '#5b8fcb', values: this.paywall()?.weeks.map(w => w.total) ?? [] }]);
  protected readonly languages = computed(() => Object.entries(this.waitlist()?.byLanguage ?? {}));
  protected readonly waitLabels = computed(() => this.waitlist()?.weeks.map(w => w.week) ?? []);
  protected readonly waitSeries = computed<Series[]>(() => [{ name: 'Signups', color: '#4fae78', values: this.waitlist()?.weeks.map(w => w.joined) ?? [] }]);

  constructor() {
    effect(() => { const r = this.range(); void this.load(r); });
  }

  protected async saveDownloads(): Promise<void> {
    if (this.dlCount === null) return;
    this.error.set(''); this.message.set('');
    try {
      const saved = await this.api.saveDownloads({ week: this.dlWeek, downloads: this.dlCount, note: this.dlNote.trim() || null });
      this.message.set(`Saved: ${saved.downloads} downloads for the week of ${saved.week}.`);
      this.dlCount = null; this.dlNote = '';
      await this.load(this.range());
    } catch (e) { this.error.set(marketingError(e)); }
  }

  protected async removeDownloads(week: string): Promise<void> {
    this.error.set(''); this.message.set('');
    try { await this.api.deleteDownloads(week); await this.load(this.range()); } catch (e) { this.error.set(marketingError(e)); }
  }

  private async load(range: Range): Promise<void> {
    try {
      const [p, w, dl] = await Promise.all([this.api.paywall(range), this.api.waitlist(), this.api.downloads(range)]);
      this.paywall.set(p); this.waitlist.set(w); this.downloads.set(dl);
    } catch (e) { this.error.set(marketingError(e)); }
  }
}
