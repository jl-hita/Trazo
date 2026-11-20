import { UI_COPY } from '../../core/ui-copy';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MarketingApi, fmt, marketingError } from './marketing.api';
import type { EconomicsReport, Range } from './marketing.models';
import { BarRow, BarsComponent } from './marketing.charts';
import { UiTableComponent } from '../../shared/ui/ui-table.component';

/**
 * Unit economics: what winning a paying customer costs, what that customer brings per month, how long they keep paying and whether the
 * strategy pays back. It is an estimate built on observed renewals and the Google Play prices of the tax settings.
 */
@Component({
  selector: 'mk-economics',
  standalone: true,
  imports: [BarsComponent, UiTableComponent],
  templateUrl: './economics.tab.html'
})
export class EconomicsTabComponent {
  protected readonly labels = UI_COPY.marketing;

  private readonly api = inject(MarketingApi);
  readonly range = input.required<Range>();
  protected readonly fmt = fmt;
  protected readonly data = signal<EconomicsReport | null>(null);
  protected readonly error = signal('');
  protected readonly survival = computed<BarRow[]>(() =>
    (this.data()?.survival ?? []).filter(p => p.retained !== null)
      .map(p => ({ label: p.months === 0 ? this.labels.firstMonth : `${this.labels.after} ${p.months} ${p.months === 1 ? this.labels.monthSingular : this.labels.monthPlural}`, value: p.retained ?? 0, text: `${fmt.pct(p.retained, 0)} of ${p.eligible}` })));

  constructor() {
    effect(() => { const r = this.range(); void this.load(r); });
  }

  private async load(range: Range): Promise<void> {
    this.error.set('');
    try { this.data.set(await this.api.economics(range)); } catch (e) { this.error.set(marketingError(e)); }
  }
}

