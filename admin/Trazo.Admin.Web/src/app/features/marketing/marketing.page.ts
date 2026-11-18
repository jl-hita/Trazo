import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import { Range, daysAgo, isoDay } from './marketing.api';
import { OverviewTabComponent } from './overview.tab';
import { StrategiesTabComponent } from './strategies.tab';
import { CampaignsTabComponent } from './campaigns.tab';
import { FunnelTabComponent } from './funnel.tab';
import { LeversTabComponent } from './levers.tab';
import { EconomicsTabComponent } from './economics.tab';

type Tab = 'overview' | 'strategies' | 'economics' | 'campaigns' | 'funnel' | 'levers';

/**
 * Marketing panel for the commercial role (and the superadmin): which acquisition strategies bring people who sign up, train, pay and stay.
 * Everything shown is an aggregate; nobody can be identified from it. All tabs share one period.
 */
@Component({
  selector: 'trazo-marketing-page',
  standalone: true,
  imports: [FormsModule, OverviewTabComponent, StrategiesTabComponent, CampaignsTabComponent, FunnelTabComponent, LeversTabComponent, EconomicsTabComponent],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .mk { max-width: 1280px; }
    .mk-toolbar { display: flex; flex-wrap: wrap; gap: 10px; align-items: end; margin-bottom: 14px; }
    .mk-toolbar label, .mk-field { display: grid; gap: 4px; color: #8291a6; font-size: 11px; }
    .mk input, .mk select, .mk textarea { padding: 8px 10px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; }
    .mk input:focus, .mk select:focus, .mk textarea:focus, .mk button:focus-visible { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .mk-presets { display: flex; gap: 6px; }
    .mk-btn { padding: 8px 12px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .mk-btn:hover:not(:disabled) { background: #22364f; }
    .mk-btn:disabled { opacity: .45; cursor: not-allowed; }
    .mk-btn.on { border-color: #6d9bd2; background: #3b6ea8; color: #fff; }
    .mk-btn.danger { border-color: #7a3b3b; color: #f0a3a3; background: #2c1717; }
    .mk-tabs { display: flex; flex-wrap: wrap; gap: 4px; border-bottom: 1px solid #293444; margin-bottom: 18px; }
    .mk-tab { padding: 10px 16px; border: 0; border-bottom: 2px solid transparent; background: transparent; color: #8291a6; font-size: 13px; cursor: pointer; }
    .mk-tab:hover { color: #e8edf5; }
    .mk-tab.on { color: #e8edf5; border-bottom-color: #6d9bd2; }
    .mk h2 { margin: 24px 0 10px; font-size: 13px; color: #a9c2df; letter-spacing: .08em; text-transform: uppercase; }
    .mk h2:first-child { margin-top: 0; }
    .mk-note { margin: 0 0 12px; color: #aab6c7; font-size: 12.5px; line-height: 1.55; max-width: 80ch; }
    .mk-muted { color: #8291a6; font-size: 11.5px; }
    .mk-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 12px; margin-bottom: 18px; }
    .mk-card { padding: 14px; border: 1px solid #293444; border-radius: 8px; background: #131b27; }
    .mk-card b { display: block; font-size: 24px; font-variant-numeric: tabular-nums; color: #f2f5fa; }
    .mk-card span { color: #8291a6; font-size: 11px; letter-spacing: .04em; }
    .mk-card .delta { display: block; margin-top: 4px; font-size: 11.5px; }
    .mk-up { color: #7fd3a0; } .mk-down { color: #f0a3a3; } .mk-flat { color: #8291a6; }
    .mk-grid2 { display: grid; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); gap: 18px; }
    .mk-panel { padding: 14px 16px; border: 1px solid #293444; border-radius: 8px; background: #131b27; }
    .mk-table { width: 100%; border-collapse: collapse; border: 1px solid #293444; background: #131b27; font-size: 12.5px; }
    .mk-table th { padding: 9px 10px; text-align: right; color: #8291a6; font-size: 10px; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; border-bottom: 1px solid #293444; white-space: nowrap; }
    .mk-table th:first-child, .mk-table td:first-child { text-align: left; }
    .mk-table td { padding: 9px 10px; text-align: right; border-bottom: 1px solid #1d2735; color: #dbe5f2; font-variant-numeric: tabular-nums; }
    .mk-table tr:last-child td { border-bottom: 0; }
    .mk-table tr.clickable { cursor: pointer; }
    .mk-table tr.clickable:hover td { background: #182231; }
    .mk-table .name { display: block; color: #f2f5fa; }
    .mk-table .sub { display: block; color: #8291a6; font-size: 11px; }
    .mk-scroll { overflow-x: auto; }
    .mk-ci { display: block; color: #8291a6; font-size: 10.5px; }
    .mk-flag { display: inline-block; margin-left: 6px; padding: 1px 6px; border: 1px solid #6a5a2e; border-radius: 999px; color: #e6d39b; font-size: 10px; }
    .mk-badge { display: inline-block; padding: 2px 8px; border-radius: 999px; border: 1px solid #34465b; color: #aab6c7; font-size: 11px; }
    .mk-badge.active { border-color: #3d7a55; color: #9fd9b0; } .mk-badge.paused { border-color: #8a7a3a; color: #e6d39b; } .mk-badge.archived { color: #6b7a8e; }
    .mk-insights { display: grid; gap: 8px; margin-bottom: 18px; }
    .mk-insight { padding: 10px 14px; border: 1px solid #293444; border-left-width: 4px; border-radius: 6px; background: #131b27; }
    .mk-insight.good { border-left-color: #4fae78; } .mk-insight.warn { border-left-color: #d6a83e; } .mk-insight.info { border-left-color: #6d9bd2; }
    .mk-insight b { display: block; color: #f2f5fa; font-size: 13px; }
    .mk-insight span { color: #aab6c7; font-size: 12.5px; line-height: 1.5; }
    .mk-err { color: #f0a3a3; font-size: 13px; margin: 0 0 12px; } .mk-ok { color: #9fd9b0; font-size: 13px; margin: 0 0 12px; }
    .mk-empty { padding: 28px 0; text-align: center; color: #8291a6; }
    .mk-bars { display: grid; gap: 7px; }
    .mk-bar-row { display: grid; grid-template-columns: minmax(90px, 190px) 1fr 74px; align-items: center; gap: 10px; font-size: 12.5px; }
    .mk-bar-label { color: #dbe5f2; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .mk-bar-track { position: relative; height: 14px; border-radius: 3px; background: #0d1119; }
    .mk-bar-fill { position: absolute; inset: 0 auto 0 0; border-radius: 3px; background: #5b8fcb; }
    .mk-bar-fill.dim { background: #3d5470; }
    .mk-bar-ci { position: absolute; top: 5px; height: 4px; background: #e8edf5; opacity: .55; border-radius: 2px; }
    .mk-bar-value { text-align: right; color: #e8edf5; font-variant-numeric: tabular-nums; }
    .mk-columns { margin: 0; } .mk-columns svg { width: 100%; height: 150px; display: block; }
    .mk-axis { fill: #8291a6; font-size: 9px; } .mk-baseline { stroke: #34465b; stroke-width: 1; }
    .mk-legend { display: flex; flex-wrap: wrap; gap: 14px; margin-top: 6px; color: #aab6c7; font-size: 11.5px; }
    .mk-legend i { display: inline-block; width: 10px; height: 10px; margin-right: 5px; border-radius: 2px; vertical-align: -1px; }
    .mk-heat td.cell { color: #0b1220; font-weight: 600; min-width: 52px; }
    .mk-form { display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: 12px; align-items: end; }
    .mk-form .wide { grid-column: 1 / -1; } .mk-form textarea { min-height: 64px; resize: vertical; }
    .mk-link { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .mk-link code { padding: 4px 8px; border-radius: 4px; background: #0d1119; color: #b9d3f0; font-size: 12px; word-break: break-all; }
    .mk-help summary { cursor: pointer; color: #a9c2df; font-size: 13px; }
    .mk-help ul { margin: 8px 0 0; padding-left: 18px; color: #aab6c7; font-size: 12.5px; line-height: 1.6; }
  `],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">MARKETING</p>
        <h1>Marketing panel</h1>
        <p class="subtitle">Which strategies bring people who sign up, train, pay and stay. Aggregated data only: no person can be identified from anything shown here.</p>
      </div>
    </div>
    <div class="mk">
      <div class="mk-toolbar">
        <div class="mk-presets" role="group" aria-label="Period presets">
          @for (p of presets; track p.days) {
            <button type="button" class="mk-btn" [class.on]="preset() === p.days" (click)="usePreset(p.days)">{{ p.label }}</button>
          }
        </div>
        <label>From <input type="date" [ngModel]="from()" (ngModelChange)="custom($event, to())" name="from"></label>
        <label>To (included) <input type="date" [ngModel]="to()" (ngModelChange)="custom(from(), $event)" name="to"></label>
        @if (invalid()) { <span class="mk-err" role="alert">The start must be before the end.</span> }
      </div>
      <div class="mk-tabs" role="tablist">
        @for (t of tabs; track t.key) {
          <button type="button" role="tab" class="mk-tab" [class.on]="tab() === t.key" [attr.aria-selected]="tab() === t.key" (click)="tab.set(t.key)">{{ t.label }}</button>
        }
      </div>
      @if (!invalid()) {
        @switch (tab()) {
          @case ('overview') { <mk-overview [range]="range()" /> }
          @case ('strategies') { <mk-strategies [range]="range()" (openCampaign)="tab.set('campaigns')" /> }
          @case ('economics') { <mk-economics [range]="range()" /> }
          @case ('campaigns') { <mk-campaigns [range]="range()" /> }
          @case ('funnel') { <mk-funnel [range]="range()" /> }
          @case ('levers') { <mk-levers [range]="range()" /> }
        }
      }
    </div>
  `
})
export class MarketingPageComponent {
  protected readonly auth = inject(AuthService);
  protected readonly presets = [{ days: 7, label: '7 days' }, { days: 30, label: '30 days' }, { days: 90, label: '90 days' }, { days: 365, label: '12 months' }];
  protected readonly tabs: { key: Tab; label: string }[] = [
    { key: 'overview', label: 'Overview' }, { key: 'strategies', label: 'Strategies' }, { key: 'economics', label: 'Unit economics' }, { key: 'campaigns', label: 'Campaigns' },
    { key: 'funnel', label: 'Funnel & retention' }, { key: 'levers', label: 'Conversion & store' }
  ];
  protected readonly tab = signal<Tab>('overview');
  protected readonly preset = signal<number | null>(30);
  protected readonly from = signal(daysAgo(29));
  protected readonly to = signal(isoDay(new Date()));
  protected readonly invalid = computed(() => !this.from() || !this.to() || this.from() > this.to());
  protected readonly range = computed<Range>(() => ({ from: this.from(), to: this.to() }));

  protected usePreset(days: number): void {
    this.preset.set(days);
    this.from.set(daysAgo(days - 1));
    this.to.set(isoDay(new Date()));
  }

  protected custom(from: string, to: string): void {
    this.preset.set(null);
    this.from.set(from);
    this.to.set(to);
  }
}
