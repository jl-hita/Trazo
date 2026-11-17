import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

/** Referral programme and conversion funnel: totals, audit of invitations and rewards. Economic data is never edited, only voided with a reason. */
@Component({
  selector: 'trazo-referrals-page',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .rf-toolbar { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 16px; align-items: end; }
    .rf-toolbar label { display: grid; gap: 4px; color: #8291a6; font-size: 11px; }
    .rf-toolbar input, .rf-toolbar select { padding: 9px 11px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; }
    .rf-toolbar button, .rf-btn { padding: 9px 14px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .rf-btn.danger { border-color: #7a3b3b; color: #f0a3a3; }
    .rf-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 12px; margin-bottom: 20px; }
    .rf-card { padding: 14px; border: 1px solid #293444; border-radius: 8px; background: #131b27; }
    .rf-card b { display: block; font-size: 22px; color: #f2f5fa; }
    .rf-card span { color: #8291a6; font-size: 11px; }
    .rf h2 { margin: 22px 0 10px; font-size: 14px; color: #a9c2df; letter-spacing: .08em; text-transform: uppercase; }
    .rf-table { width: 100%; border-collapse: collapse; border: 1px solid #293444; border-radius: 8px; background: #131b27; font-size: 13px; }
    .rf-table th { padding: 9px 12px; text-align: left; color: #8291a6; font-size: 10px; letter-spacing: .12em; text-transform: uppercase; border-bottom: 1px solid #293444; }
    .rf-table td { padding: 9px 12px; border-bottom: 1px solid #1d2735; color: #d4deea; }
    .rf-table .muted { color: #8291a6; font-size: 11px; }
    .rf .err { color: #f0a3a3; font-size: 12px; }
    .rf .ok { color: #7fd3a0; font-size: 12px; }
    .rf .warn { color: #e6d39b; }
  `],
  template: `
    <div class="page-heading">
      <div><p class="eyebrow">GROWTH</p><h1>Referrals &amp; funnel</h1>
        <p class="subtitle">Invitations, conversions to a first real payment, rewards (promotional days or Google Play deferrals) and the signup-to-retention funnel. Read-only: a reward can only be voided, with a reason that is recorded.</p></div>
    </div>
    <div class="rf">
      <div class="rf-toolbar">
        <label>From <input type="date" [ngModel]="from()" (ngModelChange)="from.set($event)" name="from"></label>
        <label>To <input type="date" [ngModel]="to()" (ngModelChange)="to.set($event)" name="to"></label>
        <label>Channel
          <select [ngModel]="channel()" (ngModelChange)="channel.set($event)" name="channel">
            <option value="">All</option><option value="referral">Referral</option><option value="organic">Organic</option>
          </select></label>
        <button type="button" (click)="load()">Apply</button>
        <button type="button" (click)="reconcile()">Run reconciliation</button>
        @if (message()) { <span class="ok" role="status">{{ message() }}</span> }
        @if (error()) { <span class="err" role="alert">{{ error() }}</span> }
      </div>

      @if (summary(); as s) {
        <h2>Referral programme</h2>
        <div class="rf-cards">
          <div class="rf-card"><b>{{ s.linkOpens }}</b><span>Links opened</span></div>
          <div class="rf-card"><b>{{ s.shares }}</b><span>Shares</span></div>
          <div class="rf-card"><b>{{ s.installsAttributed }}</b><span>Attributable installs</span></div>
          <div class="rf-card"><b>{{ s.attributed }}</b><span>Registered (attributed)</span></div>
          <div class="rf-card"><b>{{ s.rewarded }}</b><span>Paid → rewarded ({{ pct(s.registeredToPaid) }})</span></div>
          <div class="rf-card"><b>{{ s.rewardDaysGranted }}</b><span>Reward days granted</span></div>
          <div class="rf-card"><b>{{ s.rewardsPending }}</b><span>Pending ({{ s.rewardDaysPending }} d)</span></div>
          <div class="rf-card"><b>{{ s.rewardsDeferred }}</b><span>Deferred at Google</span></div>
          <div class="rf-card"><b>{{ s.rewardsPromo }}</b><span>Promotional windows</span></div>
          <div class="rf-card"><b>{{ s.voided }}</b><span>Voided</span></div>
          <div class="rf-card"><b [class.warn]="s.incidentCount > 0">{{ s.incidentCount }}</b><span>Incidents</span></div>
          <div class="rf-card"><b>{{ s.paidMonthly }} / {{ s.paidAnnual }}</b><span>Monthly / annual</span></div>
          <div class="rf-card"><b>{{ s.renewedAtLeastOnce }} / {{ s.renewedAtLeastThree }}</b><span>Renewed 1+ / 3+ times</span></div>
          <div class="rf-card"><b>{{ pct(s.linkToRegistered) }}</b><span>Link → registration</span></div>
        </div>
      }

      @if (funnel(); as f) {
        <h2>Funnel — {{ f.cohort }} accounts created in the period</h2>
        <table class="rf-table">
          <thead><tr><th>Stage</th><th>Users</th><th>vs previous</th><th>vs cohort</th></tr></thead>
          <tbody>
            @for (st of f.stages; track st.name) {
              <tr><td>{{ st.name }}</td><td>{{ st.users }}</td><td>{{ pct(st.rateOfPrevious) }}</td><td>{{ pct(st.rateOfCohort) }}</td></tr>
            }
          </tbody>
        </table>
        <p class="muted">Signup → trial {{ pct(f.signupToTrial) }} · trial → paid {{ pct(f.trialToPaid) }} · signup → paid {{ pct(f.signupToPaid) }} ·
          paid customers renewed 3+ times {{ pct(f.retainedAfterSeveralCycles) }}. Groups under {{ f.minGroupSize }} users are folded into "other".</p>
        @if (f.cohorts.length) {
          <h2>Weekly cohorts</h2>
          <table class="rf-table"><thead><tr><th>Week</th><th>Signups</th><th>Paid</th><th>Renewed</th></tr></thead>
            <tbody>@for (c of f.cohorts; track c.week) { <tr><td>{{ c.week }}</td><td>{{ c.signups }}</td><td>{{ c.paid }}</td><td>{{ c.renewed }}</td></tr> }</tbody></table>
        }
        @if (f.byAppVersion.length) {
          <h2>By app version</h2>
          <table class="rf-table"><thead><tr><th>Version</th><th>Signups</th><th>Paid</th></tr></thead>
            <tbody>@for (g of f.byAppVersion; track g.key) { <tr><td>{{ g.key }}</td><td>{{ g.users }}</td><td>{{ g.paid }}</td></tr> }</tbody></table>
        }
        @if (f.byPlan.length) {
          <h2>First purchase by plan</h2>
          <table class="rf-table"><thead><tr><th>Product</th><th>Users</th></tr></thead>
            <tbody>@for (g of f.byPlan; track g.key) { <tr><td>{{ g.key }}</td><td>{{ g.users }}</td></tr> }</tbody></table>
        }
      }

      <h2>Invitations</h2>
      <div class="rf-toolbar">
        <label>Status
          <select [ngModel]="status()" (ngModelChange)="status.set($event); loadList()" name="status">
            <option value="">All</option><option value="registered">Registered</option><option value="rewarded">Rewarded</option>
            <option value="voided">Voided</option><option value="skipped">Skipped</option>
          </select></label>
        <label><span><input type="checkbox" [ngModel]="incidents()" (ngModelChange)="incidents.set($event); loadList()" name="incidents"> Only incidents</span></label>
      </div>
      <table class="rf-table">
        <thead><tr><th>Registered</th><th>Referrer</th><th>Invited</th><th>Status</th><th>Reward</th><th></th></tr></thead>
        <tbody>
          @for (r of items(); track r.id) {
            <tr>
              <td>{{ r.attributedAt.slice(0, 10) }}<div class="muted">{{ r.source }} · {{ r.code }}</div></td>
              <td>{{ r.referrerEmail }}<div><button class="rf-btn" type="button" (click)="ledger(r.referrerId)">Ledger</button></div></td>
              <td>{{ r.referredEmail }}</td>
              <td>{{ r.status }}@if (r.plan) { <div class="muted">{{ r.plan }}</div> }@if (r.incident) { <div class="warn">{{ r.incident }}</div> }</td>
              <td>@if (r.rewardDays) { {{ r.rewardDays }} d · {{ r.rewardStatus }} }@if (r.lastDeferNote) { <div class="muted">{{ r.lastDeferNote }}</div> }</td>
              <td>
                @if (r.rewardGrantId && (r.rewardStatus === 'pending' || r.rewardStatus === 'active')) {
                  <button class="rf-btn danger" type="button" (click)="voidGrant(r.rewardGrantId)">Void…</button>
                }
                @if (r.rewardGrantId && r.rewardStatus === 'pending') { <button class="rf-btn" type="button" (click)="retry(r.rewardGrantId)">Retry</button> }
              </td>
            </tr>
          } @empty { <tr><td colspan="6" class="muted">No invitations for this filter.</td></tr> }
        </tbody>
      </table>

      @if (ledgerRows().length) {
        <h2>Premium-time ledger of the selected user</h2>
        <table class="rf-table">
          <thead><tr><th>Granted</th><th>Kind</th><th>Days</th><th>Status</th><th>Window / Google</th><th>Notes</th></tr></thead>
          <tbody>
            @for (g of ledgerRows(); track g.id) {
              <tr>
                <td>{{ g.grantedAt.slice(0, 10) }}</td><td>{{ g.kind }}</td><td>{{ g.days }}</td><td>{{ g.status }}</td>
                <td>@if (g.startsAt) { {{ g.startsAt.slice(0, 10) }} → {{ g.endsAt?.slice(0, 10) }} }
                  @if (g.googleExpiryAfter) { Google expiry {{ g.googleExpiryBefore?.slice(0, 10) }} → {{ g.googleExpiryAfter.slice(0, 10) }} }</td>
                <td>{{ g.voidReason ?? g.lastDeferError }}@if (g.voidedBy) { <span class="muted"> (by {{ g.voidedBy }})</span> }</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `
})
export class ReferralsPageComponent {
  private readonly api = inject(AdminApi);
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly channel = signal('');
  protected readonly status = signal('');
  protected readonly incidents = signal(false);
  protected readonly summary = signal<any>(null);
  protected readonly funnel = signal<any>(null);
  protected readonly items = signal<any[]>([]);
  protected readonly ledgerRows = signal<any[]>([]);
  protected readonly message = signal('');
  protected readonly error = signal('');

  constructor() { void this.load(); }

  protected pct(value: number | null | undefined): string {
    return value == null ? '–' : `${(value * 100).toFixed(1)}%`;
  }

  private range() {
    return { from: this.from() ? new Date(this.from()).toISOString() : undefined, to: this.to() ? new Date(this.to() + 'T23:59:59Z').toISOString() : undefined };
  }

  protected async load(): Promise<void> {
    this.error.set('');
    try {
      const range = this.range();
      const [summary, funnel] = await Promise.all([
        this.api.getReferralSummary(range), this.api.getFunnel({ ...range, channel: this.channel() || undefined })
      ]);
      this.summary.set(summary); this.funnel.set(funnel);
      await this.loadList();
    } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async loadList(): Promise<void> {
    try {
      this.items.set((await this.api.getReferrals({ status: this.status() || undefined, incidents: this.incidents() || undefined })).items);
    } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async ledger(userId: string): Promise<void> {
    try { this.ledgerRows.set((await this.api.getUserLedger(userId)).items); } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async voidGrant(id: string): Promise<void> {
    const reason = window.prompt('Why is this reward being voided? (5–300 characters, it is recorded with your name)');
    if (!reason) return;
    this.message.set(''); this.error.set('');
    try { await this.api.voidGrant(id, reason); this.message.set('Reward voided.'); await this.load(); } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async retry(id: string): Promise<void> {
    this.message.set(''); this.error.set('');
    try { const r = await this.api.retryGrant(id); this.message.set(`Retry: ${r.outcome}`); await this.load(); } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async reconcile(): Promise<void> {
    this.message.set(''); this.error.set('');
    try { const r = await this.api.reconcileReferrals(); this.message.set(`Reconciled: ${r.conversions} conversions, ${r.pendingResolved} applied, ${r.pendingWaiting} waiting.`); await this.load(); }
    catch (e) { this.error.set(errorMessage(e)); }
  }
}
