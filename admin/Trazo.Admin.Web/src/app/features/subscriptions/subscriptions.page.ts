import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminApi, AdminSubscriptionDetail, AdminSubscriptionSummary } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

const STATUSES: { value: string; label: string }[] = [
  { value: 'trial', label: 'Trial' }, { value: 'active', label: 'Active' }, { value: 'grace_period', label: 'Grace period' },
  { value: 'on_hold', label: 'On hold' }, { value: 'canceled', label: 'Canceled' }, { value: 'expired', label: 'Expired' },
  { value: 'revoked', label: 'Revoked' }, { value: 'pending', label: 'Pending' }
];

/** Subscriptions bought through Google Play: filter, inspect and re-sync with the store. */
@Component({
  selector: 'trazo-subscriptions-page',
  standalone: true,
  imports: [FormsModule, RouterLink],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .subs-layout { display: grid; grid-template-columns: minmax(0, 1fr) 360px; gap: 18px; align-items: start; }
    .subs-layout > section { min-width: 0; overflow-x: auto; }
    .subs-toolbar { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 14px; align-items: center; }
    .subs-toolbar input, .subs-toolbar select { padding: 9px 11px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; }
    .subs-toolbar input[type=search] { flex: 1 1 220px; }
    .subs-toolbar label { display: flex; align-items: center; gap: 6px; color: #8291a6; font-size: 11px; }
    .subs-toolbar input:focus, .subs-toolbar select:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .subs-table { width: 100%; border-collapse: collapse; border: 1px solid #293444; border-radius: 8px; background: #131b27; font-size: 13px; }
    .subs-table th { padding: 11px 14px; text-align: left; color: #8291a6; font-size: 10px; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; border-bottom: 1px solid #293444; }
    .subs-table td { padding: 11px 14px; border-bottom: 1px solid #1d2735; vertical-align: middle; }
    .subs-table tr.row { cursor: pointer; }
    .subs-table tr.row:hover td, .subs-table tr.row.selected td { background: #182231; }
    .subs-table tr:last-child td { border-bottom: 0; }
    .subs-table .name { color: #e8edf5; font-weight: 500; }
    .subs-table .sub { color: #8291a6; font-size: 11px; }
    .sbadge { display: inline-block; padding: 2px 8px; border: 1px solid #34465b; border-radius: 999px; color: #aab6c7; font-size: 10px; letter-spacing: .04em; }
    .sbadge.good { border-color: #2f6a4b; color: #7fd3a0; }
    .sbadge.warn { border-color: #6b5a2e; color: #e6d39b; }
    .sbadge.bad { border-color: #7a3b3b; color: #f0a3a3; }
    .subs-pager { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-top: 12px; color: #8291a6; font-size: 12px; }
    .subs-pager button, .subs-panel button { padding: 8px 14px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .subs-pager button:disabled, .subs-panel button:disabled { opacity: .45; cursor: not-allowed; }
    .subs-panel { position: sticky; top: 16px; padding: 20px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 14px; }
    .subs-panel h2 { margin: 0; font-size: 17px; font-weight: 600; color: #f2f5fa; }
    .subs-panel .email { margin: 2px 0 0; color: #98a6b8; font-size: 12px; word-break: break-all; }
    .subs-panel .facts { display: grid; gap: 6px; margin: 0; padding: 0; list-style: none; color: #aab6c7; font-size: 12px; }
    .subs-panel .facts b { color: #e4edf8; font-weight: 500; word-break: break-all; }
    .subs-panel .ok { color: #7fd3a0; font-size: 12px; margin: 0; }
    .subs-panel .err, .subs-error { color: #f0a3a3; font-size: 12px; margin: 0; }
    .subs-panel .empty { color: #8291a6; font-size: 13px; text-align: center; padding: 18px 0; }
    .subs-panel a { color: #a9c2df; font-size: 12px; }
    @media (max-width: 1280px) { .subs-layout { grid-template-columns: 1fr; } .subs-panel { position: static; } }
  `],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">ADMINISTRATION</p>
        <h1>Subscriptions</h1>
        <p class="subtitle">Google Play subscriptions and free trials. Google is the source of truth; refresh a row to re-read it.</p>
      </div>
    </div>

    <div class="subs-layout">
      <section aria-label="Subscription list">
        <div class="subs-toolbar">
          <input type="search" placeholder="Search by user name or email" aria-label="Search subscriptions"
                 [ngModel]="q()" (ngModelChange)="onSearch($event)">
          <select aria-label="Filter by status" [ngModel]="status()" (ngModelChange)="setFilter('status', $event)">
            <option value="">Any status</option>
            @for (s of statuses; track s.value) { <option [value]="s.value">{{ s.label }}</option> }
          </select>
          <select aria-label="Filter by source" [ngModel]="source()" (ngModelChange)="setFilter('source', $event)">
            <option value="">Any source</option>
            <option value="google_play">Google Play</option>
          </select>
          <label>Expires from <input type="date" [ngModel]="expiresFrom()" (ngModelChange)="setFilter('expiresFrom', $event)"></label>
          <label>to <input type="date" [ngModel]="expiresTo()" (ngModelChange)="setFilter('expiresTo', $event)"></label>
          <select aria-label="Sort" [ngModel]="sort()" (ngModelChange)="setFilter('sort', $event)">
            <option value="">Sort: last change</option>
            <option value="expires">Sort: expiry (soonest)</option>
            <option value="started">Sort: newest</option>
          </select>
        </div>

        @if (error()) { <p class="subs-error" role="alert">{{ error() }}</p> }

        <table class="subs-table">
          <thead><tr><th>User</th><th>Status</th><th>Expires</th><th>Source</th></tr></thead>
          <tbody>
            @for (s of items(); track s.id) {
              <tr class="row" [class.selected]="selected()?.subscription?.id === s.id" (click)="select(s)" tabindex="0" (keydown.enter)="select(s)">
                <td><div class="name">{{ s.displayName }}</div><div class="sub">{{ s.email }}</div></td>
                <td><span class="sbadge" [class]="'sbadge ' + tone(s)">{{ label(s.status) }}</span></td>
                <td>{{ formatDate(s.expiresAt) }}@if (s.hasAccess && !s.autoRenewing) { <div class="sub">not renewing</div> }</td>
                <td>{{ s.source === 'google_play' ? 'Google Play' : s.source }}</td>
              </tr>
            } @empty {
              <tr><td colspan="4" class="sub" style="text-align:center;padding:26px">{{ loading() ? 'Loading…' : 'No subscriptions match these filters.' }}</td></tr>
            }
          </tbody>
        </table>

        <div class="subs-pager">
          <span>{{ totalCount() }} subscription{{ totalCount() === 1 ? '' : 's' }} · page {{ page() }} of {{ totalPages() || 1 }}</span>
          <span>
            <button type="button" (click)="goTo(page() - 1)" [disabled]="page() <= 1 || loading()">Previous</button>
            <button type="button" (click)="goTo(page() + 1)" [disabled]="page() >= totalPages() || loading()">Next</button>
          </span>
        </div>
      </section>

      <aside class="subs-panel" aria-label="Subscription details">
        @if (selected(); as d) {
          <div>
            <h2>{{ d.subscription.displayName }}</h2>
            <p class="email">{{ d.subscription.email }}</p>
          </div>
          <ul class="facts">
            <li>Status: <b>{{ label(d.subscription.status) }}</b> · access <b>{{ d.subscription.hasAccess ? 'yes' : 'no' }}</b></li>
            <li>Product: <b>{{ d.subscription.productId }}</b></li>
            <li>Started: <b>{{ formatDate(d.subscription.startedAt) }}</b></li>
            <li>Expires: <b>{{ formatDate(d.subscription.expiresAt) }}</b></li>
            <li>Auto-renewing: <b>{{ d.subscription.autoRenewing ? 'yes' : 'no' }}</b></li>
            <li>Google state: <b>{{ d.storeState || 'unknown' }}</b></li>
            <li>Latest order: <b>{{ d.latestOrderId || 'none' }}</b></li>
            <li>Acknowledged: <b>{{ d.acknowledgedAt ? formatDate(d.acknowledgedAt) : 'not yet' }}</b></li>
            <li>Test purchase: <b>{{ d.isTestPurchase ? 'yes' : 'no' }}</b></li>
            <li>Token fingerprint: <b>{{ d.purchaseTokenFingerprint }}</b></li>
            <li>Last change: <b>{{ formatDate(d.subscription.updatedAt) }}</b></li>
          </ul>
          <div><button type="button" (click)="refresh()" [disabled]="busy()">Refresh from Google Play</button></div>
          <a routerLink="/users">Manage the user in Users</a>
          @if (message()) { <p class="ok" role="status">{{ message() }}</p> }
          @if (panelError()) { <p class="err" role="alert">{{ panelError() }}</p> }
        } @else {
          <p class="empty">Select a subscription to see its details.</p>
        }
      </aside>
    </div>
  `
})
export class SubscriptionsPageComponent {
  private readonly api = inject(AdminApi);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private requestId = 0;

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
    try { this.selected.set(await this.api.getSubscription(summary.id)); }
    catch (e) { this.panelError.set(errorMessage(e)); }
  }

  protected async refresh(): Promise<void> {
    const current = this.selected();
    if (!current) return;
    this.busy.set(true); this.message.set(''); this.panelError.set('');
    try {
      this.selected.set(await this.api.refreshSubscription(current.subscription.id));
      this.message.set('Refreshed from Google Play.');
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
    return new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
  }

  private async load(): Promise<void> {
    const id = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    try {
      const result = await this.api.listSubscriptions({
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
