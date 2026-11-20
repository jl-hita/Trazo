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
  styleUrl: './marketing.page.scss',
  templateUrl: './marketing.page.html'
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
