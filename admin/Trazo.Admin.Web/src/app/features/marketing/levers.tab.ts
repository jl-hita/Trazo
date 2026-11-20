import { UI_COPY } from '../../core/ui-copy';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MarketingApi, fmt, marketingError } from './marketing.api';
import type { DownloadsReport, PaywallReport, Range, WaitlistReport } from './marketing.models';
import { BarRow, BarsComponent, ColumnsComponent, Series } from './marketing.charts';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiTableComponent } from '../../shared/ui/ui-table.component';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';

const SOURCE_LABELS: Record<string, string> = { ...UI_COPY.marketing.sourceLabels };

/** The levers that are not campaigns: the Premium offer (where people see it), the invitation programme and the landing waitlist. */
@Component({
  selector: 'mk-levers',
  standalone: true,
  imports: [FormsModule, BarsComponent, ColumnsComponent, UiButtonComponent, UiTableComponent, UiFormFieldComponent],
  templateUrl: './levers.tab.html'
})
export class LeversTabComponent {
  protected readonly labels = UI_COPY.marketing;

  private readonly api = inject(MarketingApi);
  readonly range = input.required<Range>();
  protected readonly fmt = fmt;
  protected readonly paywall = signal<PaywallReport | null>(null);
  protected readonly waitlist = signal<WaitlistReport | null>(null);
  protected readonly downloads = signal<DownloadsReport | null>(null);
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected dlWeek = new Date().toISOString().slice(0, 10);
  protected dlCount: number | null = null;
  protected dlNote = '';

  protected readonly sourceRows = computed<BarRow[]>(() =>
    (this.paywall()?.sources ?? []).filter(s => s.views > 0).map(s => ({ label: SOURCE_LABELS[s.source] ?? s.source, value: s.share, text: `${s.views} · ${fmt.pct(s.share, 0)}` })));
  protected readonly paywallLabels = computed(() => this.paywall()?.weeks.map(w => w.week) ?? []);
  protected readonly paywallSeries = computed<Series[]>(() => [{ name: this.labels.timesOpened, color: '#5b8fcb', values: this.paywall()?.weeks.map(w => w.total) ?? [] }]);
  protected readonly languages = computed(() => Object.entries(this.waitlist()?.byLanguage ?? {}));
  protected readonly waitLabels = computed(() => this.waitlist()?.weeks.map(w => w.week) ?? []);
  protected readonly waitSeries = computed<Series[]>(() => [{ name: this.labels.signups, color: '#4fae78', values: this.waitlist()?.weeks.map(w => w.joined) ?? [] }]);

  constructor() {
    effect(() => { const r = this.range(); void this.load(r); });
  }

  protected async saveDownloads(): Promise<void> {
    if (this.dlCount === null) return;
    this.error.set(''); this.message.set('');
    try {
      const saved = await this.api.saveDownloads({ week: this.dlWeek, downloads: this.dlCount, note: this.dlNote.trim() || null });
      this.message.set(`${this.labels.savedDownloads}: ${saved.downloads} ${this.labels.downloadsLabel} ${this.labels.forTheWeek} ${saved.week}.`);
      this.dlCount = null; this.dlNote = '';
      await this.load(this.range());
    } catch (e) { this.error.set(marketingError(e)); }
  }

  protected async removeDownloads(week: string): Promise<void> {
    this.error.set(''); this.message.set('');
    try { await this.api.deleteDownloads(week); await this.load(this.range()); } catch (e) { this.error.set(marketingError(e)); }
  }

  private async load(range: Range): Promise<void> {
    try {
      const [p, w, dl] = await Promise.all([this.api.paywall(range), this.api.waitlist(), this.api.downloads(range)]);
      this.paywall.set(p); this.waitlist.set(w); this.downloads.set(dl);
    } catch (e) { this.error.set(marketingError(e)); }
  }
}
