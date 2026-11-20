import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

/** Referral programme operations: aggregate performance, invitation audit, reward recovery and audited reward cancellation. */
@Component({
  selector: 'trazo-referrals-page',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './referrals.page.scss',
  templateUrl: './referrals.page.html'
})
export class ReferralsPageComponent {
  private readonly api = inject(AdminApi);
  protected readonly from = signal(new Date(Date.now() - 90 * 86_400_000).toISOString().slice(0, 10));
  protected readonly to = signal(new Date().toISOString().slice(0, 10));
  protected readonly channel = signal('');
  protected readonly status = signal('');
  protected readonly incidents = signal(false);
  protected readonly summary = signal<any>(null);
  protected readonly programmeEnabled = signal(false);
  protected readonly rewardDays = signal(30);
  protected readonly maxRewardDays = signal(365);
  protected readonly settingsBusy = signal(false);
  protected readonly funnel = signal<any>(null);
  protected readonly items = signal<any[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 25;
  protected readonly pageCount = () => Math.max(1, Math.ceil(this.total() / this.pageSize));
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
      const [summary, funnel, settings] = await Promise.all([
        this.api.getReferralSummary(range),
        this.api.getFunnel({ ...range, channel: this.channel() || undefined }),
        this.api.getReferralProgrammeSettings()
      ]);
      this.summary.set(summary); this.funnel.set(funnel);
      this.programmeEnabled.set(settings.referralsEnabled);
      this.rewardDays.set(settings.referralRewardDays);
      this.maxRewardDays.set(settings.maxDays);
      await this.loadList();
    } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async saveProgramme(): Promise<void> {
    this.settingsBusy.set(true); this.error.set(''); this.message.set('');
    try {
      const saved = await this.api.saveReferralProgrammeSettings({ enabled: this.programmeEnabled(), rewardDays: this.rewardDays() });
      this.programmeEnabled.set(saved.referralsEnabled);
      this.rewardDays.set(saved.referralRewardDays);
      this.maxRewardDays.set(saved.maxDays);
      this.message.set('Referral programme settings saved.');
    } catch (e) { this.error.set(errorMessage(e)); }
    finally { this.settingsBusy.set(false); }
  }

  protected async loadList(): Promise<void> {
    try {
      const result = await this.api.getReferrals({
        status: this.status() || undefined,
        incidents: this.incidents() || undefined,
        ...this.range(),
        page: this.page(),
        pageSize: this.pageSize
      });
      this.items.set(result.items);
      this.total.set(result.total);
    } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async changePage(delta: number): Promise<void> {
    const next = this.page() + delta;
    if (next < 1 || next > this.pageCount()) return;
    this.page.set(next);
    await this.loadList();
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
