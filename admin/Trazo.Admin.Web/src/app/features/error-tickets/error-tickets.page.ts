import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminApi, ErrorTicketDetail, ErrorTicketPage, ErrorTicketStatus, ErrorTicketSummary } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

/**
 * Anonymous error tickets: the unexpected errors of the API and the crashes of the Android app, one ticket per distinct error,
 * with the complete stack trace. Superadmin only (a trace can reveal how the system is built).
 */
@Component({
  selector: 'trazo-error-tickets-page',
  standalone: true,
  imports: [FormsModule, DatePipe],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .et-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-bottom: 12px; }
    .et-toolbar input[type=search], .et-toolbar select { padding: 8px 10px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; }
    .et-toolbar input[type=search] { flex: 1 1 240px; }
    .et-toolbar input:focus, .et-toolbar select:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .et-btn { padding: 8px 12px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .et-btn:hover:not(:disabled) { background: #22364f; }
    .et-btn:disabled { opacity: .45; cursor: not-allowed; }
    .et-btn.danger { border-color: #8a4040; color: #f4b0b0; background: #2c1717; }
    .et-stats { display: flex; gap: 12px; margin-bottom: 14px; flex-wrap: wrap; }
    .et-stat { padding: 10px 16px; border: 1px solid #293444; border-radius: 8px; background: #131b27; min-width: 110px; }
    .et-stat b { display: block; font-size: 22px; font-variant-numeric: tabular-nums; color: #e8edf5; }
    .et-stat span { color: #8291a6; font-size: 11px; letter-spacing: .06em; text-transform: uppercase; }
    .et-table { width: 100%; border-collapse: collapse; border: 1px solid #293444; background: #131b27; font-size: 13px; }
    .et-table th { padding: 10px 12px; text-align: left; color: #8291a6; font-size: 10px; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; border-bottom: 1px solid #293444; }
    .et-table td { padding: 9px 12px; border-bottom: 1px solid #1d2735; color: #dbe5f2; font-variant-numeric: tabular-nums; vertical-align: top; }
    .et-table tr.row { cursor: pointer; }
    .et-table tr.row:hover td { background: #172234; }
    .et-table tr.open td { background: #172234; }
    .et-type { font-family: ui-monospace, Consolas, monospace; font-size: 12px; word-break: break-all; }
    .et-badge { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 11px; border: 1px solid #34465b; }
    .et-badge.new { border-color: #a05a3a; color: #f4c3a3; }
    .et-badge.seen { border-color: #4a6585; color: #bcd2ec; }
    .et-badge.resolved { border-color: #3d7a55; color: #a9dbbd; }
    .et-detail td { background: #0f1621; }
    .et-meta { display: flex; flex-wrap: wrap; gap: 8px 24px; color: #8291a6; font-size: 12px; margin-bottom: 10px; }
    .et-trace { margin: 0 0 12px; padding: 12px; max-height: 420px; overflow: auto; background: #0a0e15; border: 1px solid #293444; border-radius: 6px; color: #d6e2f1; font: 12px/1.5 ui-monospace, Consolas, monospace; white-space: pre; }
    .et-actions { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
    .et-confirm { color: #f4b0b0; font-size: 12px; }
    .et-pager { display: flex; align-items: center; justify-content: space-between; margin-top: 12px; color: #8291a6; font-size: 12px; }
    .et-info { color: #a9dbbd; font-size: 13px; margin: 0 0 12px; }
    .et-error { color: #f0a3a3; font-size: 13px; margin: 0 0 12px; }
    .et-empty { text-align: center; padding: 28px 0; color: #8291a6; }
  `],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">ADMINISTRATION</p>
        <h1>Error tickets</h1>
        <p class="subtitle">Anonymous reports of unexpected errors in the API and the Android app, grouped by error, with the complete stack trace. Superadmin only.</p>
      </div>
    </div>

    <div class="et-stats">
      <div class="et-stat"><b>{{ data()?.overall?.new ?? '—' }}</b><span>New</span></div>
      <div class="et-stat"><b>{{ data()?.overall?.seen ?? '—' }}</b><span>Seen</span></div>
      <div class="et-stat"><b>{{ data()?.overall?.resolved ?? '—' }}</b><span>Resolved</span></div>
    </div>

    <div class="et-toolbar">
      <input type="search" placeholder="Search the type or the stack trace" aria-label="Search error tickets" [ngModel]="q()" (ngModelChange)="onSearch($event)">
      <select aria-label="Status" [ngModel]="status()" (ngModelChange)="setFilter('status', $event)">
        <option value="">All statuses</option><option value="new">New</option><option value="seen">Seen</option><option value="resolved">Resolved</option>
      </select>
      <select aria-label="Source" [ngModel]="source()" (ngModelChange)="setFilter('source', $event)">
        <option value="">API and app</option><option value="backend">API</option><option value="android">Android app</option>
      </select>
      <button type="button" class="et-btn" (click)="load()" [disabled]="loading()">Refresh</button>
      <button type="button" class="et-btn" (click)="triggerTest()" [disabled]="testing()" title="Makes the API throw an exception on purpose to check this system">Trigger test error</button>
    </div>

    @if (info()) { <p class="et-info" role="status">{{ info() }}</p> }
    @if (error()) { <p class="et-error" role="alert">{{ error() }}</p> }

    <table class="et-table">
      <thead><tr><th>Error</th><th>Source</th><th>Version</th><th>Times</th><th>Last seen</th><th>Status</th></tr></thead>
      <tbody>
        @for (t of tickets(); track t.id) {
          <tr class="row" [class.open]="openId() === t.id" (click)="toggle(t)" tabindex="0" (keydown.enter)="toggle(t)">
            <td class="et-type">{{ t.errorType }}</td>
            <td>{{ t.source === 'backend' ? 'API' : 'Android' }}</td>
            <td>{{ t.appVersion ?? '—' }}</td>
            <td>{{ t.occurrences }}</td>
            <td>{{ t.lastSeenAt | date: 'yyyy-MM-dd HH:mm' }}</td>
            <td><span class="et-badge" [class]="'et-badge ' + t.status">{{ t.status }}</span></td>
          </tr>
          @if (openId() === t.id) {
            <tr class="et-detail">
              <td colspan="6">
                @if (detail(); as d) {
                  <div class="et-meta">
                    <span>First seen {{ d.firstSeenAt | date: 'yyyy-MM-dd HH:mm' }}</span>
                    <span>Last seen {{ d.lastSeenAt | date: 'yyyy-MM-dd HH:mm' }}</span>
                    <span>{{ d.occurrences }} {{ d.occurrences === 1 ? 'time' : 'times' }}</span>
                  </div>
                  @if (d.originalStackTrace) {
                    <button type="button" class="et-btn" (click)="toggleOriginalTrace()">
                      {{ showOriginalTrace() ? 'Show retraced trace' : 'Show original obfuscated trace' }}
                    </button>
                  }
                  <pre class="et-trace" tabindex="0">{{ showOriginalTrace() && d.originalStackTrace ? d.originalStackTrace : d.stackTrace }}</pre>
                  <div class="et-actions">
                    @if (d.status !== 'resolved') { <button type="button" class="et-btn" (click)="setStatus(d, 'resolved')">Mark as resolved</button> }
                    @if (d.status === 'resolved') { <button type="button" class="et-btn" (click)="setStatus(d, 'new')">Reopen</button> }
                    @if (d.status === 'new') { <button type="button" class="et-btn" (click)="setStatus(d, 'seen')">Mark as seen</button> }
                    <button type="button" class="et-btn" (click)="copy(d)">Copy stack trace</button>
                    @if (confirmId() === d.id) {
                      <span class="et-confirm">Delete permanently?</span>
                      <button type="button" class="et-btn danger" (click)="remove(d)">Delete</button>
                      <button type="button" class="et-btn" (click)="confirmId.set(null)">Cancel</button>
                    } @else {
                      <button type="button" class="et-btn" (click)="confirmId.set(d.id)">Delete</button>
                    }
                  </div>
                } @else { Loading… }
              </td>
            </tr>
          }
        } @empty {
          <tr><td colspan="6" class="et-empty">{{ loading() ? 'Loading…' : 'No error tickets match.' }}</td></tr>
        }
      </tbody>
    </table>

    <div class="et-pager">
      <span>{{ data()?.totalCount ?? 0 }} tickets</span>
      <span>
        <button type="button" class="et-btn" (click)="go(page() - 1)" [disabled]="page() <= 1 || loading()">Previous</button>
        Page {{ page() }} of {{ totalPages() }}
        <button type="button" class="et-btn" (click)="go(page() + 1)" [disabled]="page() >= totalPages() || loading()">Next</button>
      </span>
    </div>
  `
})
export class ErrorTicketsPageComponent {
  private readonly api = inject(AdminApi);
  private searchTimer?: ReturnType<typeof setTimeout>;
  private static readonly PageSize = 25;

  protected readonly data = signal<ErrorTicketPage | null>(null);
  protected readonly q = signal('');
  protected readonly status = signal('');
  protected readonly source = signal('');
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly info = signal('');
  protected readonly testing = signal(false);
  protected readonly openId = signal<string | null>(null);
  protected readonly detail = signal<ErrorTicketDetail | null>(null);
  protected readonly showOriginalTrace = signal(false);
  protected readonly confirmId = signal<string | null>(null);
  protected readonly tickets = computed<ErrorTicketSummary[]>(() => this.data()?.items ?? []);
  protected readonly totalPages = computed(() => Math.max(1, this.data()?.totalPages ?? 1));

  constructor() { void this.load(); }

  protected async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.data.set(await this.api.listErrorTickets(this.q(), this.status(), this.source(), this.page(), ErrorTicketsPageComponent.PageSize));
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

  protected setFilter(which: 'status' | 'source', value: string): void {
    (which === 'status' ? this.status : this.source).set(value);
    this.page.set(1);
    void this.load();
  }

  protected go(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.totalPages()));
    this.openId.set(null);
    void this.load();
  }

  /** Opens a ticket and loads its stack trace; opening a new one marks it as seen. */
  protected async toggle(ticket: ErrorTicketSummary): Promise<void> {
    this.confirmId.set(null);
    if (this.openId() === ticket.id) { this.openId.set(null); return; }
    this.openId.set(ticket.id);
    this.detail.set(null);
    this.showOriginalTrace.set(false);
    try {
      const loaded = await this.api.getErrorTicket(ticket.id);
      if (this.openId() !== ticket.id) return;
      this.detail.set(loaded);
      if (loaded.status === 'new') await this.setStatus(loaded, 'seen');
    } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async setStatus(ticket: ErrorTicketDetail, status: ErrorTicketStatus): Promise<void> {
    try {
      await this.api.setErrorTicketStatus(ticket.id, status);
      this.detail.set({ ...ticket, status });
      await this.load();
    } catch (e) { this.error.set(errorMessage(e)); }
  }

  /** Throws an exception in the API on purpose to check the whole chain: ticket, console and email notice. */
  protected async triggerTest(): Promise<void> {
    this.testing.set(true);
    this.info.set('');
    this.error.set('');
    try {
      const status = await this.api.triggerTestException();
      if (status === 500) {
        this.info.set('The API threw the test exception (HTTP 500). The ticket appears in a moment.');
        await new Promise(resolve => setTimeout(resolve, 1500)); // the ticket is stored by a background writer
        this.page.set(1);
        await this.load();
      } else {
        this.error.set(status === 0 ? 'The API did not answer.' : `Unexpected answer (HTTP ${status}); the test exception was not thrown.`);
      }
    } finally { this.testing.set(false); }
  }

  protected toggleOriginalTrace(): void { this.showOriginalTrace.update(value => !value); }

  protected async copy(ticket: ErrorTicketDetail): Promise<void> {
    try {
      const trace = this.showOriginalTrace() && ticket.originalStackTrace ? ticket.originalStackTrace : ticket.stackTrace;
      await navigator.clipboard.writeText(trace);
      this.error.set('');
    }
    catch { this.error.set('The browser did not allow copying; select the text and copy it by hand.'); }
  }

  protected async remove(ticket: ErrorTicketDetail): Promise<void> {
    try {
      await this.api.deleteErrorTicket(ticket.id);
      this.confirmId.set(null);
      this.openId.set(null);
      if (this.tickets().length === 1 && this.page() > 1) this.page.update(p => p - 1);
      await this.load();
    } catch (e) { this.error.set(errorMessage(e)); }
  }
}
