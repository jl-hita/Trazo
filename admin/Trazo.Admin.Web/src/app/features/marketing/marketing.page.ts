import { UI_COPY } from '../../core/ui-copy';
import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import type { Range } from './marketing.models';
import { daysAgo, isoDay } from './marketing.api';
import { OverviewTabComponent } from './overview.tab';
import { StrategiesTabComponent } from './strategies.tab';
import { CampaignsTabComponent } from './campaigns.tab';
import { FunnelTabComponent } from './funnel.tab';
import { LeversTabComponent } from './levers.tab';
import { EconomicsTabComponent } from './economics.tab';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';

type Tab = 'overview' | 'strategies' | 'economics' | 'campaigns' | 'funnel' | 'levers';

/**
 * Marketing panel for the commercial role (and the superadmin): which acquisition strategies bring people who sign up, train, pay and stay.
 * Everything shown is an aggregate; nobody can be identified from it. All tabs share one period.
 */
@Component({
  selector: 'trazo-marketing-page',
  standalone: true,
  imports: [FormsModule, OverviewTabComponent, StrategiesTabComponent, CampaignsTabComponent, FunnelTabComponent, LeversTabComponent, EconomicsTabComponent, UiButtonComponent, UiFormFieldComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './marketing.page.scss',
  templateUrl: './marketing.page.html'
})
export class MarketingPageComponent {
  protected readonly labels = UI_COPY.marketing;

  protected readonly auth = inject(AuthService);
  protected readonly presets = [{ days: 7, label: UI_COPY.marketing.days7 }, { days: 30, label: UI_COPY.marketing.days30 }, { days: 90, label: UI_COPY.marketing.days90 }, { days: 365, label: UI_COPY.marketing.months12 }];
  protected readonly tabs: { key: Tab; label: string }[] = [
    { key: 'overview', label: UI_COPY.marketing.tabs.overview }, { key: 'strategies', label: UI_COPY.marketing.tabs.strategies },
    { key: 'economics', label: UI_COPY.marketing.tabs.economics }, { key: 'campaigns', label: UI_COPY.marketing.tabs.campaigns },
    { key: 'funnel', label: UI_COPY.marketing.tabs.funnel }, { key: 'levers', label: UI_COPY.marketing.tabs.levers }
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
