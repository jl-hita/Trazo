import { Component, ViewEncapsulation, computed, input } from '@angular/core';

export interface BarRow { label: string; value: number; text: string; low?: number | null; high?: number | null; dim?: boolean; hint?: string; }

/** Horizontal bars, one per row, with an optional confidence range drawn over the bar. */
@Component({
  selector: 'mk-bars',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="mk-bars" role="list">
      @for (r of rows(); track r.label) {
        <div class="mk-bar-row" role="listitem" [attr.title]="r.hint ?? null">
          <span class="mk-bar-label">{{ r.label }}</span>
          <span class="mk-bar-track">
            <span class="mk-bar-fill" [class.dim]="r.dim" [style.width.%]="width(r.value)"></span>
            @if (r.low != null && r.high != null) {
              <span class="mk-bar-ci" [style.left.%]="width(r.low)" [style.width.%]="width(r.high) - width(r.low)"></span>
            }
          </span>
          <span class="mk-bar-value">{{ r.text }}</span>
        </div>
      }
    </div>
  `
})
export class BarsComponent {
  readonly rows = input.required<BarRow[]>();
  /** Scale end; by default the largest value (or confidence end) shown. */
  readonly max = input<number | null>(null);
  private readonly top = computed(() => this.max() ?? Math.max(0.0001, ...this.rows().map(r => Math.max(r.value, r.high ?? 0))));
  protected width(v: number): number { return Math.max(0, Math.min(100, (v / this.top()) * 100)); }
}

export interface Series { name: string; color: string; values: number[]; }

/** Grouped vertical bars over time (one group per week). Values are also readable from the table underneath each chart. */
@Component({
  selector: 'mk-columns',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `
    <figure class="mk-columns">
      <svg [attr.viewBox]="'0 0 ' + width() + ' ' + height" role="img" [attr.aria-label]="label()" preserveAspectRatio="none">
        @for (g of groups(); track g.label; let i = $index) {
          @for (s of series(); track s.name; let j = $index) {
            <rect [attr.x]="g.x + j * g.bar" [attr.y]="y(s.values[i])" [attr.width]="g.bar - 1" [attr.height]="height - 18 - y(s.values[i])"
                  [attr.fill]="s.color" rx="1.5"><title>{{ s.name }} · {{ g.label }}: {{ s.values[i] }}</title></rect>
          }
          <text [attr.x]="g.x + g.slot / 2" [attr.y]="height - 4" text-anchor="middle" class="mk-axis">{{ g.short }}</text>
        }
        <line x1="0" [attr.x2]="width()" [attr.y1]="height - 18" [attr.y2]="height - 18" class="mk-baseline" />
      </svg>
      <figcaption class="mk-legend">
        @for (s of series(); track s.name) { <span><i [style.background]="s.color"></i>{{ s.name }}</span> }
        <span class="mk-muted">peak {{ peak() }}</span>
      </figcaption>
    </figure>
  `
})
export class ColumnsComponent {
  readonly labels = input.required<string[]>();
  readonly series = input.required<Series[]>();
  readonly label = input('Weekly chart');
  protected readonly height = 150;
  protected readonly width = computed(() => Math.max(300, this.labels().length * (this.series().length * 9 + 10)));
  protected readonly peak = computed(() => Math.max(1, ...this.series().flatMap(s => s.values)));
  protected readonly groups = computed(() => {
    const n = Math.max(1, this.labels().length);
    const slot = this.width() / n;
    const bar = Math.max(4, (slot - 6) / Math.max(1, this.series().length));
    return this.labels().map((label, i) => ({ label, short: label.slice(5), x: i * slot + 3, slot, bar }));
  });
  protected y(value: number): number {
    const usable = this.height - 18 - 6;
    return this.height - 18 - (value / this.peak()) * usable;
  }
}
