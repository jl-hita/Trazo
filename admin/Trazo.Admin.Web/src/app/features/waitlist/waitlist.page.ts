import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminApi, WaitlistEntry, WaitlistPage, WaitlistStatus } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

/**
 * People who asked, from the public landing page, to be told when the app opens and to try the closed test. Personal data:
 * superadmin only. An address only counts once the person confirmed it from the email we sent. The closed-test flow is:
 * copy the confirmed addresses, paste them in the Play Console tester list, then send the invitations (the link in the
 * email only works for addresses that are already in that list).
 */
@Component({
  selector: 'trazo-waitlist-page',
  standalone: true,
  imports: [FormsModule, DatePipe],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './waitlist.page.scss',
  templateUrl: './waitlist.page.html'
})
export class WaitlistPageComponent {
  private readonly api = inject(AdminApi);
  private searchTimer?: ReturnType<typeof setTimeout>;
  private static readonly PageSize = 50;
  private static readonly CopyPageSize = 200;
  /** The API sends at most this many invitations per call. */
  private static readonly InviteChunk = 50;

  protected readonly data = signal<WaitlistPage | null>(null);
  protected readonly q = signal('');
  protected readonly status = signal<WaitlistStatus>('');
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly exporting = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly confirmId = signal<string | null>(null);
  /** Exactly the addresses that were copied for Play Console: the invitations go to these and to nobody else. */
  protected readonly copied = signal<WaitlistEntry[]>([]);
  protected readonly confirmSend = signal(false);
  protected readonly entries = computed<WaitlistEntry[]>(() => this.data()?.items ?? []);
  protected readonly totalPages = computed(() => Math.max(1, this.data()?.totalPages ?? 1));

  constructor() { void this.load(); }

  protected async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.data.set(await this.api.listWaitlist(this.q(), this.page(), WaitlistPageComponent.PageSize, this.status()));
      this.error.set('');
    } catch (e) { this.error.set(errorMessage(e)); }
    finally { this.loading.set(false); }
  }

  protected onSearch(value: string): void {
    this.q.set(value);
    this.page.set(1);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.load(), 300);
  }

  protected onStatus(value: WaitlistStatus): void {
    this.status.set(value);
    this.page.set(1);
    void this.load();
  }

  protected go(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.totalPages()));
    void this.load();
  }

  protected async remove(entry: WaitlistEntry): Promise<void> {
    try {
      await this.api.deleteWaitlistEntry(entry.id);
      this.confirmId.set(null);
      this.copied.update(list => list.filter(x => x.id !== entry.id));
      if (this.entries().length === 1 && this.page() > 1) this.page.update(p => p - 1);
      await this.load();
    } catch (e) { this.error.set(errorMessage(e)); }
  }

  /** Step 1: every confirmed address that has not been invited, as a comma-separated list on the clipboard. */
  protected async copyConfirmed(): Promise<void> {
    this.busy.set(true);
    this.notice.set('');
    try {
      const all: WaitlistEntry[] = [];
      for (let page = 1; ; page++) {
        const result = await this.api.listWaitlist('', page, WaitlistPageComponent.CopyPageSize, 'confirmed');
        all.push(...result.items);
        if (page >= result.totalPages) break;
      }
      if (!all.length) { this.notice.set('There are no confirmed addresses waiting for an invitation.'); return; }
      await navigator.clipboard.writeText(all.map(x => x.email).join(', '));
      this.copied.set(all);
      this.confirmSend.set(false);
      this.error.set('');
      this.notice.set(`${all.length} address(es) copied. Paste them in Play Console (closed test, testers), save, and then send the invitations.`);
    } catch (e) { this.error.set(errorMessage(e)); }
    finally { this.busy.set(false); }
  }

  /** Step 2: the invitations, in chunks of what the API accepts. Failed ones stay as "confirmed" so they can be copied and sent again. */
  protected async sendInvitations(): Promise<void> {
    this.busy.set(true);
    this.confirmSend.set(false);
    try {
      const ids = this.copied().map(x => x.id);
      let invited = 0, failed = 0;
      for (let i = 0; i < ids.length; i += WaitlistPageComponent.InviteChunk) {
        const result = await this.api.inviteWaitlist(ids.slice(i, i + WaitlistPageComponent.InviteChunk));
        invited += result.invited;
        failed += result.failed;
      }
      this.copied.set([]);
      this.error.set('');
      this.notice.set(failed ? `${invited} invitation(s) sent, ${failed} failed (check the email settings). The failed ones are still listed as confirmed.` : `${invited} invitation(s) sent.`);
      await this.load();
    } catch (e) { this.error.set(errorMessage(e)); }
    finally { this.busy.set(false); }
  }

  protected async exportCsv(): Promise<void> {
    this.exporting.set(true);
    try {
      const blob = await this.api.exportWaitlist();
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `trazo-waitlist-${new Date().toISOString().slice(0, 10)}.csv`;
      link.click();
      URL.revokeObjectURL(url);
      this.error.set('');
    } catch (e) { this.error.set(errorMessage(e)); }
    finally { this.exporting.set(false); }
  }
}
