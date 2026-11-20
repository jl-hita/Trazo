import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MarketingApi, fmt, isoDay, marketingError } from './marketing.api';
import type { Campaign, CampaignDetail, CampaignInput, CampaignItem, Range } from './marketing.models';
import { ColumnsComponent, Series } from './marketing.charts';

interface Draft { slug: string; name: string; channel: string; status: string; startsOn: string; endsOn: string; notes: string; }

const CHANNEL_HELP: Record<string, string> = {
  ads: 'Paid ads', social: 'Social media posts', influencer: 'Influencers and creators', email: 'E-mail', partner: 'Partners and communities',
  content: 'Blog, SEO, video', press: 'Press and reviews', event: 'Events and talks', other: 'Other'
};

/**
 * Campaign links. Each campaign has a permanent tracking link; whoever opens it is sent to Google Play and, if they install and sign up,
 * the account is attributed to the campaign. Spend is entered per day so cost per signup and per payer can be computed for any period.
 */
@Component({
  selector: 'mk-campaigns',
  standalone: true,
  imports: [FormsModule, ColumnsComponent],
  templateUrl: './campaigns.tab.html'
})
export class CampaignsTabComponent {
  private readonly api = inject(MarketingApi);
  readonly range = input.required<Range>();
  protected readonly fmt = fmt;
  protected readonly items = signal<CampaignItem[]>([]);
  protected readonly channels = signal<string[]>(Object.keys(CHANNEL_HELP).filter(k => k !== 'other').concat('other'));
  protected readonly detail = signal<CampaignDetail | null>(null);
  protected readonly link = signal('');
  protected readonly editing = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected readonly dayLabels = signal<string[]>([]);
  protected readonly daySeries = signal<Series[]>([]);
  protected draft: Draft = this.emptyDraft();
  protected spend = { day: isoDay(new Date()), amount: null as number | null, note: '' };

  constructor() {
    void this.reload();
    // A new period reloads the figures of the campaign being looked at (reading it untracked: opening it sets it again).
    effect(() => { this.range(); const d = untracked(() => this.detail()); if (d) void untracked(() => this.open({ campaign: d.campaign, link: this.link() })); });
  }

  protected help(channel: string): string { return CHANNEL_HELP[channel] ?? channel; }

  private emptyDraft(): Draft { return { slug: '', name: '', channel: 'ads', status: 'active', startsOn: '', endsOn: '', notes: '' }; }

  private async reload(): Promise<void> {
    try {
      this.items.set((await this.api.campaigns()).items);
      this.channels.set((await this.api.options()).channels);
    } catch (e) { this.error.set(marketingError(e)); }
  }

  protected async open(item: CampaignItem): Promise<void> {
    this.error.set('');
    try {
      this.link.set(item.link);
      const d = await this.api.campaign(item.campaign.id, this.range());
      this.detail.set(d);
      this.dayLabels.set(d.days.map(x => x.day));
      this.daySeries.set([
        { name: 'Clicks', color: '#5b8fcb', values: d.days.map(x => x.clicks) },
        { name: 'Installs', color: '#d6a83e', values: d.days.map(x => x.installs) },
        { name: 'Signups', color: '#4fae78', values: d.days.map(x => x.signups) }
      ]);
    } catch (e) { this.error.set(marketingError(e)); }
  }

  protected edit(c: Campaign): void {
    this.editing.set(c.id);
    this.draft = { slug: c.slug, name: c.name, channel: c.channel, status: c.status, startsOn: c.startsOn ?? '', endsOn: c.endsOn ?? '', notes: c.notes ?? '' };
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected cancel(): void { this.editing.set(null); this.draft = this.emptyDraft(); }

  private input(): CampaignInput {
    const d = this.draft;
    return { slug: d.slug.trim(), name: d.name.trim(), channel: d.channel, status: d.status, startsOn: d.startsOn || null, endsOn: d.endsOn || null, notes: d.notes.trim() || null };
  }

  protected async save(): Promise<void> {
    this.busy.set(true); this.error.set(''); this.message.set('');
    try {
      const id = this.editing();
      const result = id ? await this.api.updateCampaign(id, this.input()) : await this.api.createCampaign(this.input());
      this.message.set(id ? 'Campaign saved.' : `Campaign created. Its link is ${result.link}`);
      this.cancel();
      await this.reload();
      await this.open(result);
    } catch (e) { this.error.set(marketingError(e)); } finally { this.busy.set(false); }
  }

  protected async setStatus(c: Campaign, status: string): Promise<void> {
    this.busy.set(true); this.error.set('');
    try {
      const result = await this.api.updateCampaign(c.id, { name: c.name, channel: c.channel, status, startsOn: c.startsOn, endsOn: c.endsOn, notes: c.notes });
      await this.reload();
      await this.open(result);
    } catch (e) { this.error.set(marketingError(e)); } finally { this.busy.set(false); }
  }

  protected async addSpend(): Promise<void> {
    const d = this.detail();
    if (!d || this.spend.amount === null) return;
    this.busy.set(true); this.error.set('');
    try {
      await this.api.addSpend(d.campaign.id, { day: this.spend.day, amount: this.spend.amount, note: this.spend.note.trim() || null });
      this.spend = { day: this.spend.day, amount: null, note: '' };
      await this.reload();
      await this.open({ campaign: d.campaign, link: this.link() });
    } catch (e) { this.error.set(marketingError(e)); } finally { this.busy.set(false); }
  }

  protected async removeSpend(campaignId: string, spendId: string): Promise<void> {
    const d = this.detail();
    if (!d) return;
    try {
      await this.api.deleteSpend(campaignId, spendId);
      await this.reload();
      await this.open({ campaign: d.campaign, link: this.link() });
    } catch (e) { this.error.set(marketingError(e)); }
  }

  protected async copy(text: string): Promise<void> {
    try { await navigator.clipboard.writeText(text); this.message.set('Link copied.'); } catch { this.message.set(`Copy it by hand: ${text}`); }
  }
}
