import { Component, inject, signal } from '@angular/core';
import { SetupApi, SettingsView } from '../../core/setup.api';
import { errorMessage } from '../../core/api-error';
import { ConfigFormComponent } from '../setup/config-form.component';
import { PlayBillingFormComponent } from './play-billing-form.component';
import { OperationsFormComponent } from './operations-form.component';
import { PushFormComponent } from './push-form.component';
import { PromotionsFormComponent } from './promotions-form.component';
import { SETTINGS_PAGE_COPY } from './settings.page.copy';

@Component({
  selector: 'trazo-settings-page',
  standalone: true,
  imports: [ConfigFormComponent, PlayBillingFormComponent, PromotionsFormComponent, PushFormComponent, OperationsFormComponent],
  templateUrl: './settings.page.html'
})
export class SettingsPageComponent {
  protected readonly copy = SETTINGS_PAGE_COPY;
  protected readonly settings = signal<SettingsView | null>(null);
  protected readonly error = signal('');

  constructor() {
    inject(SetupApi).getSettings().then(s => this.settings.set(s), e => this.error.set(errorMessage(e)));
  }
}
