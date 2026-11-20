import { Component, DestroyRef, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import type { LogEntry, LogLevelGroup, LogPage } from '../../core/admin.models';
import { LogsApi } from './logs.api';
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
  styleUrl: './logs.page.scss',
  templateUrl: './logs.page.html'
})
export class LogsPageComponent {
  private readonly api = inject(LogsApi);
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
    this.applyQueryParams();
    void this.load();
    inject(DestroyRef).onDestroy(() => { clearTimeout(this.searchTimer); clearInterval(this.refreshTimer); });
  }

  /**
   * Filters passed in the URL (links from the overview): <c>levels</c> (comma separated), <c>range=24h</c> (from yesterday's
   * date, the closest whole-day filter of this page) and <c>q</c>. Unknown values are ignored.
   */
  private applyQueryParams(): void {
    const query = inject(ActivatedRoute).snapshot.queryParamMap;
    const levels = (query.get('levels') ?? '').split(',').map(x => x.trim())
      .filter((x): x is LogLevelGroup => LEVELS.some(l => l.id === x));
    if (levels.length > 0) this.selectedLevels.set(levels);
    if (query.get('range') === '24h') {
      const now = new Date();
      this.from.set(dateInputValue(new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1)));
    }
    const q = query.get('q');
    if (q) this.q.set(q.slice(0, 100));
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
      const page = await this.api.list({
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
