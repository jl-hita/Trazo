import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiTableComponent } from '../../shared/ui/ui-table.component';
import { ErrorTicketDetail, ErrorTicketPage, ErrorTicketStatus, ErrorTicketSummary } from '../../core/admin.models';
import { ErrorTicketsApi } from './error-tickets.api';
import { errorMessage } from '../../core/setup.api';
import { UI_COPY } from '../../core/ui-copy';

/**
 * Anonymous error tickets: the unexpected errors of the API and the crashes of the Android app, one ticket per distinct error,
 * with the complete stack trace. Superadmin only (a trace can reveal how the system is built).
 */
@Component({
  selector: 'trazo-error-tickets-page',
  standalone: true,
  imports: [FormsModule, DatePipe, UiButtonComponent, UiTableComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './error-tickets.page.scss',
  templateUrl: './error-tickets.page.html'
})
export class ErrorTicketsPageComponent {
  private readonly api = inject(ErrorTicketsApi);
  private searchTimer?: ReturnType<typeof setTimeout>;
  private static readonly PageSize = 25;

  protected readonly labels = UI_COPY.errorTickets;
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
      this.data.set(await this.api.list(this.q(), this.status(), this.source(), this.page(), ErrorTicketsPageComponent.PageSize));
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
      const loaded = await this.api.get(ticket.id);
      if (this.openId() !== ticket.id) return;
      this.detail.set(loaded);
      if (loaded.status === 'new') await this.setStatus(loaded, 'seen');
    } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async setStatus(ticket: ErrorTicketDetail, status: ErrorTicketStatus): Promise<void> {
    try {
      await this.api.setStatus(ticket.id, status);
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
        this.info.set(this.labels.testExceptionTriggered);
        await new Promise(resolve => setTimeout(resolve, 1500)); // the ticket is stored by a background writer
        this.page.set(1);
        await this.load();
      } else {
        this.error.set(status === 0 ? this.labels.noApiResponse : `${this.labels.unexpectedResponse} ${status}${this.labels.testNotThrown}`);
      }
    } finally { this.testing.set(false); }
  }

  protected labelStatus(status: ErrorTicketStatus): string { return this.labels.statuses[status]; }

  protected toggleOriginalTrace(): void { this.showOriginalTrace.update(value => !value); }

  protected async copy(ticket: ErrorTicketDetail): Promise<void> {
    try {
      const trace = this.showOriginalTrace() && ticket.originalStackTrace ? ticket.originalStackTrace : ticket.stackTrace;
      await navigator.clipboard.writeText(trace);
      this.error.set('');
    }
    catch { this.error.set(this.labels.copyDenied); }
  }

  protected async remove(ticket: ErrorTicketDetail): Promise<void> {
    try {
      await this.api.delete(ticket.id);
      this.confirmId.set(null);
      this.openId.set(null);
      if (this.tickets().length === 1 && this.page() > 1) this.page.update(p => p - 1);
      await this.load();
    } catch (e) { this.error.set(errorMessage(e)); }
  }
}
