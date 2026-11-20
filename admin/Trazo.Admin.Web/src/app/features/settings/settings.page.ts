import { Component, inject, signal } from '@angular/core';
import { SetupApi, SettingsView, errorMessage } from '../../core/setup.api';
import { ConfigFormComponent } from '../setup/config-form.component';
import { PlayBillingFormComponent } from './play-billing-form.component';
import { OperationsFormComponent } from './operations-form.component';
import { PushFormComponent } from './push-form.component';
import { PromotionsFormComponent } from './promotions-form.component';

@Component({
  selector: 'trazo-settings-page',
  standalone: true,
  imports: [ConfigFormComponent, PlayBillingFormComponent, PromotionsFormComponent, PushFormComponent, OperationsFormComponent],
  templateUrl: './settings.page.html'
})
export class SettingsPageComponent {
  protected readonly settings = signal<SettingsView | null>(null);
  protected readonly error = signal('');

  constructor() {
    inject(SetupApi).getSettings().then(s => this.settings.set(s), e => this.error.set(errorMessage(e)));
  }
}
