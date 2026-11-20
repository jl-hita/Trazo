import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiTableComponent } from '../../shared/ui/ui-table.component';
import { RouterLink } from '@angular/router';
import type { AdminSubscriptionDetail, AdminSubscriptionSummary } from '../../core/admin.models';
import { SubscriptionsApi } from './subscriptions.api';
import { errorMessage } from '../../core/api-error';
import { UI_COPY } from '../../core/ui-copy';
import { formatUiDateTime } from '../../core/ui-format';

const STATUSES: { value: string; label: string }[] = [
  { value: 'trial', label: UI_COPY.subscriptions.statuses.trial }, { value: 'active', label: UI_COPY.subscriptions.statuses.active },
  { value: 'grace_period', label: UI_COPY.subscriptions.statuses.grace_period }, { value: 'on_hold', label: UI_COPY.subscriptions.statuses.on_hold },
  { value: 'canceled', label: UI_COPY.subscriptions.statuses.canceled }, { value: 'expired', label: UI_COPY.subscriptions.statuses.expired },
  { value: 'revoked', label: UI_COPY.subscriptions.statuses.revoked }, { value: 'pending', label: UI_COPY.subscriptions.statuses.pending }
];

/** Subscriptions bought through Google Play: filter, inspect and re-sync with the store. */
@Component({
  selector: 'trazo-subscriptions-page',
  standalone: true,
  imports: [FormsModule, RouterLink, UiButtonComponent, UiTableComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './subscriptions.page.scss',
  templateUrl: './subscriptions.page.html'
})
export class SubscriptionsPageComponent {
  private readonly api = inject(SubscriptionsApi);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private requestId = 0;

  protected readonly copy = UI_COPY.subscriptions;
  protected readonly statuses = STATUSES;
  protected readonly items = signal<AdminSubscriptionSummary[]>([]);
  protected readonly q = signal('');
  protected readonly status = signal('');
  protected readonly source = signal('');
  protected readonly expiresFrom = signal('');
  protected readonly expiresTo = signal('');
  protected readonly sort = signal('');
  protected readonly page = signal(1);
  protected readonly totalPages = signal(1);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  protected readonly selected = signal<AdminSubscriptionDetail | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly panelError = signal('');

  constructor() { void this.load(); }

  protected onSearch(value: string): void {
    this.q.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => { this.page.set(1); void this.load(); }, 300);
  }

  protected setFilter(kind: 'status' | 'source' | 'expiresFrom' | 'expiresTo' | 'sort', value: string): void {
    this[kind].set(value);
    this.page.set(1);
    void this.load();
  }

  protected goTo(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.page.set(page);
    void this.load();
  }

  protected async select(summary: AdminSubscriptionSummary): Promise<void> {
    this.message.set(''); this.panelError.set('');
    try { this.selected.set(await this.api.get(summary.id)); }
    catch (e) { this.panelError.set(errorMessage(e)); }
  }

  protected async refresh(): Promise<void> {
    const current = this.selected();
    if (!current) return;
    this.busy.set(true); this.message.set(''); this.panelError.set('');
    try {
      this.selected.set(await this.api.refresh(current.subscription.id));
      this.message.set(this.copy.messages.refreshed);
      await this.load();
    } catch (e) {
      this.panelError.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected label(status: string): string {
    return STATUSES.find(s => s.value === status)?.label ?? status;
  }

  protected tone(s: AdminSubscriptionSummary): string {
    if (s.hasAccess) return s.status === 'canceled' || s.status === 'grace_period' ? 'warn' : 'good';
    return s.status === 'on_hold' || s.status === 'revoked' ? 'bad' : '';
  }

  protected formatDate(value: string): string {
    return formatUiDateTime(value);
  }

  private async load(): Promise<void> {
    const id = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    try {
      const result = await this.api.list({
        q: this.q(), status: this.status(), source: this.source(), expiresFrom: this.expiresFrom(),
        expiresTo: this.expiresTo(), sort: this.sort(), page: this.page(), pageSize: 25
      });
      if (id !== this.requestId) return; // a newer request replaced this one
      this.items.set(result.items);
      this.totalCount.set(result.totalCount);
      this.totalPages.set(result.totalPages);
    } catch (e) {
      if (id === this.requestId) this.error.set(errorMessage(e));
    } finally {
      if (id === this.requestId) this.loading.set(false);
    }
  }
}
