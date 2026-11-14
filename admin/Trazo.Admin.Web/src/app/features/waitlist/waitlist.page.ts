import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminApi, WaitlistEntry, WaitlistPage } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

/**
 * People who asked, from the public landing page, to be told when the app opens. Personal data: superadmin only.
 * Shows the count, searches the list, exports it as CSV and deletes one address (erasure request).
 */
@Component({
  selector: 'trazo-waitlist-page',
  standalone: true,
  imports: [FormsModule, DatePipe],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .wl-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-bottom: 12px; }
    .wl-toolbar input[type=search] { flex: 1 1 240px; padding: 8px 10px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; }
    .wl-toolbar input:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .wl-btn { padding: 8px 12px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .wl-btn:hover:not(:disabled) { background: #22364f; }
    .wl-btn:disabled { opacity: .45; cursor: not-allowed; }
    .wl-btn.danger { border-color: #8a4040; color: #f4b0b0; background: #2c1717; }
    .wl-stats { display: flex; gap: 12px; margin-bottom: 14px; flex-wrap: wrap; }
    .wl-stat { padding: 10px 16px; border: 1px solid #293444; border-radius: 8px; background: #131b27; min-width: 110px; }
    .wl-stat b { display: block; font-size: 22px; font-variant-numeric: tabular-nums; color: #e8edf5; }
    .wl-stat span { color: #8291a6; font-size: 11px; letter-spacing: .06em; text-transform: uppercase; }
    .wl-table { width: 100%; border-collapse: collapse; border: 1px solid #293444; background: #131b27; font-size: 13px; }
    .wl-table th { padding: 10px 12px; text-align: left; color: #8291a6; font-size: 10px; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; border-bottom: 1px solid #293444; }
    .wl-table td { padding: 9px 12px; border-bottom: 1px solid #1d2735; color: #dbe5f2; font-variant-numeric: tabular-nums; }
    .wl-table tr:last-child td { border-bottom: 0; }
    .wl-table td.actions { text-align: right; white-space: nowrap; }
    .wl-confirm { color: #f4b0b0; font-size: 12px; margin-right: 8px; }
    .wl-pager { display: flex; align-items: center; justify-content: space-between; margin-top: 12px; color: #8291a6; font-size: 12px; }
    .wl-error { color: #f0a3a3; font-size: 13px; margin: 0 0 12px; }
    .wl-empty { text-align: center; padding: 28px 0; color: #8291a6; }
  `],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">ADMINISTRATION</p>
        <h1>Waitlist</h1>
        <p class="subtitle">Addresses collected by the landing page, with the date of their consent. Personal data: superadmin only.</p>
      </div>
    </div>

    <div class="wl-stats">
      <div class="wl-stat"><b>{{ data()?.overall?.total ?? '—' }}</b><span>Total</span></div>
      <div class="wl-stat"><b>{{ data()?.overall?.es ?? '—' }}</b><span>Spanish</span></div>
      <div class="wl-stat"><b>{{ data()?.overall?.en ?? '—' }}</b><span>English</span></div>
    </div>

    <div class="wl-toolbar">
      <input type="search" placeholder="Search by email" aria-label="Search the waitlist" [ngModel]="q()" (ngModelChange)="onSearch($event)">
      <button type="button" class="wl-btn" (click)="exportCsv()" [disabled]="exporting() || !data()?.overall?.total">Export CSV</button>
      <button type="button" class="wl-btn" (click)="load()" [disabled]="loading()">Refresh</button>
    </div>

    @if (error()) { <p class="wl-error" role="alert">{{ error() }}</p> }

    <table class="wl-table">
      <thead><tr><th>Email</th><th>Language</th><th>Consent</th><th>Joined</th><th></th></tr></thead>
      <tbody>
        @for (e of entries(); track e.id) {
          <tr>
            <td>{{ e.email }}</td>
            <td>{{ e.language }}</td>
            <td>{{ e.consentAt | date: 'yyyy-MM-dd HH:mm' }}</td>
            <td>{{ e.createdAt | date: 'yyyy-MM-dd HH:mm' }}</td>
            <td class="actions">
              @if (confirmId() === e.id) {
                <span class="wl-confirm">Delete permanently?</span>
                <button type="button" class="wl-btn danger" (click)="remove(e)">Delete</button>
                <button type="button" class="wl-btn" (click)="confirmId.set(null)">Cancel</button>
              } @else {
                <button type="button" class="wl-btn" (click)="confirmId.set(e.id)" [attr.aria-label]="'Delete ' + e.email">Delete</button>
              }
            </td>
          </tr>
        } @empty {
          <tr><td colspan="5" class="wl-empty">{{ loading() ? 'Loading…' : (q() ? 'No address matches the search.' : 'Nobody has joined yet.') }}</td></tr>
        }
      </tbody>
    </table>

    <div class="wl-pager">
      <span>{{ data()?.totalCount ?? 0 }} {{ q() ? 'matching ' : '' }}addresses</span>
      <span>
        <button type="button" class="wl-btn" (click)="go(page() - 1)" [disabled]="page() <= 1 || loading()">Previous</button>
        Page {{ page() }} of {{ totalPages() }}
        <button type="button" class="wl-btn" (click)="go(page() + 1)" [disabled]="page() >= totalPages() || loading()">Next</button>
      </span>
    </div>
  `
})
export class WaitlistPageComponent {
  private readonly api = inject(AdminApi);
  private searchTimer?: ReturnType<typeof setTimeout>;
  private static readonly PageSize = 50;

  protected readonly data = signal<WaitlistPage | null>(null);
  protected readonly q = signal('');
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly exporting = signal(false);
  protected readonly error = signal('');
  protected readonly confirmId = signal<string | null>(null);
  protected readonly entries = computed<WaitlistEntry[]>(() => this.data()?.items ?? []);
  protected readonly totalPages = computed(() => Math.max(1, this.data()?.totalPages ?? 1));

  constructor() { void this.load(); }

  protected async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.data.set(await this.api.listWaitlist(this.q(), this.page(), WaitlistPageComponent.PageSize));
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

  protected go(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.totalPages()));
    void this.load();
  }

  protected async remove(entry: WaitlistEntry): Promise<void> {
    try {
      await this.api.deleteWaitlistEntry(entry.id);
      this.confirmId.set(null);
      if (this.entries().length === 1 && this.page() > 1) this.page.update(p => p - 1);
      await this.load();
    } catch (e) { this.error.set(errorMessage(e)); }
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
