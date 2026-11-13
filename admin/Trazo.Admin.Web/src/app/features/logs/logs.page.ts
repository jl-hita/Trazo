import { Component, DestroyRef, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi, LogEntry, LogLevelGroup, LogPage } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

const LEVELS: { id: LogLevelGroup; label: string }[] = [
  { id: 'info', label: 'Info' },
  { id: 'warning', label: 'Warning' },
  { id: 'error', label: 'Error' }
];

const pad = (n: number) => String(n).padStart(2, '0');
const dateInputValue = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
/** Local midnight of a yyyy-MM-dd date, as an ISO instant. */
const startOfDay = (value: string, addDays = 0): string => {
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y, m - 1, d + addDays).toISOString();
};

/**
 * Application logs: filter by level and by date, search the text, and page from the newest entries backwards.
 * Paging is by cursor (the last entry shown), so new entries arriving meanwhile never shift or repeat a page.
 */
@Component({
  selector: 'trazo-logs-page',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .logs-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-bottom: 12px; }
    .logs-toolbar .group { display: flex; align-items: center; gap: 6px; }
    .logs-toolbar input, .logs-toolbar select { padding: 8px 10px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; color-scheme: dark; }
    .logs-toolbar input[type=search] { flex: 1 1 220px; }
    .logs-toolbar input:focus, .logs-toolbar select:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .logs-toolbar .label { color: #8291a6; font-size: 11px; letter-spacing: .06em; text-transform: uppercase; }
    .logs-btn { padding: 8px 12px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .logs-btn:hover:not(:disabled) { background: #22364f; }
    .logs-btn:disabled { opacity: .45; cursor: not-allowed; }
    .logs-btn.small { padding: 6px 9px; }
    .level-chip { display: inline-flex; align-items: center; gap: 7px; padding: 7px 12px; border: 1px solid #34465b; border-radius: 999px; background: #0d1119; color: #8291a6; font-size: 12px; cursor: pointer; }
    .level-chip .count { color: #aab6c7; font-variant-numeric: tabular-nums; }
    .level-chip[aria-pressed=true].lv-info { border-color: #4a6585; background: #162233; color: #cfdcec; }
    .level-chip[aria-pressed=true].lv-warning { border-color: #8a7233; background: #2a2412; color: #ecd9a0; }
    .level-chip[aria-pressed=true].lv-error { border-color: #8a4040; background: #2c1717; color: #f4b0b0; }
    .logs-table { width: 100%; border-collapse: collapse; border: 1px solid #293444; background: #131b27; font-size: 13px; table-layout: fixed; }
    .logs-table th { padding: 10px 12px; text-align: left; color: #8291a6; font-size: 10px; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; border-bottom: 1px solid #293444; }
    .logs-table td { padding: 9px 12px; border-bottom: 1px solid #1d2735; vertical-align: top; }
    .logs-table .c-time { width: 168px; font-variant-numeric: tabular-nums; color: #aab6c7; white-space: nowrap; }
    .logs-table .c-level { width: 92px; }
    .logs-table .c-cat { width: 220px; color: #8fa0b6; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .logs-table .c-msg { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #e8edf5; }
    .logs-table tr.entry { cursor: pointer; }
    .logs-table tr.entry:hover td, .logs-table tr.entry.open td { background: #182231; }
    .logs-table tr.entry.lv-error td.c-msg { color: #f4b0b0; }
    .logs-table tr.detail td { background: #0f1620; padding: 14px 16px; white-space: normal; }
    .logs-table tr:last-child td { border-bottom: 0; }
    .log-badge { display: inline-block; padding: 2px 8px; border: 1px solid #34465b; border-radius: 999px; font-size: 10px; letter-spacing: .06em; text-transform: uppercase; color: #aab6c7; }
    .log-badge.lv-warning { border-color: #8a7233; color: #ecd9a0; }
    .log-badge.lv-error { border-color: #8a4040; color: #f4b0b0; }
    .log-detail dl { display: grid; grid-template-columns: 110px 1fr; gap: 6px 14px; margin: 0; font-size: 12px; }
    .log-detail dt { color: #8291a6; }
    .log-detail dd { margin: 0; color: #dbe5f2; word-break: break-all; }
    .log-detail pre { margin: 0; padding: 10px 12px; border: 1px solid #263244; border-radius: 6px; background: #0a0f17; color: #cfdcec; font: 12px/1.5 ui-monospace, Consolas, monospace; white-space: pre-wrap; word-break: break-word; max-height: 320px; overflow: auto; }
    .logs-pager { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-top: 12px; color: #8291a6; font-size: 12px; }
    .logs-error { color: #f0a3a3; font-size: 13px; margin: 0 0 12px; }
    .logs-empty { text-align: center; padding: 28px 0; color: #8291a6; }
    .auto-refresh { display: inline-flex; align-items: center; gap: 6px; color: #98a6b8; font-size: 12px; }
  `],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">ADMINISTRATION</p>
        <h1>Logs</h1>
        <p class="subtitle">Application events from the API, newest first. Logs are kept for a limited time.</p>
      </div>
    </div>

    <div class="logs-toolbar" role="group" aria-label="Level filter">
      @for (l of levels; track l.id) {
        <button type="button" class="level-chip" [class]="'level-chip lv-' + l.id" [attr.aria-pressed]="isOn(l.id)" (click)="toggle(l.id)">
          {{ l.label }} <span class="count">{{ counts()[l.id] }}</span>
        </button>
      }
      <input type="search" placeholder="Search message, category or exception" aria-label="Search logs"
             [ngModel]="q()" (ngModelChange)="onSearch($event)">
    </div>

    <div class="logs-toolbar" role="group" aria-label="Date filter">
      <span class="label">Date</span>
      <button type="button" class="logs-btn small" (click)="shiftDay(-1)" aria-label="Previous day">◀</button>
      <input type="date" aria-label="From date" [ngModel]="from()" (ngModelChange)="setDate('from', $event)">
      <span class="label">to</span>
      <input type="date" aria-label="To date" [ngModel]="to()" (ngModelChange)="setDate('to', $event)">
      <button type="button" class="logs-btn small" (click)="shiftDay(1)" aria-label="Next day">▶</button>
      <button type="button" class="logs-btn small" (click)="preset('today')">Today</button>
      <button type="button" class="logs-btn small" (click)="preset('yesterday')">Yesterday</button>
      <button type="button" class="logs-btn small" (click)="preset('week')">Last 7 days</button>
      <button type="button" class="logs-btn small" (click)="preset('all')" [disabled]="!from() && !to()">All dates</button>
      <span style="flex:1"></span>
      <select aria-label="Entries per page" [ngModel]="pageSize()" (ngModelChange)="setPageSize($event)">
        <option [ngValue]="25">25 per page</option>
        <option [ngValue]="50">50 per page</option>
        <option [ngValue]="100">100 per page</option>
      </select>
      <label class="auto-refresh"><input type="checkbox" [ngModel]="auto()" (ngModelChange)="setAuto($event)"> Auto-refresh</label>
      <button type="button" class="logs-btn" (click)="refresh()" [disabled]="loading()">Refresh</button>
    </div>

    @if (error()) { <p class="logs-error" role="alert">{{ error() }}</p> }

    <table class="logs-table">
      <thead><tr><th class="c-time">Time</th><th class="c-level">Level</th><th class="c-cat">Category</th><th>Message</th></tr></thead>
      <tbody>
        @for (e of entries(); track e.id) {
          <tr class="entry" [class]="'entry lv-' + e.level" [class.open]="openId() === e.id" tabindex="0" role="button"
              [attr.aria-expanded]="openId() === e.id" (click)="open(e)" (keydown.enter)="open(e)">
            <td class="c-time">{{ formatTime(e.timestamp) }}</td>
            <td class="c-level"><span class="log-badge" [class]="'log-badge lv-' + e.level">{{ e.level }}</span></td>
            <td class="c-cat" [title]="e.category">{{ shortCategory(e.category) }}</td>
            <td class="c-msg">{{ firstLine(e.message) }}</td>
          </tr>
          @if (openId() === e.id) {
            <tr class="detail"><td colspan="4">
              <div class="log-detail">
                <dl>
                  <dt>Time</dt><dd>{{ formatTime(e.timestamp) }} (local) · {{ e.timestamp }}</dd>
                  <dt>Category</dt><dd>{{ e.category }}</dd>
                  @if (e.traceId) { <dt>Trace</dt><dd>{{ e.traceId }}</dd> }
                  <dt>Message</dt><dd><pre>{{ e.message }}</pre></dd>
                  @if (e.exception) { <dt>Exception</dt><dd><pre>{{ e.exception }}</pre></dd> }
                </dl>
              </div>
            </td></tr>
          }
        } @empty {
          <tr><td colspan="4" class="logs-empty">{{ loading() ? 'Loading…' : emptyText() }}</td></tr>
        }
      </tbody>
    </table>

    <div class="logs-pager">
      <span>{{ entries().length }} entr{{ entries().length === 1 ? 'y' : 'ies' }} shown · page {{ pageNumber() }}</span>
      <span>
        <button type="button" class="logs-btn" (click)="newer()" [disabled]="pageNumber() <= 1 || loading()">◀ Newer</button>
        <button type="button" class="logs-btn" (click)="older()" [disabled]="!nextCursor() || loading()">Older ▶</button>
      </span>
    </div>
  `
})
export class LogsPageComponent {
  private readonly api = inject(AdminApi);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private refreshTimer: ReturnType<typeof setInterval> | undefined;
  private requestId = 0;
  /** Cursor that opens each page already visited; the first page has none. */
  private readonly cursors = signal<(string | null)[]>([null]);

  protected readonly levels = LEVELS;
  protected readonly selectedLevels = signal<LogLevelGroup[]>(['info', 'warning', 'error']);
  protected readonly q = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly pageSize = signal(50);
  protected readonly auto = signal(false);

  protected readonly entries = signal<LogEntry[]>([]);
  protected readonly counts = signal<LogPage['counts']>({ info: 0, warning: 0, error: 0 });
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly openId = signal<number | null>(null);
  protected readonly pageNumber = computed(() => this.cursors().length);
  protected readonly emptyText = computed(() =>
    this.selectedLevels().length === 0 ? 'Select at least one level.' : 'No log entries match these filters.');

  constructor() {
    void this.load();
    inject(DestroyRef).onDestroy(() => { clearTimeout(this.searchTimer); clearInterval(this.refreshTimer); });
  }

  protected isOn(level: LogLevelGroup): boolean { return this.selectedLevels().includes(level); }

  protected toggle(level: LogLevelGroup): void {
    this.selectedLevels.update(current => current.includes(level) ? current.filter(x => x !== level) : [...current, level]);
    this.restart();
  }

  protected onSearch(value: string): void {
    this.q.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.restart(), 300);
  }

  protected setDate(kind: 'from' | 'to', value: string): void {
    this[kind].set(value ?? '');
    this.restart();
  }

  /** Moves a single-day (or open) selection by whole days; both ends move together. */
  protected shiftDay(days: number): void {
    const base = this.from() || this.to() || dateInputValue(new Date());
    const [y, m, d] = base.split('-').map(Number);
    const moved = dateInputValue(new Date(y, m - 1, d + days));
    this.from.set(moved);
    this.to.set(moved);
    this.restart();
  }

  protected preset(kind: 'today' | 'yesterday' | 'week' | 'all'): void {
    const today = new Date();
    const day = (offset: number) => dateInputValue(new Date(today.getFullYear(), today.getMonth(), today.getDate() + offset));
    if (kind === 'all') { this.from.set(''); this.to.set(''); }
    else if (kind === 'today') { this.from.set(day(0)); this.to.set(day(0)); }
    else if (kind === 'yesterday') { this.from.set(day(-1)); this.to.set(day(-1)); }
    else { this.from.set(day(-6)); this.to.set(day(0)); }
    this.restart();
  }

  protected setPageSize(size: number): void { this.pageSize.set(Number(size)); this.restart(); }

  protected setAuto(on: boolean): void {
    this.auto.set(on);
    clearInterval(this.refreshTimer);
    // Only the first page follows new entries; reading older pages must not jump under the reader's eyes.
    if (on) this.refreshTimer = setInterval(() => { if (this.pageNumber() === 1 && !this.loading()) void this.load(true); }, 10_000);
  }

  protected refresh(): void { void this.load(); }

  protected older(): void {
    const next = this.nextCursor();
    if (!next) return;
    this.cursors.update(c => [...c, next]);
    void this.load();
  }

  protected newer(): void {
    if (this.cursors().length <= 1) return;
    this.cursors.update(c => c.slice(0, -1));
    void this.load();
  }

  protected open(entry: LogEntry): void { this.openId.set(this.openId() === entry.id ? null : entry.id); }

  protected formatTime(iso: string): string {
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString('sv-SE');
  }

  protected shortCategory(category: string): string {
    const parts = category.split('.');
    return parts.length > 2 ? '…' + parts.slice(-2).join('.') : category;
  }

  protected firstLine(message: string): string { return message.split('\n', 1)[0]; }

  private restart(): void {
    this.cursors.set([null]);
    this.openId.set(null);
    void this.load();
  }

  private async load(quiet = false): Promise<void> {
    const request = ++this.requestId;
    if (this.selectedLevels().length === 0) {
      // The API treats an empty level list as "all", so nothing selected is answered here.
      this.entries.set([]); this.nextCursor.set(null); this.loading.set(false); this.error.set('');
      return;
    }
    if (!quiet) this.loading.set(true);
    this.error.set('');
    try {
      const page = await this.api.listLogs({
        levels: this.selectedLevels(),
        from: this.from() ? startOfDay(this.from()) : null,
        // The end date is inclusive in the UI and exclusive in the API: up to the start of the next day.
        to: this.to() ? startOfDay(this.to(), 1) : null,
        q: this.q(), cursor: this.cursors()[this.cursors().length - 1], pageSize: this.pageSize()
      });
      if (request !== this.requestId) return; // a newer request superseded this one
      this.entries.set(page.items);
      this.counts.set(page.counts);
      this.nextCursor.set(page.nextCursor);
    } catch (e) {
      if (request === this.requestId) this.error.set(errorMessage(e));
    } finally {
      if (request === this.requestId) this.loading.set(false);
    }
  }
}
